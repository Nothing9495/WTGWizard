# AGENTS.md

## Agent Constraints
**Whatever content you're trying to write into AGENTS.md, use English first.**
**Before running any command or starting any session, run `powershell -NoLogo -Command 'Get-Culture | Select DisplayName'` first to determine the language for the ToC and messages.**
**When trying to write any content to project files, use `CRLF` first on Windows and `LF` on Unix/Linux.**

## Project Overview

**WTGWizard** is a Windows To Go (WTG) deployment wizard — a WinUI 3 desktop application that guides users through creating Windows To Go workstations.

- **Stack**: C# / .NET 10 / WinUI 3 (Windows App SDK 2.3)
- **Platform**: x64 only, Windows 10 1809+ (min version 10.0.17763.0)
- **Architecture**: Main (WinUI host) + Worker (out-of-process console app) communicating via Named Pipes

---

## Project Structure

```
src/
├── WTGWizard.Main/            # WinUI 3 main app (WinExe)
│   ├── Pages/                 #   MainWindow, TaskPage, WizardHost, SettingsPage, Steps/
│   ├── ViewModels/            #   WizardViewModel + 5 sub-VMs
│   ├── UserControls/          #   TaskContentCard, TerminalBox, ImageInfoCard, File/FolderPicker
│   ├── Helpers/               #   TitleBarHelper, WindowHelper, WindowsBuildHelper
│   ├── Models/                #   WinBuildConstants.cs (build-number thresholds)
│   ├── Messages/              #   NavigateToPageMessage
│   └── Styles/                #   AbortButtonResources.xaml (referenced locally by TaskPage)
├── WTGWizard.Main.Language/   # Localization resources (.resx) + Localization.cs accessor
├── WTGWizard.Main.DeploymentCore/  # Deployment engine (models/steps/orchestrator/Worker bridge)
├── WTGWizard.Shared.Services/ # Core services: disk, WIM, logging, terminal buffer
├── WTGWizard.Shared.Common/   # Named Pipe IPC protocol
├── WTGWizard.Launcher/        # Native launcher (vcxproj/C++; not in slnx, invoked by the build script via msbuild)
└── WTGWizard.Worker/          # Worker child process (Exe)
```

### Dependency Graph

```
Main ──┬──> Shared.Services
       ├──> Shared.Common
       ├──> Main.Language
       └──> Main.DeploymentCore ──┬──> Shared.Services
                                  ├──> Shared.Common
                                  └──> Main.Language

Worker ──┬──> Shared.Services
         └──> Shared.Common
```

Worker is NOT a project reference — MSBuild targets copy Worker output to Main's output directory after build.

---

## Build & Run

### Build Path Selection (Agent constraints)

**Prefer the DevVM remote build.** Before building, check three conditions in order:

1. The `devvm-remote-build` skill is installed (available in the skills list)
2. The DevVM build environment is available (`ssh -o ConnectTimeout=8 devvm "dotnet --version"` succeeds)
3. The project manifest `.devvm-build.json` exists (at the project root)

| Result | Action |
|---|---|
| All three satisfied | **DevVM remote build** (below) |
| Any not satisfied | **Fall back to local build** (below), and state the reason for the fallback in your reply |

### Remote Build (preferred)

```powershell
# Any PowerShell on the Host (pwsh preferred; use powershell 5.1 when pwsh is absent — the script is 5.1-compatible)
powershell -NoProfile -File "$env:USERPROFILE\.config\opencode\skills\devvm-remote-build\scripts\Remote-Build.ps1" -ProjectPath "E:\Development\Local_Projects\WTGWizard.AOT"
```

- Manifest command: `BuildArtifacts.ps1 -BuildType FDD -OutputDir {artifactDir}`, timeout 600s
- Artifact retrieval: `E:\Development\BuildArtifacts\WTGWizard.AOT`
- The bash tool timeout must be set to ≥ 1800000 ms

### Local Build (fallback)

```powershell
# Build
dotnet build WTGWizard.slnx

# Publish (single source of publish parameters: Properties/PublishProfiles/*.pubxml; see Pitfall 22)
dotnet publish src/WTGWizard.Main -p:PublishProfile=SCD-x64
dotnet publish src/WTGWizard.Main -p:PublishProfile=FDD-x64 -p:PublishDir=build\publish\FDD
```

- **SDK**: `global.json` baseline `.NET 10.0.400` + `rollForward: latestMinor` (allows 10.0.x minor/patch upgrades; the actual resolution depends on the local installation, e.g. 10.0.401; a change of the upgrade policy = an explicit commit to global.json)
- **Target Framework**: `net10.0-windows10.0.26100.0`
- **Language Version**: `preview`
- **Platform**: x64 only
- **Package Type**: Unpackaged (not MSIX), uses `app.manifest`

---

## Module Responsibilities

### WTGWizard.Main — UI Layer

**Entry Point**: `App.xaml.cs` → `OnLaunched()` → DI setup → `MainWindow`

**Helpers/**: `TitleBarHelper` (custom title bar), `WindowHelper` (window sizing/centering), `WindowsBuildHelper` (BootEx build-number threshold check — `MeetsBootExThreshold` via `BuildMajor*`/`BuildRevisionThreshold` from `Models/WinBuildConstants.cs`)

**Models/**: `WinBuildConstants.cs` — the only constant class on the UI side (Windows build-number thresholds: `BuildMajor26100`/`BuildMajor26200`/`BuildRevisionThreshold`, used only by WindowsBuildHelper). Disk/deployment constants are not here (see DiskConstants/DeploymentConstants).

**Messages/**: `NavigateToPageMessage` — WeakReferenceMessenger cross-page navigation (sent to MainWindow → Frame switch)

**Debug-Build Warning**: `RootGrid_Loaded` ends with `#if DEBUG ShowDebugBuildWarning()` → ContentDialog (shown on every DEBUG launch; localization keys `App.Dialog.DebugBuild.*`)

**5-Step Wizard** (`Pages/Steps/`):

| Step | Page | Purpose |
|------|------|---------|
| 1 | `ImageConfigPage` | WIM/ESD file selection + index picker (cached info load + verify) |
| 2 | `DeployMethodPage` | Disk selection, clean/partition install |
| 3 | `DeployOptionsPage` | WTG settings (hide disks, drive letter, etc.) |
| 4 | `AdvancedOptionsPage` | Driver integration, answer files, boot options (BootEx gated by image build) |
| 5 | `ConfirmPage` | Summary + "Start Deployment" button |

**ViewModels**: `WizardViewModel` is the orchestrator, composing 5 sub-VMs (`ImageConfigVM`, `DeployMethodVM`, `DeployOptionsVM`, `AdvancedOptionsVM`, `ConfirmVM`). `TakeOrchestrator()` hands the orchestrator to TaskPage and nulls it (prevents duplicate deployment).

**TaskPage** (`Pages/TaskPage.xaml` + `.cs`): deployment progress UI — migrated from original WTGToolbox.Wizard framework:
- `NavigationCacheMode="Required"` (page instance survives tab switches; A8 fix)
- Three-state lifecycle: no-task → return / return-and-reconnect (snapshot replay) / new-deployment → full reset
- `TerminalOutputBuffer.Shared` snapshot replay (history survives tab switch-away)
- 100ms `DispatcherTimer` throttled snapshot diff rendering
- `TaskContentCard` items (DP visibility, single ProgressRing, hover highlight)
- `TerminalBox` (RichTextBlock, 5000-line cap, auto-scroll pause, Ctrl+C, CJK font fallback)
- Disk perf toolbar (`_orchestrator.DiskNumber`), Wrap/Freeze toggles, AbortButton (CTS cancel)
- `RunDeploymentAsync` finally: flush + stop monitor + disconnect + dispose CTS + `_orchestrator?.Dispose()`

### WTGWizard.Main.DeploymentCore — Deployment Engine

| Directory | Responsibility |
|-----------|---------------|
| `Models/` | `DeploymentConfig`, `DeploymentConstants` (single source of deployment execution parameters: Worker command timeouts Timeout*Ms), `DeployTaskId` (verb-object), `DeployTaskItem`, `TaskUpdate`, `StepResult`, `WorkerCommand`, `DeploymentResult`, `DeployTaskStatus`, `WorkerExecutionResult` |
| `Contracts/` | `IDeploymentOrchestrator`, `IDeploymentStep` (+`TitleKey`/`DescriptionKey`), `IWorkerProcess`, `IStepContext`, `IDeploymentPipeline`, `IAnswerFileProvider` |
| `Orchestrator/` | `DeploymentOrchestrator` (pipeline + `DiskNumber` + localized task list), `StepContext`, `DeploymentPipeline`, `DeploymentStepBase` |
| `Steps/` | 7 steps (class names are behavior-based; TaskId uses verb-object — see the DeployTaskId table for the mapping): `PartitionStep`, `ExtractStep`, `DriverStep`, `ImportAnsFileStep`, `ApplyWtgStep`, `BcdbootStep`, `CleanupStep` |
| `Worker/` | `WorkerProcess` (UTF-8 stdout/stderr read → `TerminalOutputBuffer`), `WorkerCommandFactory`, `CommandBuilder`, `WorkerSettings` |
| `Builders/` | `DiskScriptBuilder` (PowerShell scripts, forces `[Console]::OutputEncoding=UTF8`), `AnswerFileGenerator`, `TempFileManager` |

### WTGWizard.Shared.Services — Service Layer

| Service | Purpose |
|---------|---------|
| `DiskIOService` | Disk enumeration (SetupAPI), partition queries, safety checks, device monitoring — split into `DiskIOReader` / `DiskIOWriter` (⚠️ PInvoke rewrite in progress; see TODO) / `DiskIOWatcher` |
| `DriveLetterService` | Two-phase drive letter assignment (fallback chains in `Models/DiskConstants.cs`) |
| `DiskPerformanceMonitor` | Disk perf counters (TaskPage toolbar) |
| `WimService` (namespace `WTGWizard.Shared.Services.WimService`) | WIM operations via ManagedWimLib — **single wimlib load point** (`Wim.GlobalInit`); `ExtractImageAsync` reports progress **only on EXTRACT_STREAMS** messages (stage messages via `WimExtractStage` callback); `DiskConstants.BytesPerGiB` |
| `LoggerService` | Serilog: Debug sink + optional File sink (day rolling, `fileNameTemplate`, retains 7 days) |
| `TerminalOutputBuffer` | Thread-safe snapshot buffer (Worker stdout writes, TaskPage reads; skips snapshot build when no subscribers) |

**Disk Models** (`DiskServices/Models/`): `DiskBasicInfo`, `PartitionBasicInfo`, `DiskConstants` (the **single source** of disk physical layout: GPT GUIDs (Guid + PS strings), partition layout parameters, CleanInstall fixed partition numbers, drive-letter fallback chains, EFI size range; reserved entries for DiskIOWriter PInvoke)

### WTGWizard.Shared.Common — IPC Protocol

| Class | Purpose |
|-------|---------|
| `PipeProtocol` | Message type constants + JSON builders (AOT-compatible, hand-rolled) |
| `PipeServer` | Main-side: creates NamedPipe server, waits for Worker connection |
| `PipeReader` | Reads newline-delimited JSON, dispatches typed events |
| `PipeWriter` | Worker-side: connects to Main's pipe, sends messages |

### WTGWizard.Worker — Out-of-Process Worker

Entry point: `Program.cs` → parse command → dispatch to handler

| Command | Status | Purpose |
|---------|--------|---------|
| `pwsh` | ✅ | PowerShell script execution |
| `dism` | ✅ | DISM operations |
| `bcdboot` | ✅ | Boot configuration |
| `filecopy` | ✅ | File copy operations |
| `extract` | ✅ | WIM extraction via `SharedServices.WimService` (progress on EXTRACT_STREAMS; stage messages + 5s-throttled progress via stdout) |

**Files**: `Commands/` (5 command classes + `CommandArgs`), `Encoding/EncodingResolver.cs`, `ProcessRunner.cs`, `PipeHelper.cs` (reuses `Shared.Common.PipeWriter` + three-way handshake), `WorkerCancellation.cs`, `WorkerDebug.cs`, `Models/WorkerResult.cs`

**Encoding** (`Encoding/EncodingResolver.cs`): per-executable output decoding — PowerShell = UTF-8 (scripts force it), DISM/BCDBoot = system OEM code page (adaptive: zh-CN 936 / en-US 437 / ja-JP 932). Worker stdout/stderr are UTF-8 (stream-wrapped, no `Console.OutputEncoding` dependency).

**Logging**: `LoggerService(enableFile: true, fileNameTemplate: "WTGWorker-.log")` — Worker system logs (Serilog, day rolling, retains 7). No tee mirror: operation messages go only to stdout/stderr pipes → TerminalBox. Worker cleans nothing manually (Serilog handles retention).

### WTGWizard.Main.Language — Localization

- `Lang.resx` — English (default)
- `Lang.zh-CN.resx` — Chinese (Simplified)
- `Lang.Designer.cs` — Auto-generated strongly-typed accessor
- `Localization.cs` — ResourceManager accessor (`GetString(name[, culture])`), runtime lookups without Designer
- All UI strings are localized
- Task title/desc keys use verb-object naming: `Task.CreateDiskLayout.Title` / `Task.CreateDiskLayout.Desc` (`.Desc.Esp` / `.Desc.EspOs` variants for RemoveDriveLetters)

---

## Key Patterns

### Dependency Injection

All services and ViewModels are **singletons** registered in `App.xaml.cs` → `ConfigureServices()`.

```csharp
// Registration
services.AddSingleton<ILoggerService>(sp => new LoggerService());
services.AddSingleton<IDiskIOService, DiskIOService>();
services.AddSingleton<WizardViewModel>();

// Usage in pages
var vm = App.Services.GetRequiredService<WizardViewModel>();
```

### MVVM (CommunityToolkit.Mvvm)

```csharp
// Source generators for properties
[ObservableProperty] public partial string FilePath { get; set; } = string.Empty;

// Source generators for commands
[RelayCommand]
private void GoBack() { ... }

// Cascading property change notifications
[NotifyPropertyChangedFor(nameof(CanGoForward))]
[NotifyPropertyChangedFor(nameof(IsCurrentStepValid))]
public partial int CurrentStep { get; set; }
```

Note: `[ObservableProperty]` does NOT work in WinUI control classes (`UserControl` base — source generator does not emit properties; XAML pass2 cascades WMC9999). Use manual INPC with `SetXxx` dedup helpers there (see `TaskContentCard`).

### Sub-ViewModel Composition

`WizardViewModel` composes 5 sub-VMs. Each sub-VM manages its own state and validation:

```csharp
public ImageConfigVM Image { get; } = new();
public DeployMethodVM Method { get; }
public DeployOptionsVM Options { get; } = new();
public AdvancedOptionsVM Advanced { get; } = new();
public ConfirmVM Display { get; private set; } = null!;
```

Sub-VMs notify parent via `PropertyChanged` events for `IsValid` changes.

### Page Lifecycle

Pages implement `ITabActivatable` for tab switching:

```csharp
public sealed partial class DeployMethodPage : Page, ITabActivatable
{
    protected override void OnNavigatedTo(NavigationEventArgs e) { ... }
    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e) { ... }
    public void OnTabActivated() { ... }
    public void OnTabDeactivated() { ... }
}
```

`WizardHost` navigates pages via `Frame.Navigate(StepTypes[step], VM, transitionInfo)`.

### Message-Driven Navigation

Cross-page navigation uses `WeakReferenceMessenger`:

```csharp
// Send
WeakReferenceMessenger.Default.Send(new NavigateToPageMessage("TaskPage"));

// Receive (in MainWindow)
WeakReferenceMessenger.Default.Register<NavigateToPageMessage>(this, (r, m) => { ... });
```

### IPC Protocol

Newline-delimited JSON over Named Pipes — **every message must end with `PipeProtocol.NewLine`** (`ReadLine()` frames on `\n`):

```json
{"type":"task_progress","task":"extract","percent":45.2}
```

- AOT-compatible (hand-rolled JSON, no System.Text.Json source generators)
- Pipe naming: `WTGWizardWorker_{PID}`
- 15-second connection timeout
- One-way (Worker → Main); task status via `task_running/progress/completed/failed`

### Terminal Output Pipeline (Worker stdout)

```
Worker Console.WriteLine (UTF-8) → stdout pipe
  → Main WorkerProcess (StandardOutputEncoding=UTF8) → TerminalOutputBuffer.Shared.Append
  → TaskPage 100ms-throttled snapshot diff → TerminalBox
```

- stdout = human-readable operation messages; stderr → `[ERR]` prefix
- NamedPipe = structured task status (cards/progress rings)
- Worker decodes child tools per `EncodingResolver`; its own stdout/stderr are always UTF-8

---

## Conventions

### Resource Key Naming

Resource keys use `.` separator in `.resx` files:

```
Page.Task.WrapToggle
Task.CreateDiskLayout.Title
Task.RemoveDriveLetters.Desc.EspOs
```

C# property names use `_` separator (auto-converted):

```csharp
Lang.Page_Task_WrapToggle
Lang.Task_CreateDiskLayout_Title
```

XAML bindings use the C# property name:

```xml
<TextBlock Text="{x:Bind lang:Lang.Page_Task_WrapToggle}" />
```

### DeployTaskId Naming (verb-object)

| Step class | Field | Value | TitleKey |
|---------|-------|-------|----------|
| `PartitionStep` | `CreateDiskLayout` | create-disk-layout | Task.CreateDiskLayout.Title |
| `ExtractStep` | `ExtractImage` | extract-image | Task.ExtractImage.Title |
| `DriverStep` | `IntegrateDrivers` | integrate-drivers | Task.IntegrateDrivers.Title |
| `ImportAnsFileStep` | `ImportAnswerFile` | import-answer-file | Task.ImportAnswerFile.Title |
| `ApplyWtgStep` | `ApplySysSettings` | apply-sys-settings | Task.ApplySysSettings.Title |
| `BcdbootStep` | `CreateBootFiles` | create-boot-files | Task.CreateBootFiles.Title |
| `CleanupStep` | `RemoveDriveLetters` | remove-drive-letters | Task.RemoveDriveLetters.Title |

DeployTaskId is independent from Worker pipe task names (dism/bcdboot/pwsh/extract/filecopy).

### File Naming

| Type | Naming | Example |
|------|--------|---------|
| Page | `{Name}Page.xaml` | `DeployOptionsPage.xaml` |
| ViewModel | `{Name}VM.cs` | `DeployMethodVM.cs` |
| Service Interface | `I{Name}Service.cs` | `IDiskIOService.cs` |
| Service Impl | `{Name}Service.cs` | `DiskIOService.cs` |
| UserControl | `{Name}Control.xaml` | `FilePickerControl.xaml` |
| Record Model | `{Name}Info.cs` | `DiskBasicInfo.cs` |

### Namespace Convention

| Project | Root Namespace |
|---------|---------------|
| Main | `WTGWizard` (pages: `WTGWizard.Pages.Steps`, VMs: `WTGWizard.ViewModels`) |
| DeploymentCore | `WTGWizard.Main.DeploymentCore` (Steps/Orchestrator/Worker/Builders) |
| Shared.Services | `WTGWizard.Shared.Services` (Wim: `WTGWizard.Shared.Services.WimService`, Logger: `WTGWizard.Shared.Services.Logger`, Disk: `WTGWizard.Shared.Services.DiskServices`) |
| Shared.Common | `WTGWizard.Shared.Common` |
| Main.Language | `WTGWizard.Main.Language` |
| Worker | `WTGWizard.Worker` (Commands/Encoding) |

### Logging

```csharp
// Via ILoggerService
_logger.Debug("DiskService", "GetPartitions for disk {Index}", diskIndex);
_logger.Error("WimService", "Extract failed: {Error}", ex.Message);

// Category is the first parameter, message template uses {Placeholder} syntax
```

- Main: Serilog file `WTGWizard-*.log` (day rolling, retains 7) + VS Debug output
- Worker: Serilog file `WTGWorker-*.log` (day rolling, retains 7) — system logs only
- TerminalBox shows operation messages (stdout), NOT Serilog-formatted logs

---

## Common Tasks

### Adding a New Wizard Step

1. Create `ViewModels/{Name}VM.cs` with `[ObservableProperty]` fields
2. Create `Pages/Steps/{Name}Page.xaml` + `.xaml.cs` implementing `ITabActivatable`
3. Add to `WizardHost.xaml.cs`:
   - `StepTypes` array
   - `StepResourceKeys` array
4. Add resource keys to `Lang.resx` + `Lang.zh-CN.resx`
5. Update `Lang.Designer.cs` (run custom tool in VS or manually add properties)
6. Add VM to `WizardViewModel` if needed
7. Update `IsCurrentStepValid` switch expression

### Adding a New Service

1. Create interface `I{Name}Service.cs` in `Shared.Services`
2. Create implementation `{Name}Service.cs`
3. Register in `App.xaml.cs` → `ConfigureServices()`
4. Inject via `App.Services.GetRequiredService<I{Name}Service>()`

### Adding a New Worker Command

1. Create `Commands/{Name}Command.cs` with static `Run(string[] args, ...)` method
2. Register in `Program.cs` command dispatch switch
3. Parse args via `CommandArgs.GetArg(args, "--name")`
4. Report status via `pipe.WriteRunning()`, `pipe.WriteCompleted()`, `pipe.WriteFailed()`
5. If the command invokes a child process, decoding is handled by `EncodingResolver.Resolve(fileName)` (no manual encoding)

### Adding a New Deployment Step

1. Create `Steps/{Name}Step.cs` implementing `IDeploymentStep` (class name = behavior, `TaskId` = verb-object per DeployTaskId table above)
2. Add resource keys `Task.{VerbObject}.Title` / `Task.{VerbObject}.Desc` to `Lang.resx` + `Lang.zh-CN.resx` + `Lang.Designer.cs`
3. Register in `WizardViewModel.StartDeploy()` pipeline via `AddStep<{Name}Step>()`

### Adding Localization Resources

1. Add entries to `Lang.resx` (English)
2. Add entries to `Lang.zh-CN.resx` (Chinese)
3. Add properties to `Lang.Designer.cs` (or run `PublicResXFileCodeGenerator`)
4. Use in XAML: `{x:Bind lang:Lang.Page_WizStep_XXX_YYY}`
5. Use in code: `Lang.Page_WizStep_XXX_YYY`

---

## Current State & TODOs

| Area | Status | Location |
|------|--------|----------|
| 5-Step Wizard UI | ✅ Complete | `Pages/Steps/` |
| TaskPage (full framework) | ✅ Complete | `Pages/TaskPage.xaml` + `.cs`, `UserControls/TaskContentCard.xaml`, `TerminalBox.xaml` (with dual-state SwitchPresenter) |
| Disk Services | ✅ Complete | `Shared.Services/DiskServices/` |
| WIM Service (extract + stages) | ✅ Complete | `Shared.Services/WimService/` |
| Image Verification (manual, 4-state) | ✅ Complete | `ImageConfigVM.VerifyStatus` (Idle/Verifying/Succeeded/NotPass/Failed/Unknown) + three-state InfoBar + progress/cancel |
| ImageFileGuard (program-lifetime handle) | ✅ Complete | `Shared.Services/WimService/ImageFileGuard.cs` |
| WimVerificationException (verify-fail vs open-fail) | ✅ Complete | `Shared.Services/WimService/WimVerificationException.cs` |
| IPC Protocol | ✅ Complete | `Shared.Common/` |
| Worker Commands (5/5) | ✅ Complete | `Worker/Commands/` incl. ExtractCommand |
| Deployment Pipeline (7 steps) | ✅ Complete | `Main.DeploymentCore/Steps/` |
| Deployment Orchestrator | ✅ Complete | `Main.DeploymentCore/Orchestrator/` |
| Encoding Adapter | ✅ Complete | `Worker/Encoding/EncodingResolver.cs` |
| Terminal Buffer + Logging | ✅ Complete | `Shared.Services/TerminalOutputBuffer.cs`, `LoggerService/` |
| SettingsPage | ✅ Complete | `Pages/SettingsPage.xaml` (Worker `--debug` toggle, localized) |
| Global CardBorderStyle | ✅ Complete | `App.xaml` (`CardBorderStyle`, reused by ConfirmPage/ImageInfoCard/WizardHost) |
| Debug Build Dialog | ✅ Complete | `MainWindow.xaml.cs` (`#if DEBUG` startup dialog; keys `App.Dialog.DebugBuild.*`) |
| DiskIOWriter | ⚠️ Stub | `Shared.Services/DiskServices/DiskIOService/DiskIOWriter.cs` (PInvoke Implementation to replace Powershell disk layout creation script.) |
| Constant consolidation (three overlapping copies) | ✅ Resolved | `WinBuildConstants` (UI build thresholds) / `DeploymentConstants` (Worker timeouts) / `DiskConstants` (single source of disk layout) — see Pitfall 18 |
| Build script (PublishProfile-based, single mode) | ✅ Complete | `BuildArtifacts.ps1` (`-BuildType FDD/SCD` single mode + publish parameters provided by `Properties/PublishProfiles/*.pubxml` + parallel/isolation mechanisms removed; see Pitfall 20 for the mechanism and gotchas; see Pitfall 22 for the Profile transition) |
| SCD trimming + PDB source elimination | ✅ Complete | SCD pubxml `PublishTrimmed=true` + `TrimMode=partial` (disabled for FDD — nothing to trim without a runtime); `Directory.Build.props` Release `DebugType=None` eliminates ProjectReference PDBs — zip 110→90MB; see Pitfall 24 for the cost |
| WASDK subpackage allowlist (artifact slimming) | ✅ Complete | `Main.csproj` 11-reference lock set (metapackage anchor + allowlist of 5 + denylist of 5 with `ExcludeAssets="all"`) — SCD extraction 250→182MB / 497 files; the desktop runtime pack (WPF/WinForms ~50MB) disappears along with the AI/ML denylist; see Pitfall 23 for the structure and update procedure |
| Artifact determinism (file-content level) | ✅ 496/497 | Under the same commit + locked SDK + locked package graph, per-file SHA256 matches across machines; `WTGWizard.Main.dll` is an attributed exception (non-deterministic XAML compiler objN numbering) — see Pitfall 25 for scope/runbook/exemption rationale |
| Launcher + new release structure | ✅ Complete | Native C launcher `src/WTGWizard.Launcher` (produces `WTGWizard.exe`; locates and launches `WTGWizard-v{version}\WTGWizard.Main.exe`); zip root = launcher + application subdirectory; see Pitfall 26 for the choice and build gotchas |

---

## Pitfalls & Gotchas

1. **XAML Compiler Error WMC9999**: Pre-existing Windows App SDK issue, not caused by code changes. Ignore.

2. **File Locking**: If `dotnet build` fails with file lock errors (MSB3021/MSB3027), stop trying `dotnet build`, tell the user what's happening, then continue/finish the task.

3. **TwoWay Binding Cascade**: `Partitions.Clear()` triggers ComboBox TwoWay binding to set `SelectedPartition = null`. Always save/restore selection before clearing collections.

4. **Tab Activation Double Refresh**: `OnNavigatedTo` + `OnTabActivated` both fire on tab switch. ImageConfigPage has **no (path, index) cache** — it reloads image info on every return (per design); a `_refreshSeq` guard discards stale async results, and file selection is **event-driven** (`SelectedIndex` change triggers `RefreshImageStateAsync`) to avoid duplicate loads.

5. **Resource Key Format**: `.resx` uses `.` separator, C# uses `_` separator, XAML uses `_` separator.

6. **Worker Process**: Worker is NOT a project reference. It's copied via MSBuild targets. Don't add it as a `<ProjectReference>`.

7. **Native Library**: `libwim-15.dll` is copied from the NuGet cache (`$(PkgManagedWimLib)\runtimes\win-x64\native\`) to `{output}/runtimes/win-x64/native/` via `<None CopyToOutputDirectory>` in `Shared.Services.csproj` (`ManagedWimLib` PackageReference has `GeneratePathProperty="true"`). ProjectReferences propagate it to Main and Worker outputs automatically. `WimService` loads it via `Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "libwim-15.dll")`. Don't keep a copy under `Native/x64/`.

8. **AOT Compatibility**: Pipe protocol uses hand-rolled JSON (no System.Text.Json source generators). Don't introduce reflection-based serialization.

9. **Serilog.Sinks.Debug**: Must be restored via `dotnet restore` before building. If `FileNotFoundException`, run `dotnet restore WTGWizard.slnx`.

10. **Lang.Designer.cs**: Not auto-updated by `dotnet build`. Must be manually regenerated in VS (right-click → Run Custom Tool) or manually edit.

11. **WIM Index vs ComboBox Position**: `ImageConfigVM.SelectedIndex` is the 0-based combo position; `Indices` holds string WIM indexes (1-based). Use `ImageConfigVM.WimIndex` (parses `Indices[SelectedIndex]`) for deployment config — never `SelectedIndex` directly.

12. **wimlib Progress Semantics**: `ExtractProgress` (completed/total) is valid on ALL extract messages (stage values 0%/100%), but `WimService` reports progress only on `EXTRACT_STREAMS` — do not report from other messages.

13. **Worker Logging**: Worker `LoggerService` writes `WTGWorker-*.log` (day rolling). TerminalBox must NOT receive Serilog-formatted logs — operation messages go via `Console.WriteLine` (stdout pipe) only.

14. **`[ObservableProperty]` in Controls**: Does not work in `UserControl`-derived XAML classes (source generator emits nothing; XAML pass2 fails). Use manual INPC + dedup setter helpers.

15. **Temp Cleanup**: `TempFileManager.Dispose` removes Scripts dir (deploy completion); `TempFileManager.CleanupAll()` runs on app close (`App.OnMainWindowClosed`) as crash-leak backstop — checks `Directory.Exists` first (avoids `DirectoryNotFoundException` first-chance noise when the temp dir was never created).

16. **WimService.Cleanup vs VerifyAsync**: `Cleanup()` (called on app close) force-cancels any in-flight `VerifyAsync` via a linked `CancellationTokenSource` and waits up to 10s before `TryGlobalCleanup()` — necessary because wimlib global cleanup during an active verify causes Access Violation. On timeout it skips cleanup (OS reclaims on process exit) rather than risking a crash. The linked-token design means BOTH page-initiated cancel (`_verifyCts`) and Cleanup-forced cancel propagate through the same callback `Abort` path.

17. **ExtractFileAsync semantics**: `targetFilePath` is a FILE path (not a directory). Internally it extracts to the target's parent dir with `ExtractFlags.NoPreserveDirStructure | ExtractFlags.NoAcls`, then `File.Move(overwrite: true)`. Using a bare `ExtractPath(target=filePath, ...)` would create `<filePath>\Windows\Panther\...` with full WIM ACLs.

18. **Three overlapping constant copies (consolidated)**: Historically GPT GUIDs/partition layout/fallback chains/timeouts were defined (partially overlapping) in three places: `Main/Models/Constants.cs`, `DeploymentCore/Models/DeploymentConstants.cs`, and `Shared.Services/DiskServices/Models/DiskConstants.cs`. On 2026-08-06 they were consolidated into three single sources: **`DiskConstants`** (the single source of disk physical layout; referencable by both Main and DeploymentCore), **`DeploymentConstants`** (only Worker command timeouts Timeout*Ms), and **`WinBuildConstants`** (only Windows build-number thresholds). New disk/deployment constants belong in these; the old files `Constants.cs`/`WimConstants.cs` have been deleted, and entries reserved in `DiskConstants` for DiskIOWriter PInvoke are annotated with a `reserved` comment.

19. **WASDK unpackaged self-contained startup crash (observations only)**: The following are observed facts about `WindowsAppSDKSelfContained=true` + unpackaged (`WindowsPackageType=None`) builds, without drawing conclusions:
   - SCD artifact crashes on startup: `0xc000027b` (stowed) / `E_FAIL` at `Application.Start` (Microsoft.UI.Xaml.dll `FailFastWithStowedExceptions`); the FDD artifact is fine.
   - Artifact differences: the SCD Main.dll embeds `UndockedRegFreeWinRTCS` and `Microsoft.WindowsAppRuntime.dll` type references (+127KB vs FDD); the SCD apphost embeds `WindowsAppRuntime`/activation references (400KB vs FDD 271KB).
   - Binary-swap experiments (pri stays at 82KB; pri is not a crash factor): SCD exe + FDD dll → crashes in Microsoft.UI.Xaml.dll (0xc000027b); FDD exe + SCD dll → crashes in CoreMessagingXP.dll (0xc0000602); FDD exe + dll → fine.
   - The local machine has `Microsoft.WindowsAppRuntime.2` 2.3.1.0 installed (same version as the project's PackageReference); FDD uses the shared registered runtime.
   - Version experiments: WASDK `1.8.260710003` and `2.3.2-experimentala` still reproduce the crash; `1.7.260224002` lacks `Microsoft.Windows.Storage.Pickers` (`FileOpenPicker`/`FileSavePicker` unavailable).
   - Upstream reference: [microsoft/WindowsAppSDK#6248](https://github.com/microsoft/WindowsAppSDK/issues/6248) (unpackaged self-contained crash on 1.8+; not reproduced on 1.7 and earlier; Open).
   - Current handling: the SCD build keeps `SelfContained=true` + `WindowsAppSDKSelfContained=true` (both in `SCD-x64.pubxml`); FDD uses `WindowsAppSDKSelfContained=false` (`FDD-x64.pubxml`); build order is irrelevant — every `BuildArtifacts.ps1` run performs a Clean (the Pitfall 19 crash only reproduces with shared obj/bin intermediates).

20. **Build script PowerShell 5.1 gotchas (BuildArtifacts.ps1)**:
   - **Native stderr + `$ErrorActionPreference=Stop`**: in `& exe 2>&1 | Out-Null`, stderr lines throw a `RemoteException` that terminates the script. When calling external commands such as 7za, do **not** merge stderr (`-bso0 -bsp0` to silence it is enough).
   - **Bare-token wildcard expansion**: `-x!*.pdb` as a bare argument gets wildcard-expanded by PS 5.1, shifting arguments (the 7za archive name gets treated as an input file). Exclusion patterns must be **passed via a variable** (`$excludePdb = '-x!*.pdb'`).
   - **`Expand-Archive` only accepts the `.zip` extension** (it does not validate content): before extracting a nupkg, copy/rename it to `.zip`.
   - **`DefaultItemExcludes` vs `ItemGroup Remove` (Directory.Build.props)**: the SDK's default Compile glob is added during the targets phase (after props), so a `Remove` in props does not affect items added later (CS0579 duplicate attribute). Use `DefaultItemExcludes` instead (set in props; it controls default-glob exclusion). (Historical entry: required in the multi-mode obj\fdd\obj\scd era; after the Profile transition a single obj is covered by the SDK's default exclusion, and this configuration was removed along with the PDB change — see Pitfall 24.)
   - **`makepri dump` blocks**: when its stdout is piped, `makepri dump` waits on stdin (overwrite confirmation/EOF). Must use `Start-Process` + `-RedirectStandardInput` (an empty file) + a `WaitForExit(60s)` timeout kill + the existence of the output file as the success criterion. (Historical entry: gotchas such as `param`/`$script:` name collisions, empty `Start-Process` ExitCode, and child-process success marker files were removed along with the parallel child-process mode — the parallel mechanism is no longer used as of 2026-08.)

21. **Build script moved to PublishProfiles (BuildArtifacts.ps1 + Properties/PublishProfiles)**: the **single source of publish parameters** (`SelfContained`/`WindowsAppSDKSelfContained`/`PublishTrimmed`/`Platform`/`RuntimeIdentifier`/`Configuration`) is `Properties/PublishProfiles/{FDD|SCD}-x64.pubxml`; the script/CI only pass `-p:PublishProfile=…` + `-p:PublishDir=…` + `-p:Version=…`:
   - **Profiles are orthogonal to build wiring**: `BaseIntermediateOutputPath`/`BaseOutputPath` no longer need per-mode injection; the script always performs a Clean per run, so single-mode local builds (FDD and SCD run separately) are naturally free of intermediate-product contamination.
   - **Multiple concurrent local instances are not supported**: only one `BuildArtifacts.ps1` instance may run at a time (the default obj/bin has no isolation); parallelism is handled by the GitHub Actions matrix (`[FDD, SCD]` dual jobs in `dotnet-ci.yml`/`dotnet-manual.yml`/`dotnet-tag.yml`, each on its own VM).
   - **Single restore**: `--locked-mode` with a single obj, not differentiated by mode.
   - **Separate Worker verification directory**: the Worker is first published to `build/Worker-{mode}/` (not included in the zip; only to verify its Profile works); after Main is published, the csproj `CopyWorkerBuildOutputToPublish` injects the Worker's **Build output** (not Publish output) into Main's output — for Main SCD, Main provides the .NET/WASDK runtime and the Worker shares the runtime from the same directory.

22. **The Worker copy target path must include the RID segment (WTGWizard.Main.csproj)**: the `WorkerOutputPath` of `CopyWorkerBuildOutput*` is `..\WTGWizard.Worker\bin\$(Platform)\$(Configuration)\$(TargetFramework)\$(RuntimeIdentifier)` (with the RID appended when `RuntimeIdentifier` is non-empty). **Without the RID segment the target silently copies zero files** — an earlier version only appeared to "work" because "the Worker had already been published to the same directory + `-o` did not clean up leftovers"; that implicit path was removed after the Profile transition. Also: `dotnet publish -o`/`-p:PublishDir` **does not clean the target directory**; relying on "the publish directory may contain stale files" is considered a bug.

23. **WASDK subpackage allowlist lock set (Main.csproj, artifact slimming)**: the metapackage `Microsoft.WindowsAppSDK` 2.4.0 pulls in AI/ML/Search/Widgets components (onnxruntime.dll 20.7MB + DirectML.dll 17.8MB + Search/Widgets/AI projections, ~45MB extracted in total), none of which Main's source uses — `Main.csproj` replaces the bare metapackage with an **11-reference lock set** (SCD extraction 250→182MB / 497 files):
     - **Structure**: metapackage anchor (`ExcludeAssets="all"`; participates only in version resolution) + an allowlist of 5 normal references (`WinUI 2.3.6`/`Foundation 2.3.9`/`Base 2.0.4`/`Runtime 2.4.0`/`DWrite 2.1.0`) + a denylist of 5 (`AI 2.4.4`/`ML 2.1.74`/`Search 2.4.4`/`Widgets 2.0.5`/`Windows.AI.MachineLearning 2.1.74`, all `ExcludeAssets="all"` to silence their buildTransitive copies).
     - **Gotcha a (the anchor is required)**: the three CommunityToolkit packages transitively require `Microsoft.WindowsAppSDK >= 1.6.250108002` — a bare allowlist (no anchor) lets the old 1.x metapackage float up, whose embedded WinUI targets duplicate the import of `microsoft.windowsappsdk.winui` 2.3.6 (MSB4011 + MSIX `CustomBeforeMicrosoftCommonTargets` errors).
     - **Gotcha b (MinVersion check)**: the `Microsoft.Windows.AI.MachineLearning` 2.1.74 targets enforce `SupportedOSPlatformVersion >= 18362` (the project min is 17763) — it must be denylisted; you cannot get around it by "not referencing" it (the transitive dependency is still in the graph).
     - **Gotcha c (Pickers ownership)**: the winmd for `Microsoft.Windows.Storage.Pickers` (FileOpenPicker/FileSavePicker) is in the **Foundation** package — do not remove Foundation from the allowlist.
     - **Side effect (the desktop pack disappears as well)**: the injection of the WindowsDesktop.App runtime pack (the full WPF/WinForms stack, ~50MB) is **coupled at build time to AI/ML asset import** (.NET 10 windows TFM framework `Microsoft.Windows.SDK.NET.Ref.Windows`; during evaluation there is no desktop FrameworkReference, so all WASDK package targets miss and the usual `FrameworkReference Remove` has nothing to target) — after the AI/ML denylist silences it, the desktop framework disappears from all three of the runtimeconfig `includedFrameworks`, deps.json, and the artifacts. Only a 16KB `WindowsBase.dll` remains (a trimmed empty shell; consistent with deps.json; harmless).
     - **Update procedure (the versions differ from one another; you cannot just bump one number)**: (1) temporarily turn the anchor into a normal reference (or use an out-of-repo probe project) and restore, then read each subpackage's resolved version from lock.json; (2) align all 11 version numbers and restore the anchor to `ExcludeAssets="all"`; (3) diff lock.json — judge each newly added unknown metapackage subpackage individually (used → allowlist, unused → denylist); (4) `dotnet restore` to update the lock → the script's full `--locked-mode` flow; (5) artifact verification: probe publish diff shows no onnxruntime/DirectML/Search/Widgets/AI, the allowlist core is present, **runtimeconfig includedFrameworks contains only Microsoft.NETCore.App** (desktop pack returning = the injection mechanism changed, stop and investigate), FileOpenPicker/image verify/startup smoke test, and FDD passes the same.
     - **Escape hatch**: reverting to the bare metapackage is one line → back to the ~250MB state, with no functional cost.
     - Verification baseline: SCD startup smoke test ✅ / Worker command dispatch ✅ / FDD 79 files ✅.

24. **No PDBs in Release (eliminated at the source)**: `src/Directory.Build.props` sets `DebugType=None` + `DebugSymbols=false` repo-wide for Release — pubxml properties are not global and are invisible to ProjectReference projects, so PDBs must be eliminated at the props layer (the 7za `-x!*.pdb`/ZipFile fallback exclusion in BuildArtifacts.ps1 is downgraded to a redundant safeguard). **Cost**: Release builds have no symbols and crash stacks have no line numbers; investigation requires a temporary local rebuild with symbols (remove that block or change to `DebugType=embedded`). The `PathMap` in the same file was once removed during a rewrite and later restored (applied unconditionally; Debug PDBs benefit too; the multi-mode obj exclusion in `DefaultItemExcludes` is covered by the SDK's default exclusion after the Profile transition to a single obj and remains removed — see Pitfall 20).

25. **Artifact determinism (file-content level, 496/497 + Main.dll exemption)**: equal-input definition = same commit + SDK at the `global.json` baseline version (`rollForward: latestMinor`; this repo's baseline is 10.0.400, and the actual value may drift to 10.0.x — cross-machine comparison must use the actual `dotnet --version` recorded in the diagnostics log) + lock `--locked-mode` package graph + the same `-p:Version` (the script's default constant 1.0.0) + `Directory.Build.props`'s `PathMap` (source-path mapping; a defensive layer when Release has no PDBs). Verification conclusion (2026-08-28, local zh-CN vs GA en-US runner, SCD 497 files): **all files except `WTGWizard.Main.dll` have identical cross-machine SHA256**.
     - **Main.dll exemption rationale (attributed; not an environment difference)**: the WinUI XAML compiler assigns globally increasing numbers to x:Bind binding classes (`{Page}_objN_Bindings`) in **parallel-processing completion order**; the numbers are non-deterministic → generated type names differ (measured: GA `DeployMethodPage_obj42` vs local `obj20`) → the CsWinRT source generator consequently produces different `VtableClasses`/`WinRTTypeDetails` → Main.dll metadata/IL/layout drifts. **Two Clean rebuilds on the same machine also yield different hashes** (`0D9D5FA5…` vs `49C60A12…`) — an intrinsic compiler non-determinism, not a locale/path/environment issue. Only the one XAML assembly is affected (the other 496 files confirm this). There is no known public compiler switch to fix the numbering; report an issue to microsoft-ui-xaml if an upstream fix is desired.
     - **Misdiagnosis exclusion record (do not re-investigate)**: in the `-Diagnostics` MSBuild property snapshot, `ShouldComputeInputPris`/`EnableCoreMrtTooling`/`WindowsSdkBuildToolsVersion` are empty on the GA side but have values locally — this is a **collection-timing artifact** (`Collect-ProjectInfo` runs before restore; on a fresh GA checkout obj has no assets → package props are not imported) and is unrelated to the artifact difference (the Worker-side snapshot has zero differences and Worker.dll is identical).
     - **Runbook (cross-build/cross-machine comparison)**: (1) `BuildArtifacts.ps1 -BuildType SCD -Diagnostics` (Clean + fixed Version by default; the manifest lands at `build/BuildDiagnostics/SCD-x64.csv` with an environment snapshot); (2) when comparing the two CSVs you **must project out the `LastWriteTimeUtc` column** (file timestamps always differ between builds, so a direct diff always reports differences) — after `Import-Csv`, compare only `RelativePath`+`SHA256` (`Length` optional); (3) attribution order for differing files: Main.dll (known exemption) → newly appearing files → the rest.

26. **Launcher (native launcher) and release structure**: zip root = `WTGWizard.exe` (native C launcher) + `WTGWizard-v{version}\` (all Main artifacts); the launcher reads the numeric FileVersion fields from its own VERSIONINFO, composes `WTGWizard-v{a.b.c}` for an exact hit, and on failure falls back to searching first-level subdirectories that start with `WTGWizard` and contain `WTGWizard.Main.exe`; if there is still no hit, a MessageBox directs the user to GitHub Releases.
     - **Why native C (vcxproj) instead of .NET**: when an FDD-style launcher sits at the zip root, the SCD package's .NET runtime is in a subdirectory and hostfxr does not resolve across directories → a root-level .NET launcher cannot start; a native exe has zero dependencies and `/subsystem:windows` naturally means no window. The vcxproj is **not in the slnx**; `BuildArtifacts.ps1` locates VS MSBuild via vswhere to build it (`dotnet msbuild` cannot build a vcxproj).
     - **PlatformToolset must use `$(DefaultPlatformToolset)`**: hard-coding v143 gives MSB8020 on VS18 (v145), and hard-coding v145 blows up the same way on CI VS2022 (v143) — it is provided automatically by the Cpp targets of the VS actually used, adapting to local/CI.
     - **Version injection must go through environment variables (cross-shell command-line quoting traps)**: a comma numeric value (`1,0,0,0`) must be quoted on the command line (a bare comma = MSB1006), but how quoted arguments are escaped differs across shells — pwsh 7 (the default GA Windows shell; `PSNativeCommandArgumentPassing` defaults to Standard, and `Windows` mode does not byte-for-byte replicate PS 5.1 either) escapes embedded quotes into a literal `\"` → CI once produced a cascade of MSB1008/RC1109. **Solution**: inject via `$env:WTGW_LAUNCHER_VER_NUM/STR` (the msbuild child process inherits environment variables, which automatically become MSBuild properties), keeping the msbuild arguments **free of any quote characters**; explicit `/p:` overrides still work. Diagnostic method: check whether `\"` appears in the Full command line of the MSBuild error.
     - **Strict same-source version constraint**: the rc's `ProductVersion` (string) and the Main version share `$MainVer` (injected via the `LauncherVersionNumeric/String` properties) → the directory name `WTGWizard-v{ver}` and the launcher probe stay strictly consistent; for a prerelease (e.g. `1.0.0-preview1`) the FileVersion numeric fields do not include the suffix → the exact hit degrades to the fallback search (no functional loss).
     - **UAC chain**: Main is `requireAdministrator` — the launcher must use `ShellExecuteExW` (`CreateProcess` reports `ERROR_ELEVATION_REQUIRED`); if the user declines UAC (`ERROR_CANCELLED`), it exits silently.
     - **Attribution**: `WTGWizard.Launcher.cpp` contains adapted fragments from Starward.Launcher (MIT); the file header retains Scighost's copyright notice; THIRD-PARTY-NOTICES entry 12.
