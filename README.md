<div align="center">
  <img src="src/LivelyScreen.WinUI/Assets/LogoMark-cropped.png" alt="LivelyScreen Logo" width="128" />

  # LivelyScreen
  
  Native Windows 11 video and photo screensaver daemon built using **WinUI 3 (Windows App SDK)** and **Fluent 2 Design**.
</div>

It features native Windows 11 Mica backdrop material, Windows Community Toolkit SettingsCards, hardware-accelerated video/photo playback, and multi-monitor management.

---

## Native Windows 11 Features

- **Fluent 2 / WinUI 3 Native UI**:
  - Native Mica backdrop with window transparency.
  - Native Windows 11 `TitleBar` and `NavigationView`.
  - Windows Community Toolkit `SettingsCard` controls.
  - Native Windows 11 `FolderPicker`.
- **Multi-Monitor Display**:
  - Dynamic discovery via `DisplayArea.FindAll()`.
  - Independent, Clone mirror, or Spanned canvas modes.
- **Hardware-Accelerated Playback**:
  - `MediaPlayerElement` direct GPU video decoding (MP4, MKV, WebM, MOV).
  - WIC `BitmapImage` photo slideshow with smooth transitions.
- **Smart Idle & Gaming Guard**:
  - `GetLastInputInfo` idle timer.
  - `SHQueryUserNotificationState` detects fullscreen DirectX games and media playback to pause screensaver.
  - Micro-vibration distance filtering prevents desk bumps from closing the screensaver.
- **System Tray Daemon & Hotkey**:
  - Win32 `Shell_NotifyIcon` taskbar notification.
  - Closing settings window minimizes to tray.
  - Global hotkey `Win + Ctrl + S` triggers screensaver instantly.

---

## How to Run

### From GitHub Releases (Pre-built)

#### Option 1: Installer (Recommended)
1. Go to [Releases](https://github.com/Unknownplanet40/LivelyScreen/releases) and download **`LivelyScreen-Setup-win-x64.exe`**.
2. Run the installer and follow the setup wizard.
3. It creates Start Menu and Desktop shortcuts and can launch on Windows startup.

#### Option 2: Portable ZIP
1. Download **`LivelyScreen-win-x64.zip`**.
2. Right-click the `.zip` and select **Extract All...**.
3. Open the folder and run **`LivelyScreen.exe`**.

#### Usage
- LivelyScreen runs in your Windows System Tray (notification area).
- Press **Win + Ctrl + S** at any time to launch the screensaver instantly.
- Right-click the system tray icon to configure settings, themes, widgets, or exit.

---

## Building from Source

### Requirements
- Windows 10 version 1809 (build 17763) or Windows 11
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Windows App SDK

### Debug
```powershell
dotnet run --project src/LivelyScreen.WinUI/LivelyScreen.WinUI.csproj
```

### Release Publish (Self-Contained)
```powershell
dotnet publish src/LivelyScreen.WinUI/LivelyScreen.WinUI.csproj -c Release -p:PublishProfile=win-x64
```
Output binary will be located in `src/LivelyScreen.WinUI/bin/Release/net9.0-windows10.0.26100.0/win-x64/publish/LivelyScreen.exe`.
