# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.10] - 2026-11-06

### Added
- **CyberOps SOC Live Disk Recon & Binary Hex Dump**: Added `tree /f` directory hierarchy crawler and 16-byte binary inspection panel with ASCII gutters, rotating every 12 seconds alongside live event logs.
- **Artwork Pre-Warming & Decode Pipeline**: Implemented background sequential I/O OS-cache warming and GPU Direct3D texture pre-decoding for smooth 60/120 FPS photo transitions on mechanical HDDs.
- **Multi-Monitor Preload Broadcast**: Master window now synchronizes preloaded media across cloned monitors simultaneously.

### Fixed
- **Artwork Gallery Timer Freeze**: Fixed photo advance timer stalling due to async stream decode lockups in WinRT pipeline.
- **Corrupt Image Resilience**: Hooked `ImageFailed` to automatically advance past corrupted or unreadable images instead of locking the screensaver.
- **Dashboard Sampler Lifecycle**: Added explicit `Attach()` and `Detach()` handlers to avoid event handler accumulation on screensaver start/stop cycles.

## [1.0.9] - 2026-11-06

### Added
- **3D Stash Knowledge Map Screensaver**: Visualizes SQLite knowledge bases (e.g. `brain.db` from KnowledgeBase-Quotes) as an interactive celestial constellation starfield with 3D depth, harmonic drift, and synaptic node filaments.
- **Knowledge Map Customizations**: Support for 3D Celestial Constellation vs. 2D Plane spin modes, adjustable orbit speed, camera pitch tilt, and clean distraction-free fullscreen (widget suppression).

### Fixed
- **Rich Text Release Notes**: Formatted markdown rendering in the built-in update notification viewer instead of displaying raw markdown markup.

## [1.0.8] - 2026-11-03

### Added
- **Authentic Fake OS Update & Crash Screens**: Pixel-accurate screens for Windows 10/11, macOS, Ubuntu, and ChromeOS with measured 6-dot PCHIP spinner easing, dynamic crash dump percentage climbing, and reboot sequence transitions.
- **Synchronized Multi-Monitor Support**: Added `MULTI_MONITOR_MODE` supporting `primary_only` (black secondary screens) and `mirror` (synchronized clock, seed, percentage, and independent per-monitor resolution scaling).
- **Real-Time SOC / System Monitor Terminal**: Replaced old simulated CyberOps terminal with a 100% real hardware telemetry dashboard via Win2D, DXGI, PDH, and NTDLL with live process inspection, connection tracking, and snapshot-diff event logging.
- **Data Privacy Mode**: Added `MASK_SENSITIVE` setting to mask hostnames, usernames, MAC addresses, and IP octets in the telemetry dashboard.
- **Automated Dashboard Test Suite**: Added xUnit test project covering formatters, ring buffers, and diff event emission.

## [1.0.7] - 2026-11-01

### Added
- **Multi-Source Wisdom & Quotes Engine**: Load quotes and cultural sayings from SQLite knowledge bases (`.db`), local JSON files (`.json`), or remote HTTPS endpoints.
- **Flexible JSON Schema Parser**: Ingest quotes formatted as root arrays or wrapped objects (`quotes`, `entries`, `data`, `items`) with property aliases (`quote`, `text`, `content`, `author`, `meaning`, `category`).
- **Offline Fallback Caching**: Remote HTTPS quotes are automatically cached locally in AppData to guarantee instant availability when offline.
- **Curated Fallback Library**: 26 timeless literary quotes, Stoic maxims, and cultural proverbs included out of the box with explanations and cultural context.
- **Second-Stage Auto-Blackout Sleep**: Added toggle and mode dropdown to optionally trigger full system standby (PC sleep) or monitor power-off after prolonged screensaver idle.
- **Instant Resume Dismissal**: Screen immediately unlocks and closes the screensaver upon system wake from standby.
- **Per-Folder Playback Ordering**: Added sequential, shuffle, alphabetical, and date-modified playback order options in the folder settings dialog.

### Fixed
- Prevented dynamic wallpaper playback leaks and isolated timers during multi-monitor transition cycles.

## [1.0.6] - 2026-10-24

### Fixed
- Released file handle lock on downloaded installer before launching setup process to prevent update installation failures.
- Automated application relaunch following update completion.

## [1.0.5] - 2026-10-24

### Added
- Built-in GitHub update notification system with background checking and in-app release notes viewer.

### Fixed
- Resolved taskbar flashing and foreground z-order issues across multi-monitor fullscreen screensaver sessions.

## [1.0.4] - 2026-10-20

### Added
- Per-monitor widget configuration with independent layouts across displays.
- Top-right acrylic battery badge and low-power charging status indicator.

### Changed
- Made screensaver manual activation hotkey configurable with `Ctrl+Alt+S` default.

## [1.0.3] - 2026-10-20

### Fixed
- Ensured PRI resources, XBF markup, and visual asset manifests are properly bundled in unpackaged deployment outputs.

## [1.0.2] - 2026-10-20

### Added
- Inno Setup installer script for standard Windows desktop installation and uninstallation.
- GitHub Actions CI/CD release workflow for automated x64 packaging.

## [1.0.1] - 2026-10-19

### Changed
- Standardized release artifacts to deliver standalone zip bundles.

## [1.0.0] - 2026-10-19

### Added
- Initial release of LivelyScreen for Windows 10/11 using WinUI 3 and Windows App SDK.
- Support for video wallpapers (MP4, MKV, WebM) and high-resolution photo slideshows.
- Fluid transitions including Crossfade, Wipe, Slide, Zoom, and Ken Burns motion effects.
- Dynamic time-locked solar themes with sunrise/sunset auto-switching.
- Customizable desktop widgets: Clock, Weather, Media/Now Playing, System Performance, Calendar, and KnowledgeBase Wisdom cards.
- Multi-monitor modes: Span Across All, Duplicate/Clone, and Independent Per-Display playlist assignments.

[Unreleased]: https://github.com/Unknownplanet40/LivelyScreen/compare/v1.0.7...HEAD
[1.0.7]: https://github.com/Unknownplanet40/LivelyScreen/compare/v1.0.6...v1.0.7
[1.0.6]: https://github.com/Unknownplanet40/LivelyScreen/compare/v1.0.5...v1.0.6
[1.0.5]: https://github.com/Unknownplanet40/LivelyScreen/compare/v1.0.4...v1.0.5
[1.0.4]: https://github.com/Unknownplanet40/LivelyScreen/compare/v1.0.3...v1.0.4
[1.0.3]: https://github.com/Unknownplanet40/LivelyScreen/compare/v1.0.2...v1.0.3
[1.0.2]: https://github.com/Unknownplanet40/LivelyScreen/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/Unknownplanet40/LivelyScreen/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/Unknownplanet40/LivelyScreen/releases/tag/v1.0.0
