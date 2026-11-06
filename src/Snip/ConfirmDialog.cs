using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Snip;

/// <summary>A small question with two answers and Cancel: closes with true (the first), false (the second) or null.</summary>
sealed class ConfirmDialog : Window
{
    public ConfirmDialog() : this("", "", "OK", "No") { }

    public ConfirmDialog(string title, string message, string yes, string no)
    {
        Title = title;
        Width = 400;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Palette.Panel;

        Button Make(string text, bool? result, bool accent = false)
        {
            var button = new Button { Content = text, MinWidth = 86, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (accent) button.Classes.Add("accent");
            button.Click += (_, _) => Close(result);
            return button;
        }

        Content = new StackPanel
        {
            Margin = new Thickness(20, 18),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Palette.TextPrimary },
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Palette.TextSecondary },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { Make(yes, true, accent: true), Make(no, false), Make("Cancel", null) },
                },
            },
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(null); };
    }
}
