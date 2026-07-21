<p align="center">
  <img src="src/HDRScreenMirror/Assets/logo-hdrscreenmirror.png" width="160" alt="HDRScreenMirror Logo">
</p>

<h1 align="center">HDRScreenMirror</h1>

<p align="center">An HDR screen mirroring tool for Windows</p>

<p align="center"><a href="README.md">简体中文</a> ｜ <strong>English</strong></p>

## Why HDRScreenMirror exists

As is widely known, Windows cannot normally enable display duplication after HDR is turned on and can only use the displays in extended mode. This is inconvenient, for example, when you want to compare the HDR image quality of two monitors connected to one computer.

HDRScreenMirror captures the desktop from one display in real time and copies it to a target display through Direct3D 11 using an FP16 scRGB HDR output path.

In short, it provides an approximation of display duplication while Windows HDR remains enabled.

## Features

1. Real time Windows HDR desktop capture and mirroring.
2. FP16 scRGB HDR output designed to preserve HDR luminance and color information.
3. A single output display or every other display connected to the same GPU.
4. Automatic aspect ratio preservation with black bars when required.
5. Optional cursor rendering on output displays.
6. An optional output status panel with live format, resolution, frame rate, and cursor information.
7. Optional mouse click through for interacting with the desktop behind the mirror window.
8. Global hotkeys for start, stop, emergency stop, and recalling the control panel.
9. Optional minimize to system tray behavior while mirroring continues in the background.

## Requirements

1. A 64 bit edition of Windows 10 or Windows 11.
2. [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
3. A GPU and driver that support Direct3D 11 and Desktop Duplication.
4. The capture and output displays must be connected to the same GPU.
5. HDR must be enabled in Windows for the output display.

## Installation

1. Download and extract the `win-x64` archive from Releases.
2. Make sure .NET 8 Desktop Runtime is installed.
3. Run `HDRScreenMirror.exe`.

No installer is required. Delete the extracted directory to remove the application.

## How to use it

### First run

1. Open Windows Settings and set the displays to “Extend these displays”.
2. Enable HDR in Windows for the target display.
3. Start `HDRScreenMirror.exe`.
4. Select the source screen under “Capture display”.
5. Select the target screen under “Output display”.
6. Click “Start HDR mirror”.
7. Click “Stop” or use a global hotkey when you want to end the mirror session.

### Mirroring to three or more displays

Enable “All displays on this GPU” to ignore the single output selection and present the captured image to every other display connected to the capture GPU.

For example, if three displays are connected to one GPU, HDRScreenMirror can capture the first display and present it to the second and third displays at the same time.

## Main options

| Option | Default | Purpose |
| --- | --- | --- |
| SDR white level | 203 nits | Controls SDR fallback and cursor brightness in the HDR output without changing the FP16 HDR image |
| VSync | Enabled | Reduces visible tearing on the output |
| Output status panel | Enabled | Shows resolution, format, frame rate, and cursor state in the top left corner of the output |
| Render cursor on output | Enabled | Composites the capture display cursor into the mirror image |
| Click through mirror | Disabled | Lets mouse clicks reach the desktop behind the mirror window |
| Enable global hotkeys | Enabled | Registers the start, stop, emergency stop, and recall shortcuts |
| Minimize to tray on close | Disabled | Hides the control panel while keeping an active mirror session running |

## Global hotkeys

| Hotkey | Action |
| --- | --- |
| `Ctrl + Alt + Shift + M` | Start or stop mirroring |
| `Ctrl + Alt + Shift + Q` | Emergency stop |
| `Ctrl + Alt + Shift + H` | Recall the control panel to the capture display |

Disable “Enable global hotkeys” if any shortcut conflicts with another application.

## System tray

When “Minimize to tray on close” is enabled, closing the main window only hides the control panel. An active mirror session continues running.

Double click the tray icon to show the control panel again. The tray menu can also show the control panel, start or stop mirroring, or exit the application completely.

## Troubleshooting

### Mirroring does not start

Confirm that the capture and output displays are different and connected to the same GPU. Also verify that HDR is enabled in Windows for the target display.

### Brightness or color looks incorrect

Check the Windows HDR state of the output display first. If the capture driver only supplies SDR BGRA8 frames, HDRScreenMirror must use the SDR fallback path and cannot restore HDR highlights that Windows has already tone mapped away.

### A video is not captured

Some DRM protected content and some Windows MPO hardware overlays may not enter Desktop Duplication reliably. This is a limitation of the Windows capture path.

### The control panel is missing

Press `Ctrl + Alt + Shift + H`. If tray mode is enabled, you can also double click the tray icon.

## Known limitations

1. Cross GPU mirroring is not currently supported.
2. The same display cannot be both the capture and output display.
3. Some MPO surfaces, protected videos, and exclusive fullscreen content may not be available to Desktop Duplication.
4. When the driver only returns an SDR image, original HDR highlights cannot be reconstructed.
5. Rare legacy monochrome or XOR cursors may be displayed using approximate colors.
