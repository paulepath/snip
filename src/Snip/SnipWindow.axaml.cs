using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Snip;

/// <summary>
/// One image being marked up: the tools down the left, the colour and size along the top with Undo, Copy and Save,
/// and the canvas. Copy and Save give the crop with every mark. Closing asks first only if there are marks that
/// haven't been copied or saved.
/// </summary>
public partial class SnipWindow : Window
{
    sealed record ToolInfo(Tool Tool, string Icon, string Label, Key Key, string Hint);

    static readonly ToolInfo[] Tools =
    [
        new(Tool.Select, "SelectIcon", "Select", Key.V, "Click a mark to pick it: drag to move it, Delete removes it, the colour and size change it. Double-click text to edit it."),
        new(Tool.Crop, "CropIcon", "Crop", Key.C, "Drag over the part to keep. Esc removes the crop."),
        new(Tool.Arrow, "ArrowIcon", "Arrow", Key.A, "Drag from the tail to the point. Shift snaps to 45°."),
        new(Tool.Rect, "RectIcon", "Box", Key.R, "Drag a box. Shift makes it square."),
        new(Tool.Ellipse, "EllipseIcon", "Ellipse", Key.E, "Drag an ellipse. Shift makes it a circle."),
        new(Tool.Pen, "PenIcon", "Pen", Key.P, "Draw freehand."),
        new(Tool.Text, "TextIcon", "Text", Key.T, "Click where the text goes, type, then Enter (Shift+Enter for a new line)."),
        new(Tool.Highlight, "HighlightIcon", "Highlight", Key.H, "Drag over what to highlight."),
        new(Tool.Pixelate, "PixelateIcon", "Pixelate", Key.X, "Drag over what to hide: it's blurred into blocks."),
        new(Tool.Step, "StepIcon", "Step", Key.S, "Click to number the steps: 1, 2, 3… Deleting one renumbers the rest."),
    ];

    static readonly string[] SwatchNames = ["Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Black", "White"];

    readonly SnipSettings _settings;
    readonly Dictionary<Tool, ToggleButton> _toolButtons = [];
    readonly List<(RadioButton Button, Rgba Colour)> _swatches = [];
    UndoStack<SnipDocument>? _history;
    PixelBuffer? _pixels;
    ImmutableList<PixelateBox> _shownPixelate = [];
    Bitmap? _shown;
    SnipDocument? _copied;
    TextBox? _textBox;
    (Pt At, int Index) _textEdit;
    bool _syncing, _allowClose;
    string? _flash;

    /// <summary>The file the image came from (and Save writes to), or null for a pasted one.</summary>
    public string? FilePath { get; private set; }

    /// <summary>Marks that haven't been copied or saved: closing would ask first.</summary>
    public bool HasUnsentMarks => _history is { IsDirty: true } && !ReferenceEquals(_history.Current, _copied);

    /// <summary>For the designer and the XAML loader only.</summary>
    public SnipWindow() : this(new SnipSettings()) { }

    public SnipWindow(SnipSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        ExtendClientAreaToDecorationsHint = true;
        Opened += (_, _) => Dispatcher.UIThread.Post(HideDrawnTitle, DispatcherPriority.Loaded);

        foreach (var info in Tools)
        {
            var button = new ToggleButton
            {
                Classes = { "tool" },
                Content = new StackPanel
                {
                    Children =
                    {
                        new PathIcon { Data = Application.Current?.FindResource(info.Icon) as Geometry },
                        new TextBlock { Classes = { "label" }, Text = info.Label },
                    },
                },
            };
            ToolTip.SetTip(button, $"{info.Label} ({info.Key})");
            button.Click += (_, _) => SetTool(info.Tool);
            _toolButtons[info.Tool] = button;
            ToolRail.Children.Add(button);
        }

        for (var i = 0; i < Rgba.Swatches.Count; i++)
        {
            var colour = Rgba.Swatches[i];
            var swatch = new RadioButton { Classes = { "swatch" }, GroupName = "colour", Background = new SolidColorBrush(Renderer.ToColor(colour)) };
            ToolTip.SetTip(swatch, SwatchNames[i]);
            swatch.IsCheckedChanged += (_, _) => { if (swatch.IsChecked == true && !_syncing) Restyle(Canvas.MarkStyle with { Colour = colour }); };
            _swatches.Add((swatch, colour));
            Swatches.Children.Add(swatch);
        }
        WidthSlider.ValueChanged += (_, e) =>
        {
            WidthLabel.Text = ((int)e.NewValue).ToString();
            if (!_syncing) Restyle(Canvas.MarkStyle with { Width = e.NewValue });
        };

        UndoButton.Click += (_, _) => _history?.Undo();
        RedoButton.Click += (_, _) => _history?.Redo();
        OpenButton.Click += async (_, _) => await OpenAsync();
        CopyButton.Click += (_, _) => Copy();
        SaveButton.Click += async (_, _) => await SaveAsync(saveAs: false);
        SaveAsButton.Click += async (_, _) => await SaveAsync(saveAs: true);
        FolderButton.Click += (_, _) => Reveal();
        ThemeButton.Click += (_, _) => NextTheme();
        ZoomButton.Click += (_, _) => Canvas.ZoomToFit();

        Canvas.SelectionChanged += ShowSelectedStyle;
        Canvas.ZoomChanged += ShowStatus;
        Canvas.TextRequested += BeginText;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) => { if (e.Key == Key.Space) Canvas.SpaceHeld = false; }, RoutingStrategies.Tunnel);
        Closing += OnClosing;

        Canvas.MarkStyle = _settings.Style;
        ShowStyle(_settings.Style);
        ShowThemeLabel();
        RestorePlacement();
        SetTool(Tool.Arrow);
        ShowState();
    }

    // ------------------------------------------------------------------ the image

    /// <summary>Opens an image file in this window.</summary>
    public bool Load(string path)
    {
        try
        {
            SetImage(ImageIo.Load(path), path);
            Log.Info($"Opened {path}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException)
        {
            Log.Warn($"Couldn't open {path}: {ex.Message}");
            Flash($"Couldn't open {Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
    }

    public void SetImage(PixelBuffer pixels, string? path)
    {
        if (_history != null) _history.Changed -= ShowState;
        _pixels = pixels;
        FilePath = path;
        _history = new UndoStack<SnipDocument>(SnipDocument.For(pixels.Width, pixels.Height));
        _history.Changed += ShowState;
        _copied = null;
        _shownPixelate = [];
        ShowImage(force: true);
        Canvas.History = _history;
        ShowState();
    }

    /// <summary>The image as shown: rebuilt only when the pixelated boxes change.</summary>
    void ShowImage(bool force = false)
    {
        if (_pixels == null || _history == null) return;
        var boxes = _history.Current.Pixelated.ToImmutableList();
        if (!force && boxes.SequenceEqual(_shownPixelate)) return;
        _shownPixelate = boxes;
        var old = _shown;
        _shown = ImageIo.ToBitmap(boxes.IsEmpty ? _pixels : _pixels.WithPixelate(boxes));
        Canvas.Image = _shown;
        old?.Dispose();
    }

    void ShowState()
    {
        ShowImage();
        var name = FilePath != null ? Path.GetFileName(FilePath) : _pixels != null ? "Pasted image" : null;
        var dirty = _history?.IsDirty == true;
        Title = name == null ? "Snip" : $"{(dirty ? "• " : "")}{name} – Snip";
        WindowTitle.Text = Title;
        UndoButton.IsEnabled = _history?.CanUndo == true;
        RedoButton.IsEnabled = _history?.CanRedo == true;
        CopyButton.IsEnabled = SaveAsButton.IsEnabled = _pixels != null;
        SaveButton.IsEnabled = _pixels != null && (FilePath == null || dirty);
        FolderButton.IsEnabled = FilePath != null;
        ShowStatus();
    }

    void ShowStatus()
    {
        HintText.Text = _flash ?? (_pixels == null
            ? "Open an image (Ctrl+O) or paste one (Ctrl+V)."
            : Tools.First(t => t.Tool == Canvas.Tool).Hint);
        if (_history?.Current is { } doc)
        {
            var output = doc.Output;
            SizeText.Text = doc.Crop != null ? $"Crop {output.W:0} × {output.H:0} of {doc.Width} × {doc.Height}"
                : doc.OutputPastImage ? $"{output.W:0} × {output.H:0} with marks past the {doc.Width} × {doc.Height} picture"
                : $"{doc.Width} × {doc.Height}";
            ZoomButton.Content = $"{Math.Round(Canvas.Scale * 100)}%";
        }
        else
        {
            SizeText.Text = "";
            ZoomButton.Content = "";
        }
    }

    /// <summary>A message in the status line for a few seconds.</summary>
    void Flash(string text)
    {
        _flash = text;
        ShowStatus();
        DispatcherTimer.RunOnce(() => { if (_flash == text) { _flash = null; ShowStatus(); } }, TimeSpan.FromSeconds(4));
    }

    // ------------------------------------------------------------------ tools and style

    void SetTool(Tool tool)
    {
        CommitText();
        Canvas.Tool = tool;
        foreach (var (t, button) in _toolButtons)
            button.IsChecked = t == tool;
        // the highlighter has its own colour; the other tools share one
        Canvas.MarkStyle = tool == Tool.Highlight ? _settings.HighlightStyle : _settings.Style;
        ShowStyle(Canvas.MarkStyle);
        ShowStatus();
    }

    /// <summary>The colour or size changed: for new marks, and for the selected one.</summary>
    void Restyle(MarkStyle style)
    {
        Canvas.MarkStyle = style;
        if (Canvas.Tool == Tool.Highlight) _settings.HighlightStyle = style;
        else _settings.Style = style;
        if (_history?.Current is { } doc && Canvas.Selected >= 0 && Canvas.Selected < doc.Items.Count)
        {
            var item = doc.Items[Canvas.Selected].Restyled(style);
            if (item is TextLabel label)
                item = label with { Size = Renderer.Measure(label.Text, style) };
            _history.Push(doc.Replace(Canvas.Selected, item));
        }
    }

    /// <summary>Picking a mark shows its colour and size (without changing anything).</summary>
    void ShowSelectedStyle()
    {
        if (_history?.Current is { } doc && Canvas.Selected >= 0 && Canvas.Selected < doc.Items.Count)
        {
            var style = doc.Items[Canvas.Selected].Style;
            Canvas.MarkStyle = style;
            ShowStyle(style);
        }
    }

    void ShowStyle(MarkStyle style)
    {
        _syncing = true;
        foreach (var (button, colour) in _swatches)
            button.IsChecked = colour == style.Colour;
        WidthSlider.Value = style.Width;
        WidthLabel.Text = ((int)style.Width).ToString();
        _syncing = false;
    }

    // ------------------------------------------------------------------ text

    void BeginText(Pt at, int index)
    {
        CommitText();
        if (_history?.Current is not { } doc) return;
        var label = index >= 0 ? doc.Items[index] as TextLabel : null;
        var style = label?.Style ?? Canvas.MarkStyle;
        _textEdit = (label?.At ?? at, label != null ? index : -1);
        var colour = Renderer.ToColor(style.Colour);
        var light = 0.299 * colour.R + 0.587 * colour.G + 0.114 * colour.B > 170;
        var box = new TextBox
        {
            Text = label?.Text ?? "",
            AcceptsReturn = true,
            FontFamily = Renderer.TextTypeface.FontFamily,
            FontWeight = FontWeight.SemiBold,
            FontSize = style.FontSize * Canvas.Scale,
            Foreground = new SolidColorBrush(colour),
            Background = new SolidColorBrush(light ? Color.FromArgb(220, 40, 40, 40) : Color.FromArgb(225, 255, 255, 255)),
            BorderBrush = Palette.Accent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2, 0),
            MinWidth = 60,
            MinHeight = 0,
            CornerRadius = new CornerRadius(3),
        };
        var screen = Canvas.ToScreen(_textEdit.At);
        Avalonia.Controls.Canvas.SetLeft(box, screen.X - 3);
        Avalonia.Controls.Canvas.SetTop(box, screen.Y - 1);
        box.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { e.Handled = true; CommitText(); }
            else if (e.Key == Key.Escape) { e.Handled = true; CommitText(cancel: true); }
        }, RoutingStrategies.Tunnel);
        box.LostFocus += (_, _) => CommitText();
        _textBox = box;
        Overlay.Children.Add(box);
        box.Focus();
        box.CaretIndex = box.Text?.Length ?? 0;
    }

    void CommitText(bool cancel = false)
    {
        if (_textBox is not { } box) return;
        _textBox = null;
        Overlay.Children.Remove(box);
        Canvas.Focus();
        if (cancel || _history?.Current is not { } doc) return;
        var text = (box.Text ?? "").Replace("\r\n", "\n").TrimEnd();
        var (at, index) = _textEdit;
        if (index >= 0 && index < doc.Items.Count && doc.Items[index] is TextLabel label)
            _history.Push(text.Length == 0 ? doc.Remove(index) : doc.Replace(index, label with { Text = text, Size = Renderer.Measure(text, label.Style) }));
        else if (text.Length > 0)
        {
            var style = Canvas.MarkStyle;
            _history.Push(doc.Add(new TextLabel(style, at, text, Renderer.Measure(text, style))));
        }
    }

    // ------------------------------------------------------------------ keys

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_textBox != null) return;   // typing: its own keys
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var handled = true;
        switch (e.Key)
        {
            case Key.Z when ctrl && shift:
            case Key.Y when ctrl:
                _history?.Redo();
                break;
            case Key.Z when ctrl:
                _history?.Undo();
                break;
            case Key.C when ctrl:
                Copy();
                break;
            case Key.V when ctrl:
                Paste();
                break;
            case Key.S when ctrl:
                _ = SaveAsync(saveAs: shift);
                break;
            case Key.O when ctrl:
                _ = OpenAsync();
                break;
            case Key.D0 or Key.NumPad0 when ctrl:
                Canvas.ZoomToFit();
                break;
            case Key.D1 or Key.NumPad1 when ctrl:
                Canvas.ZoomActual();
                break;
            case Key.Delete or Key.Back:
                if (_history?.Current is { } doc && Canvas.Selected >= 0 && Canvas.Selected < doc.Items.Count)
                {
                    var index = Canvas.Selected;
                    Canvas.Select(-1);
                    _history.Push(doc.Remove(index));
                }
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down:
                Nudge(e.Key, shift ? 10 : 1);
                break;
            case Key.Escape:
                if (!Canvas.CancelDrag())
                {
                    if (Canvas.Selected >= 0) Canvas.Select(-1);
                    else if (Canvas.Tool == Tool.Crop && _history?.Current is { Crop: not null } cropped) _history.Push(cropped.WithCrop(null));
                }
                break;
            case Key.Space:
                Canvas.SpaceHeld = true;
                break;
            default:
                handled = !ctrl && !e.KeyModifiers.HasFlag(KeyModifiers.Alt) && Tools.FirstOrDefault(t => t.Key == e.Key) is { } tool
                    && SetToolFromKey(tool.Tool);
                break;
        }
        if (handled) e.Handled = true;
    }

    bool SetToolFromKey(Tool tool)
    {
        SetTool(tool);
        return true;
    }

    void Nudge(Key key, double by)
    {
        if (_history?.Current is not { } doc || Canvas.Selected < 0 || Canvas.Selected >= doc.Items.Count) return;
        var (dx, dy) = key switch { Key.Left => (-by, 0.0), Key.Right => (by, 0.0), Key.Up => (0.0, -by), _ => (0.0, by) };
        _history.Push(doc.Replace(Canvas.Selected, doc.Items[Canvas.Selected].Offset(dx, dy)));
    }

    // ------------------------------------------------------------------ copy, save, open

    byte[]? Export() => _history?.Current is { } doc && _shown != null ? ImageIo.Export(doc, _shown) : null;

    void Copy()
    {
        CommitText();
        if (Export() is not { } png) return;
        try
        {
            ImageIo.Copy(png);
            _copied = _history?.Current;
            Flash("Copied: paste it wherever you want it.");
        }
        catch (ExternalException ex)
        {
            Log.Warn($"Copy failed: {ex.Message}");
            Flash("Couldn't copy: another app is holding the clipboard. Try again.");
        }
    }

    /// <summary>Ctrl+V: an image on the clipboard opens here if this window is empty, otherwise in a new window.</summary>
    void Paste()
    {
        if (ImageIo.Paste() is not { } pixels)
        {
            Flash("There's no image on the clipboard.");
            return;
        }
        if (_pixels == null)
            SetImage(pixels, null);
        else
        {
            var window = new SnipWindow(_settings);
            window.SetImage(pixels, null);
            window.Show();
        }
    }

    async Task<bool> SaveAsync(bool saveAs)
    {
        CommitText();
        if (_history == null || Export() is not { } png) return false;
        var path = FilePath;
        if (saveAs || path == null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save the picture",
                SuggestedFileName = path != null ? Path.GetFileNameWithoutExtension(path) + "-edited.png" : $"snip-{DateTime.Now:yyyyMMdd-HHmmss}.png",
                SuggestedStartLocation = path != null ? await StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(path)!) : null,
                DefaultExtension = "png",
                FileTypeChoices = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }],
            });
            path = file?.TryGetLocalPath();
            if (path == null) return false;
        }
        try
        {
            await File.WriteAllBytesAsync(path, png);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Saving {path} failed: {ex.Message}");
            Flash($"Couldn't save: {ex.Message}");
            return false;
        }
        FilePath = path;
        _history.MarkSaved();
        _copied = _history.Current;
        Log.Info($"Saved {path}");
        ShowState();
        Flash($"Saved {Path.GetFileName(path)}");
        return true;
    }

    /// <summary>The file picker; the picture opens here if this window is empty, otherwise in a new one.</summary>
    public async Task OpenAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a picture",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Pictures") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif"] }],
        });
        if (files is not [var file] || file.TryGetLocalPath() is not { } path) return;
        if (_pixels == null)
            Load(path);
        else
        {
            var window = new SnipWindow(_settings);
            if (window.Load(path)) window.Show();
        }
    }

    void Reveal()
    {
        if (FilePath == null || !File.Exists(FilePath)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{FilePath}\"") { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn($"Couldn't show {FilePath}: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ closing

    async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        CommitText();
        var unsent = HasUnsentMarks;
        if (_allowClose || !unsent)
        {
            StorePlacement();
            return;
        }
        e.Cancel = true;
        var answer = await new ConfirmDialog("Keep your marks?",
            "This picture has marks you haven't copied or saved.", "Save", "Don't save").ShowDialog<bool?>(this);
        if (answer == true && !await SaveAsync(saveAs: false)) return;
        if (answer == null) return;
        _allowClose = true;
        Close();
    }

    // ------------------------------------------------------------------ the window

    /// <summary>Restores and brings the window forward (the tray's request arrives while another app has focus).</summary>
    public void BringUp()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = _settings.Maximized ? WindowState.Maximized : WindowState.Normal;
        Topmost = true;
        Topmost = false;
        Activate();
        if (TryGetPlatformHandle()?.Handle is { } handle && handle != IntPtr.Zero)
            SetForegroundWindow(handle);
    }

    void NextTheme()
    {
        _settings.Theme = _settings.Theme switch { "System" => "Dark", "Dark" => "Light", _ => "System" };
        App.SetTheme(_settings.Theme);
        ShowThemeLabel();
        _settings.Save();
        Canvas.InvalidateVisual();
    }

    void ShowThemeLabel()
    {
        ThemeLabel.Text = _settings.Theme switch { "Dark" => "Dark", "Light" => "Light", _ => "Windows" };
        ToolTip.SetTip(ThemeButton, $"Theme: {(_settings.Theme is "Dark" or "Light" ? _settings.Theme.ToLowerInvariant() : "follows Windows")}. Click to change.");
    }

    void RestorePlacement()
    {
        if (_settings.WindowWidth is > 400 and var w && _settings.WindowHeight is > 300 and var h)
        {
            Width = w;
            Height = h;
        }
        if (_settings is { Left: { } left, Top: { } top })
        {
            var point = new PixelPoint(left, top);
            if (Screens.All.Any(s => s.WorkingArea.Contains(point + new PixelPoint(40, 10))))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = point;
            }
        }
        if (_settings.Maximized)
            WindowState = WindowState.Maximized;
    }

    void StorePlacement()
    {
        _settings.Maximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            _settings.Left = Position.X;
            _settings.Top = Position.Y;
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
        }
        _settings.Save();
    }

    void HideDrawnTitle()
    {
        Visual root = this;
        while (root.GetVisualParent() is { } parent) root = parent;
        foreach (var control in root.GetVisualDescendants().OfType<Control>())
            if (control.Name is "PART_TitleTextPanel" or "PART_FullScreenButton" or "PART_PopoverFullScreenButton")
                control.IsVisible = false;
    }

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr window);
}
