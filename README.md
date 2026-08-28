# WTGWizard

<p align="center">
  <img src="assets/app.png" alt="WTGWizard" width="96" />
</p>

<p align="center">
  A modern <strong>Windows To Go</strong> deployment wizard built with <strong>WinUI</strong>.
  Guides you step by step through creating a portable Windows workspace on an external drive.
</p>

---

![WTGWizard](assets/screenshot-1.png)


[中文版本](docs/README.zh-CN.md)

## Features

- **5-step guided wizard** — The wizard will guide you step by step through the process of selecting an image, configuring disks, adjusting deployment settings, and enabling advanced settings, and then provide a summary page for you to confirm all deployment settings.
- **WIM / ESD images support** — WTGWizard supports both wim and esd format of Windows images.
- **Two installation type** — WTGWizard provides you both clean install and install to partition. The former will wipe and recreate disk layout on selected disk, while the latter allows you to install Windows into an existing partition.
- **Driver integration** — Integrate drivers you need into Windows.
- **Answer file handling** — Use a your own answer file to customize Windows installation.
- **Deployment dashboard** — A simple but powerful task page allows to track deployment progress, with a built-in disk performance monitor to show disk performance metrics.
- **Localization** — WTGWizard supports English and Simplified Chinese for now.

## Requirements

- System: Windows 10 version 1809 (Build 17763) or later, **x64**. Best experienced on **Windows 11**.
- Runtimes: 
  - For `-SCD` version: All required runtimes and frameworks are bundled; nothing extra needs to be installed.
  - For `-FDD` version: Both [.NET 10.0 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/thank-you/runtime-desktop-10.0.10-windows-x64-installer?cid=getdotnetcore) and [Windows App SDK 2.4.0 x64](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) is reqiured to be installed beforehand.
- **Elevated privilege**: The app requests elevation for disk operations, DISM operations, etc.
- An external hard drive or a USB drive is needed.

## Limitations

- WTGWizard **does not** support creating a Windows To Go drive with MBR (Master Boot Record) partition style though it is technically feasible. Only GPT partition style is supported.
- WTGWizard **dose not** support creating a Windows To Go drive compatible with Legacy boot manager though it is technically feasible. Only UEFI is supported.

## Download & Usage

1. Download the latest release ZIP from the [Releases](../../releases) page. Choose `WTGWizard-vX.Y.Z-x64-FDD.zip` or `WTGWizard-vX.Y.Z-x64-SCD.zip`.
2. Install the required runtimes **before** running the app. See [Requirements](#requirements)
3. Extract the archive and run `WTGWizard.Main.exe`. The program will request for **elevation**.
4. Follow the wizard to complete every setup page, confirm your configurations, then start the deployment. It will take some time to finish the setup affected by disk performance.

> **Warning**: Deployment erases the target disk or partition. Verify your selections carefully before starting.

## Build from Source

Requirements:

- [.NET 10 SDK x64]((https://dotnet.microsoft.com/download/dotnet/thank-you/sdk-10.0.400-windows-x64-installer)) (pinned to `10.0.400` via `global.json`)
- Windows ADK 10.0.26100 (bundled with Visual Studio 2022 or the standalone Windows ADK)

```powershell
dotnet build WTGWizard.slnx
```

Publish a release build:

```powershell
# Single mode per run (FDD or SCD); the CI/Release pipeline builds both via GitHub Actions matrix
./BuildArtifacts.ps1 -BuildType FDD -MainVer 1.0.0 -WorkerVer 1.0.0 -ZipTag v1.0.0
./BuildArtifacts.ps1 -BuildType SCD -MainVer 1.0.0 -WorkerVer 1.0.0 -ZipTag v1.0.0
```

Or use the PublishProfile directly (the single source of publish parameters is `Properties/PublishProfiles/*.pubxml`):

```powershell
dotnet publish src/WTGWizard.Main -p:PublishProfile=SCD-x64
dotnet publish src/WTGWizard.Main -p:PublishProfile=FDD-x64 -p:PublishDir=build\publish\FDD
```

## Architecture

WTGWizard is split into a **Main** process (WinUI 3 UI) and an out-of-process **Worker** child process communicating over a **NamedPipe IPC** protocol.

```
src/
├── WTGWizard.Main/                    # WinUI 3 app (UI, ViewModels, pages)
├── WTGWizard.Main.DeploymentCore/     # Deployment engine (7-step pipeline, orchestrator).
├── WTGWizard.Main.Language/           # Localization resources.
├── WTGWizard.Shared.Services/         # Disk I/O, WIM, logging.
├── WTGWizard.Shared.Common/           # NamedPipe IPC protocol
└── WTGWizard.Worker/                  # Out-of-process worker (handle deployment tasks).
```

**Deployment pipeline**: Create Disk Layout/Format Partition → Extract Image → Integrate Drivers → Import Answer File → Apply System Settings → Create Boot Files → Final Cleanup.

The orchestrator publishes task updates over an observable stream; the UI renders task cards and terminal output without coupling to the worker processes.

## Versioning & Releases

- Pushing a tag `v*` triggers the GitHub Actions release workflow.
- Tags containing `preview` (e.g. `v1.0.0-preview1`) are published as **Pre-releases**.
- The **Main** version comes from the tag; the **Worker** version is maintained independently.
- The product version automatically includes the full 40-character commit hash (e.g. `1.0.0+<hash>`).

## Localization

UI strings are stored in `.resx` files (`Lang.resx` for English, `Lang.zh-CN.resx` for Simplified Chinese) with dot-separated key names, exposed through a strongly-typed designer class.

## License

This project is licensed under the **GNU General Public License v3.0**. Please use under GPLv3 restrictions. See [LICENSE](LICENSE).

Third-party component licenses are listed in [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES).

## Trademark

WTGWizard is an **independent, open-source project** and is not affiliated with, endorsed by, or sponsored by Microsoft.
**_Windows_**, **_Microsoft_**, **_Windows To Go_** and the Windows logos are trademarks or registered trademarks of Microsoft Corporation.
The application icon contains a conceptual, recolored re-interpretation of the Windows flag motif and is provided for identification purposes only.

## Acknowledgements

WTGWizard used/referred these projects below. Thanks for their excellent works!

- [wimlib](https://wimlib.net) / [ManagedWimLib](https://github.com/MircoBabin/ManagedWimLib) — WIM related operations, core component of WTGWizard.
- [Vanara](https://github.com/dahall/Vanara) — Solid foundation of DiskIOService implementation.
- [Windows CommunityToolkit](https://github.com/CommunityToolkit) — MVVM and WinUI controls.
- [Serilog](https://serilog.net) — App logging service.
- [Starward](https://github.com/Scighost/Starward) — One solution with multi-project architecture and localization implementation. Starward is a very beautiful and easy-to-use HoYoverse game launcher with a set of enhancements.
- [TaskMonitor](https://github.com/linesoft2/TaskMonitor) — The calculation formula of disk performance monitor's average response time. TaskMonitor is a young but powerful system performance monitor allowing you to monitor system performance from taskbar.

## Disclaimer

USE **AT YOUR OWN RISKS**. WTGWizard can performs destructive disk operations and the authors are not responsible for any accidental data loss or damage.
