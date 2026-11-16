# Pauth40 - Uno Platform port

This folder contains the cross-platform version of Pauth40, built with **Uno
Platform** on **.NET 10**. It shares the design, the encrypted vault, the backup
format and the JSON translations with the original WPF application, but runs on
Windows, Linux, macOS, Android, iOS, tvOS and WebAssembly.

```
linux/
  Pauth40.sln                  solution (Visual Studio / Rider / VS Code)
  Directory.Build.props        common C# settings
  Directory.Packages.props     centralized NuGet versions (Central Package Management)
  Pauth40/
    Pauth40.csproj             one project, six target frameworks
    App.xaml(.cs)              DI bootstrap and main window creation
    MainPage.xaml(.cs)         single-window shell: security, main, settings, update, capture
    Views/                     SecurityView, MainView, SettingsView, UpdateView, CaptureView
    ViewModels/MainViewModel   accounts, TOTP timer, import/copy/remove commands
    Services/                  clipboard, capture, file pickers, updates, release notes
    Controls/                  CircularCountdown (twin TOTP arcs)
    Converters/                XAML value converters
    Themes/PauthTheme.xaml     palette, styles, icon styles and button template
    Localization/              JSON localization, Loc attached properties
    Platforms/                 Android, iOS, tvOS and desktop entry points
    Assets/                    app mark, duck mascot and splash screen
    langs/                     pt-PT.json and en-US.json
  Pauth40.desktop              Linux desktop entry
  install-linux.sh             per-user installer (launcher, icon, desktop entry)
  uninstall-linux.sh           removes the per-user installation
  publish.sh                   publish helper for Linux/macOS + WebAssembly
  publish.ps1                  publish helper for Windows + Linux + WebAssembly
```

## Target frameworks

| Target | Framework | Notes |
| --- | --- | --- |
| Windows | `net10.0-windows10.0.26100` | WinAppSDK, native window frame, GDI screen capture |
| Linux | `net10.0-desktop` | Skia + X11 rendering, uses `grim`/`gnome-screenshot`/`scrot`/`import` when available |
| macOS | `net10.0-desktop` | Skia rendering, uses `screencapture` |
| Android | `net10.0-android` | uses the system image picker for QR import |
| iOS | `net10.0-ios` | uses the photo library picker |
| tvOS | `net10.0-tvos` | uses the photo library picker |
| WebAssembly | `net10.0-browserwasm` | static web bundle, uses the browser file picker |

The platform code is selected with the `PAUTH40_WINDOWS`, `PAUTH40_DESKTOP`,
`PAUTH40_MOBILE` and `PAUTH40_WASM` constants defined in `Pauth40.csproj`.

## Requirements

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- Workloads for the targets you want to build:

```bash
dotnet workload install android ios tvos wasm-tools
```

- **Android**: Android SDK with platform 36 and build-tools 36. Set
  `AndroidSdkDirectory` when it is not in the default location.
- **iOS / tvOS**: a Mac with Xcode for the final application bundle and signing.
- **Linux desktop**: a graphical session with X11 (or XWayland). The publish is
  self-contained and does not require the .NET runtime on the target machine.

## Build

```bash
# Windows
dotnet build Pauth40/Pauth40.csproj -f net10.0-windows10.0.26100 -c Release

# Linux / macOS desktop
dotnet build Pauth40/Pauth40.csproj -f net10.0-desktop -c Release

# WebAssembly
dotnet build Pauth40/Pauth40.csproj -f net10.0-browserwasm -c Release

# Android
dotnet build Pauth40/Pauth40.csproj -f net10.0-android -c Release

# iOS / tvOS
dotnet build Pauth40/Pauth40.csproj -f net10.0-ios -c Release
dotnet build Pauth40/Pauth40.csproj -f net10.0-tvos -c Release
```

Build outputs live in `Pauth40/bin/<Configuration>/<framework>`.

The iOS and tvOS builds default to the simulator; pass the runtime identifier
for a device build, for example `-p:RuntimeIdentifier=ios-arm64`.

## Run during development

```bash
# Desktop (Windows / Linux / macOS)
dotnet run --project Pauth40/Pauth40.csproj -f net10.0-desktop

# Windows
dotnet run --project Pauth40/Pauth40.csproj -f net10.0-windows10.0.26100

# WebAssembly (Uno serves the app and opens the browser)
dotnet run --project Pauth40/Pauth40.csproj -f net10.0-browserwasm
```

Set `PAUTH40_DATA_DIR` to point the application at an isolated data folder while
testing. `PAUTH40_SKIP_UPDATE_CHECK=1` disables the GitHub version check.

## Publish and package

### Windows

```powershell
.\publish.ps1                      # current platform default: win-x64
.\publish.ps1 -Runtime win-arm64 -SkipLinux
```

It writes a self-contained single-file `dist\Pauth40-<version>-win-x64.exe`,
the Linux tarball and the WebAssembly zip.

### Linux / macOS

```bash
cd linux
./publish.sh                       # native runtime identifier
./publish.sh linux-x64             # explicit runtime identifier
```

It writes `dist/Pauth40-<version>-<rid>.tar.gz`, copies `install-linux.sh`,
`uninstall-linux.sh` and `Pauth40.desktop`, and (when `APPIMAGETOOL` points to
an `appimagetool` binary) builds `dist/Pauth40-<version>-x86_64.AppImage`.
On `linux-x64` it also publishes the WebAssembly bundle.

### Linux install

```bash
tar -xzf Pauth40-2.2.0-linux-x64.tar.gz -C Pauth40
./Pauth40/install-linux.sh
```

This installs the application in `~/.local/share/pauth40`, the launcher in
`~/.local/bin/pauth40`, and the desktop entry and icon in the XDG folders.
`uninstall-linux.sh` removes the installation but keeps the vault.

### AppImage

```bash
chmod +x Pauth40-2.2.0-x86_64.AppImage
./Pauth40-2.2.0-x86_64.AppImage
```

The AppImage is the recommended Linux package because the in-app updater
replaces `.AppImage` files atomically. If FUSE is not available, run it with
`--appimage-extract-and-run`.

## Data locations

| Platform | Vault and settings |
| --- | --- |
| Windows | `%LOCALAPPDATA%\Pauth40\` |
| Linux | `~/.local/share/Pauth40/` |
| macOS | `~/.local/share/Pauth40/` (or `$PAUTH40_DATA_DIR`) |
| Android / iOS / tvOS | application data folder of the app package |
| WebAssembly | browser storage managed by Uno |

The folder contains `accounts.dat`, `security.json`, `language.json` and the
`langs` overrides. The formats are the same as the WPF build, so a vault or a
`.pauth40` backup can be moved between the two applications.

## Security differences from the WPF build

- The vault key is wrapped by a key derived from the master password with
  **PBKDF2-SHA256 (310,000 iterations)** on every platform.
- The local recovery copy uses **Windows DPAPI** on Windows, exactly like the
  WPF build.
- On Linux, macOS, Android, iOS, tvOS and WebAssembly the recovery copy is
  wrapped by a random **AES-GCM device key**. On Unix-like systems the key file
  is created with `0600` permissions. The copy only works on the same device and
  user; moving the vault to another machine requires the e-mail recovery code.
- TOTP secrets are never written to logs or sent over the network. The only
  automatic request is the read-only GitHub release check.

## Updates

- **Windows**: the app downloads the new `.exe`, replaces itself with a helper
  script and relaunches.
- **Linux AppImage**: the app downloads the new `.AppImage`, replaces the file
  and relaunches.
- **macOS, Android, iOS, tvOS, WebAssembly**: no in-app updater; new releases are
  installed manually. The mandatory update screen is only shown on Windows and
  Linux.

## Versioning and releases

Both `Pauth40.csproj` (WPF) and `linux/Pauth40/Pauth40.csproj` (Uno) carry the
same `<Version>`, `<ApplicationDisplayVersion>` and `<ApplicationVersion>`.

1. Bump the version in both project files.
2. Update `docs/releases/<version>.md` (bilingual, with `[pt-PT]` and `[en-US]`
   markers).
3. Run `publish.ps1` / `publish.sh` to produce the artifacts.
4. Commit the changes and create the release `v<version>` with the artifacts from
   `../dist` and the notes from `docs/releases/<version>.md`.
5. Alternatively, start the `Release` workflow manually from **Actions > Release >
   Run workflow** with the tag name. It builds Windows, Linux, WebAssembly and
   macOS artifacts and publishes the release with the notes from
   `docs/releases/<version>.md`.

## Troubleshooting

- **`XA5300: The Android SDK directory could not be found`** - pass
  `-p:AndroidSdkDirectory=<path>` or install the Android SDK.
- **`Microsoft.NETCore.App.Runtime.Mono.linux-x64` not found** - the desktop
  publish uses the .NET runtime, not Mono. Add `-p:UseMonoRuntime=false` (the
  publish scripts already do).
- **AppImage does not start** - install FUSE, or run with
  `--appimage-extract-and-run`.
- **macOS blocks the app** - use **Open** from the context menu the first time;
  the release builds are not notarized.
- **No window on Linux over SSH** - a graphical session is required; the app
  uses X11/XWayland through Skia.
