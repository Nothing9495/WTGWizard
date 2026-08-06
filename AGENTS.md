# AGENTS.md

## Project Overview

**WTGWizard** is a Windows To Go (WTG) deployment wizard — a WinUI 3 desktop application that guides users through creating Windows To Go workstations.

- **Stack**: C# / .NET 10 / WinUI 3 (Windows App SDK 2.3)
- **Platform**: x64 only, Windows 10 1809+ (min version 10.0.17763.0)
- **Architecture**: Main (WinUI host) + Worker (out-of-process console app) communicating via Named Pipes

---

## Project Structure

```
src/
├── WTGWizard.Main/            # WinUI 3 主应用 (WinExe)
├── WTGWizard.Main.Language/   # 本地化资源 (.resx)
├── WTGWizard.Main.DeploymentCore/  # 部署引擎（模型/步骤/编排器/Worker 桥接）
├── WTGWizard.Shared.Services/ # 核心服务：磁盘、WIM、日志、终端缓冲
├── WTGWizard.Shared.Common/   # Named Pipe IPC 协议
└── WTGWizard.Worker/          # Worker 子进程 (Exe)
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

```powershell
# Build
dotnet build WTGWizard.slnx

# Publish
dotnet publish src/WTGWizard.Main -c Release -r "win-x64" -o "build" -p:Platform=x64 -p:Version=1.0.0;
```

- **SDK**: .NET 10.0.302 (rollForward: latestMajor)
- **Target Framework**: `net10.0-windows10.0.26100.0`
- **Language Version**: `preview`
- **Platform**: x64 only
- **Package Type**: Unpackaged (not MSIX), uses `app.manifest`

---

## Module Responsibilities

### WTGWizard.Main — UI Layer

**Entry Point**: `App.xaml.cs` → `OnLaunched()` → DI setup → `MainWindow`

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
| `Models/` | `DeploymentConfig`, `DeployTaskId` (verb-object: `CreateDiskLayout`, `ApplySysSettings`...), `DeployTaskItem`, `TaskUpdate`, `StepResult`, `WorkerCommand` |
| `Contracts/` | `IDeploymentOrchestrator`, `IDeploymentStep` (+`TitleKey`/`DescriptionKey`), `IWorkerProcess`, `IStepContext`, `IDeploymentPipeline` |
| `Orchestrator/` | `DeploymentOrchestrator` (pipeline + `DiskNumber` + localized task list), `StepContext`, `DeploymentPipeline`, `DeploymentStepBase` |
| `Steps/` | 7 steps: `CreateDiskLayout`, `ExtractImage`, `IntegrateDrivers`, `ImportAnswerFile`, `ApplySysSettings`, `CreateBootFiles`, `RemoveDriveLetters` |
| `Worker/` | `WorkerProcess` (UTF-8 stdout/stderr read → `TerminalOutputBuffer`), `WorkerCommandFactory`, `CommandBuilder` |
| `Builders/` | `DiskScriptBuilder` (PowerShell scripts, forces `[Console]::OutputEncoding=UTF8`), `AnswerFileGenerator`, `TempFileManager` |

### WTGWizard.Shared.Services — Service Layer

| Service | Purpose |
|---------|---------|
| `DiskIOService` | Disk enumeration (SetupAPI), partition queries, safety checks, device monitoring |
| `DriveLetterService` | Two-phase drive letter assignment |
| `WimService` (namespace `WTGWizard.Shared.Services.WimService`) | WIM operations via ManagedWimLib — **single wimlib load point** (`Wim.GlobalInit`); `ExtractImageAsync` reports progress **only on EXTRACT_STREAMS** messages (stage messages via `WimExtractStage` callback) |
| `LoggerService` | Serilog: Debug sink + optional File sink (day rolling, `fileNameTemplate`, retains 7 days) |
| `TerminalOutputBuffer` | Thread-safe snapshot buffer (Worker stdout writes, TaskPage reads; skips snapshot build when no subscribers) |

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

**Encoding** (`Encoding/EncodingResolver.cs`): per-executable output decoding — PowerShell = UTF-8 (scripts force it), DISM/BCDBoot = system OEM code page (adaptive: zh-CN 936 / en-US 437 / ja-JP 932). Worker stdout/stderr are UTF-8 (stream-wrapped, no `Console.OutputEncoding` dependency).

**Logging**: `LoggerService(enableFile: true, fileNameTemplate: "WTGWorker-.log")` — Worker system logs (Serilog, day rolling, retains 7). No tee mirror: operation messages go only to stdout/stderr pipes → TerminalBox. Worker cleans nothing manually (Serilog handles retention).

### WTGWizard.Main.Language — Localization

- `Lang.resx` — English (default)
- `Lang.zh-CN.resx` — Chinese (Simplified)
- `Lang.Designer.cs` — Auto-generated strongly-typed accessor
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

Newline-delimited JSON over Named Pipes:

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

| Field | Value | TitleKey |
|-------|-------|----------|
| `CreateDiskLayout` | create-disk-layout | Task.CreateDiskLayout.Title |
| `ExtractImage` | extract-image | Task.ExtractImage.Title |
| `IntegrateDrivers` | integrate-drivers | Task.IntegrateDrivers.Title |
| `ImportAnswerFile` | import-answer-file | Task.ImportAnswerFile.Title |
| `ApplySysSettings` | apply-sys-settings | Task.ApplySysSettings.Title |
| `CreateBootFiles` | create-boot-files | Task.CreateBootFiles.Title |
| `RemoveDriveLetters` | remove-drive-letters | Task.RemoveDriveLetters.Title |

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

1. Create `Steps/{Name}Step.cs` implementing `IDeploymentStep` (`TaskId` verb-object, `TitleKey`/`DescriptionKey`)
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
| TaskPage (full framework) | ✅ Complete | `Pages/TaskPage.xaml` + `.cs`, `UserControls/TaskContentCard.xaml`, `TerminalBox.xaml`（含 SwitchPresenter 双态） |
| Disk Services | ✅ Complete | `Shared.Services/DiskServices/` |
| WIM Service (extract + stages) | ✅ Complete | `Shared.Services/WimService/` |
| Image Verification (manual, 4-state) | ✅ Complete | `ImageConfigVM.VerifyStatus`（Idle/Verifying/Succeeded/NotPass/Failed/Unknown）+ 三态 InfoBar + 进度/取消 |
| ImageFileGuard (program-lifetime handle) | ✅ Complete | `Shared.Services/WimService/ImageFileGuard.cs` |
| WimVerificationException (verify-fail vs open-fail) | ✅ Complete | `Shared.Services/WimService/WimVerificationException.cs` |
| IPC Protocol | ✅ Complete | `Shared.Common/` |
| Worker Commands (5/5) | ✅ Complete | `Worker/Commands/` incl. ExtractCommand |
| Deployment Pipeline (7 steps) | ✅ Complete | `Main.DeploymentCore/Steps/` |
| Deployment Orchestrator | ✅ Complete | `Main.DeploymentCore/Orchestrator/` |
| Encoding Adapter | ✅ Complete | `Worker/Encoding/EncodingResolver.cs` |
| Terminal Buffer + Logging | ✅ Complete | `Shared.Services/TerminalOutputBuffer.cs`, `LoggerService/` |
| SettingsPage | ✅ Complete | `Pages/SettingsPage.xaml`（Worker `--debug` Toggle，本地化） |
| DiskIOWriter | ⚠️ Stub | `Shared.Services/DiskServices/DiskIOService/DiskIOWriter.cs` (PInvoke Implementation to replace Powershell disk layout creation script.) |

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
