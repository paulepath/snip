# Snip and the Pi Dash

The Pi Dash (D:\code\pi-dash) is a wall display whose screen the PC's mouse and keyboard can move to (Win+Esc). While
they're there, Print Screen gives a shot of the dash instead of the PC. Nothing needs setting up: Snip finds the Pi Dash
desktop app's settings on this PC. The tray's "Use the Pi Dash" switches it off.

## How

1. **The key reaches Snip.** The Pi Dash tray app sends every key to the dash while the mouse is there, except a few
   that stay on the PC (pi-dash `KeyMap.StaysOnPc`). Print Screen is one of those, so Snip's hotkey gets it and the Pi
   never does.
2. **Snip knows the mouse is on the dash.** The Pi Dash tray app holds the named event `Local\PiDash.MouseOnDash`
   set while the mouse is there (pi-dash `RemoteController`). On Print Screen, Snip checks it
   (`DashLink.MouseOnDash`). When it's set, Snip shoots the dash; otherwise it captures Windows.
3. **Snip asks the dash.** `DashLink` connects to the dash's MQTT broker, using the `mqtt` section of
   `%LOCALAPPDATA%\PiDash\desktop.json` (host, port, user `desktop`, password). Its client id is `snip-<machine>`, so it
   never disturbs the Pi Dash app's own connection. It publishes `pidash/desktop/screenshot` `{"id"}` and waits on
   `pidash/ui/screenshot/<id>`. The dash runs `grim` (pi-dash `server/screenshot.py`) and sends back the PNG: the whole
   1280×400 screen, with the pointer. An empty payload means the shot failed.
4. **Then, as any shot:** `DashScreenshot` saves it to `Pictures\Pi Dash\dash-yyyyMMdd-HHmmss.png`, puts it on the
   clipboard and opens it in the editor. If nothing comes within 5 s, or the dash isn't connected, the tray says so,
   at most once a minute.

The topics and the broker's ACL (`topic read pidash/ui/screenshot/#` for `desktop`) are in pi-dash's
docs/contract.md and pi/mosquitto/pidash.acl.

## Without one or the other

- **No Pi Dash on this PC:** the tray says "Pi Dash: not installed here", and Print Screen always captures Windows.
- **No Snip:** Print Screen with the mouse on the dash goes to Windows, which captures the PC's screen. The dash
  still never gets the key.
