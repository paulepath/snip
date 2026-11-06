# How Snip works

## The editor

| Tool | Key | |
|---|---|---|
| Select | V | Click a mark to pick it; drag to move, arrows nudge (Shift: 10 px), Delete removes. Double-click text to edit. |
| Crop | C | Drag the part to keep; outside it is dimmed. Esc removes the crop. |
| Arrow | A | Drag tail to point. Shift snaps to 45°. |
| Box, Ellipse | R, E | Shift makes a square or circle. |
| Pen | P | Freehand. |
| Text | T | Click, type, Enter (Shift+Enter for a new line), Esc to cancel. |
| Highlight | H | A translucent box. The highlighter keeps a colour of its own (yellow unless picked). |
| Pixelate | X | Hides what's under it in blocks. It works on the screenshot itself, not on marks drawn over it. |
| Step | S | Numbered circles 1, 2, 3…; deleting one renumbers the rest. |

- **Colour and size** (along the top) apply to new marks and to the selected one.
- **Copy** (Ctrl+C) gives the picture with every mark. If marks reach past the picture's edge, the copy grows to
  include them, on white; the editor shows that white page too. A crop gives exactly the part dragged, which can reach
  past the picture to take in a mark. **Save** (Ctrl+S) and **Save as** (Ctrl+Shift+S) write the same as PNG.
- **Closing** asks only if there are marks not yet copied or saved.
- **Zoom** fits the window to start (never above 100%). Ctrl+wheel zooms at the pointer, Ctrl+0 fits, Ctrl+1 is
  100%; the wheel scrolls, and the middle button or Space+drag pans.
- **Other ways in:** Ctrl+O opens a picture, Ctrl+V pastes one (into a new window if this one has a picture). Undo and
  Redo are Ctrl+Z and Ctrl+Y.
- **Settings** (theme, colours, size, shots folder, the Pi Dash switch, the window's place) are kept in
  `%LOCALAPPDATA%\Snip\snip.json`.

## Capture (Windows)

1. **Print Screen** is a system-wide hotkey (`Capture/Hotkey.cs`, `RegisterHotKey`). The installer turns off Windows'
   own use of the key.
2. **`ScreenGrab`** copies the whole virtual desktop in physical pixels (Snip is per-monitor DPI aware), and lists the
   monitors and the visible top-level windows in z-order with their DWM frame bounds. It leaves out Snip's own,
   minimised, cloaked (other virtual desktops) and desktop windows.
3. **`CaptureOverlay`** puts a borderless, topmost window on each monitor, showing it frozen and dimmed:
   - hovering lights up the window under the pointer, and a click takes it;
   - a drag marks out a region;
   - Enter takes the monitor;
   - Esc or a right click gives up.

   The arithmetic (window or monitor under a point, regions, cutting) is `Snip.Core/CaptureMath.cs`.
4. **`CaptureService`** saves the part chosen to the shots folder (`Pictures\Snip\snip-yyyyMMdd-HHmmss.png`), puts it
   on the clipboard and opens it in the editor.

## The model

`Snip.Core` has no UI and is tested in `tests/Snip.Tests`:
- **`SnipDocument`:** the image's size, the crop and the marks, bottom first. It is immutable, as is every mark
  (`Annotations.cs`: `ArrowShape`, `RectShape`, `EllipseShape`, `PenStroke`, `TextLabel`, `HighlightBox`,
  `PixelateBox`, `StepBadge`, each with a `MarkStyle`), so `UndoStack` just keeps old versions. Marks know their
  bounds, how to move and whether a point is on them.
- **`Pixels.Pixelate`** works on a `PixelBuffer` (BGRA).
- **In the app:** `SnipCanvas` draws and handles the pointer. `Renderer` draws a document onto any Avalonia drawing
  context, so the canvas and the copied or saved picture are the same code. `ImageIo` reads files and the clipboard
  with System.Drawing (one known pixel layout) and writes PNG.

## Later
- A delay, the last region again, a magnifier while choosing, and capturing a scrolling window.
- Re-editing: the marks are plain records, ready to be saved beside the PNG (a `.snip` file), so a shot can reopen
  with its marks still editable.
- More tools: blur, callouts, lines, resize handles on the selected mark.
- Releases and updates through GitHub Releases (Velopack, as Panehouse), and a signed installer.
