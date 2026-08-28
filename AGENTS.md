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
│   ├── Pages/                 #   MainWindow, TaskPage, WizardHost, SettingsPage, Steps/
│   ├── ViewModels/            #   WizardViewModel + 5 子 VM
│   ├── UserControls/          #   TaskContentCard, TerminalBox, ImageInfoCard, File/FolderPicker
│   ├── Helpers/               #   TitleBarHelper, WindowHelper, WindowsBuildHelper
│   ├── Models/                #   WinBuildConstants.cs（构建号阈值）
│   ├── Messages/              #   NavigateToPageMessage
│   └── Styles/                #   AbortButtonResources.xaml（TaskPage 局部引用）
├── WTGWizard.Main.Language/   # 本地化资源 (.resx) + Localization.cs 访问器
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

# Publish (发布参数唯一来源：Properties/PublishProfiles/*.pubxml，见 Pitfall 22)
dotnet publish src/WTGWizard.Main -p:PublishProfile=SCD-x64
dotnet publish src/WTGWizard.Main -p:PublishProfile=FDD-x64 -p:PublishDir=build\publish\FDD
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

**Helpers/**: `TitleBarHelper` (custom title bar), `WindowHelper` (window sizing/centering), `WindowsBuildHelper` (BootEx build-number threshold check — `MeetsBootExThreshold` via `BuildMajor*`/`BuildRevisionThreshold` from `Models/WinBuildConstants.cs`)

**Models/**: `WinBuildConstants.cs` — UI 侧唯一常量类（Windows 构建号阈值：`BuildMajor26100`/`BuildMajor26200`/`BuildRevisionThreshold`，仅供 WindowsBuildHelper）。磁盘/部署常量不在此处（见 DiskConstants/DeploymentConstants）。

**Messages/**: `NavigateToPageMessage` — WeakReferenceMessenger 跨页导航（发送到 MainWindow → Frame 切换）

**Debug-Build Warning**: `RootGrid_Loaded` 末尾 `#if DEBUG ShowDebugBuildWarning()` → ContentDialog（每次 DEBUG 启动弹出，本地化键 `App.Dialog.DebugBuild.*`）

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
| `Models/` | `DeploymentConfig`, `DeploymentConstants`（部署执行参数唯一来源：Worker 命令超时 Timeout*Ms）, `DeployTaskId` (verb-object), `DeployTaskItem`, `TaskUpdate`, `StepResult`, `WorkerCommand`, `DeploymentResult`, `DeployTaskStatus`, `WorkerExecutionResult` |
| `Contracts/` | `IDeploymentOrchestrator`, `IDeploymentStep` (+`TitleKey`/`DescriptionKey`), `IWorkerProcess`, `IStepContext`, `IDeploymentPipeline`, `IAnswerFileProvider` |
| `Orchestrator/` | `DeploymentOrchestrator` (pipeline + `DiskNumber` + localized task list), `StepContext`, `DeploymentPipeline`, `DeploymentStepBase` |
| `Steps/` | 7 steps（类名以行为命名，TaskId 用 verb-object——映射见 DeployTaskId 表）: `PartitionStep`, `ExtractStep`, `DriverStep`, `ImportAnsFileStep`, `ApplyWtgStep`, `BcdbootStep`, `CleanupStep` |
| `Worker/` | `WorkerProcess` (UTF-8 stdout/stderr read → `TerminalOutputBuffer`), `WorkerCommandFactory`, `CommandBuilder`, `WorkerSettings` |
| `Builders/` | `DiskScriptBuilder` (PowerShell scripts, forces `[Console]::OutputEncoding=UTF8`), `AnswerFileGenerator`, `TempFileManager` |

### WTGWizard.Shared.Services — Service Layer

| Service | Purpose |
|---------|---------|
| `DiskIOService` | Disk enumeration (SetupAPI), partition queries, safety checks, device monitoring — split into `DiskIOReader` / `DiskIOWriter`（⚠️ PInvoke 重写中,见 TODO）/ `DiskIOWatcher` |
| `DriveLetterService` | Two-phase drive letter assignment（fallback chains 见 `Models/DiskConstants.cs`） |
| `DiskPerformanceMonitor` | Disk perf counters (TaskPage toolbar) |
| `WimService` (namespace `WTGWizard.Shared.Services.WimService`) | WIM operations via ManagedWimLib — **single wimlib load point** (`Wim.GlobalInit`); `ExtractImageAsync` reports progress **only on EXTRACT_STREAMS** messages (stage messages via `WimExtractStage` callback); `DiskConstants.BytesPerGiB` |
| `LoggerService` | Serilog: Debug sink + optional File sink (day rolling, `fileNameTemplate`, retains 7 days) |
| `TerminalOutputBuffer` | Thread-safe snapshot buffer (Worker stdout writes, TaskPage reads; skips snapshot build when no subscribers) |

**Disk Models** (`DiskServices/Models/`): `DiskBasicInfo`, `PartitionBasicInfo`, `DiskConstants`（磁盘物理布局**唯一来源**：GPT GUID（Guid + PS 字符串）、分区布局参数、CleanInstall 固定分区号、盘符回退链、EFI 尺寸范围；预留项供 DiskIOWriter PInvoke 使用）

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

**Files**: `Commands/` (5 command classes + `CommandArgs`), `Encoding/EncodingResolver.cs`, `ProcessRunner.cs`, `PipeHelper.cs`（复用 `Shared.Common.PipeWriter` + 三次握手）, `WorkerCancellation.cs`, `WorkerDebug.cs`, `Models/WorkerResult.cs`

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

Newline-delimited JSON over Named Pipes — **every message must end with `PipeProtocol.NewLine`** (`ReadLine()` frames on `\n`)：

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

| Step 类 | Field | Value | TitleKey |
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
| Global CardBorderStyle | ✅ Complete | `App.xaml`（`CardBorderStyle`，供 ConfirmPage/ImageInfoCard/WizardHost 复用） |
| Debug Build Dialog | ✅ Complete | `MainWindow.xaml.cs`（`#if DEBUG` 启动弹窗，键 `App.Dialog.DebugBuild.*`） |
| DiskIOWriter | ⚠️ Stub | `Shared.Services/DiskServices/DiskIOService/DiskIOWriter.cs` (PInvoke Implementation to replace Powershell disk layout creation script.) |
| 常量收敛（三份重叠） | ✅ Resolved | `WinBuildConstants`（UI 构建阈值）/ `DeploymentConstants`（Worker 超时）/ `DiskConstants`（磁盘布局唯一来源）——见 Pitfall 18 |
| 构建脚本（PublishProfile 化，单模式） | ✅ Complete | `BuildArtifacts.ps1`（`-BuildType FDD/SCD` 单模式 + 发布参数由 `Properties/PublishProfiles/*.pubxml` 提供 + 并行/隔离机制已移除；机制与坑见 Pitfall 20；Profile 化见 Pitfall 22） |
| SCD 裁剪 + PDB 源头消除 | ✅ Complete | SCD pubxml `PublishTrimmed=true` + `TrimMode=partial`（FDD 关闭，无运行时可裁）；`Directory.Build.props` Release `DebugType=None` 消除 ProjectReference PDB——zip 110→90MB；代价见 Pitfall 24 |
| WASDK 子包白名单（产物瘦身） | ✅ Complete | `Main.csproj` 11 引用锁集（metapackage 锚 + 白名单 5 + 黑名单 5 `ExcludeAssets="all"`）——SCD 解压 250→182MB / 497 文件，desktop runtime pack（WPF/WinForms ~50MB）随 AI/ML 黑名单顺带消失；结构与更新流程见 Pitfall 23 |

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

18. **常量三处重叠（已收敛）**: 历史上 GPT GUID/分区布局/回退链/超时在 `Main/Models/Constants.cs`、`DeploymentCore/Models/DeploymentConstants.cs`、`Shared.Services/DiskServices/Models/DiskConstants.cs` 三处重复定义（部分重叠）。2026-08-06 已收敛为三个单一来源：**`DiskConstants`**（磁盘物理布局唯一来源，Main/DeploymentCore 均可引用）、**`DeploymentConstants`**（仅 Worker 命令超时 Timeout*Ms）、**`WinBuildConstants`**（仅 Windows 构建号阈值）。新增磁盘/部署常量按此归属；旧文件 `Constants.cs`/`WimConstants.cs` 已删除，`DiskConstants` 中为 DiskIOWriter PInvoke 预留的项标注 `reserved` 注释。

19. **WASDK unpackaged self-contained 启动崩溃（仅论据）**: 以下为 `WindowsAppSDKSelfContained=true` + unpackaged（`WindowsPackageType=None`）构建的观测事实，不做结论推导：
   - SCD 产物启动崩溃：`0xc000027b`（stowed）/ `E_FAIL` @ `Application.Start`（Microsoft.UI.Xaml.dll `FailFastWithStowedExceptions`）；FDD 产物正常。
   - 产物差异：SCD Main.dll 内嵌 `UndockedRegFreeWinRTCS` 与 `Microsoft.WindowsAppRuntime.dll` 类型引用（较 FDD +127KB）；SCD apphost 内嵌 `WindowsAppRuntime`/activation 引用（400KB vs FDD 271KB）。
   - 二分替换实验（pri 保持 82KB 不变，pri 非崩溃因素）：SCD exe + FDD dll → 崩于 Microsoft.UI.Xaml.dll（0xc000027b）；FDD exe + SCD dll → 崩于 CoreMessagingXP.dll（0xc0000602）；FDD exe + dll → 正常。
   - 本机安装有 `Microsoft.WindowsAppRuntime.2` 2.3.1.0（与项目 PackageReference 同版本），FDD 走共享注册运行时。
   - 版本实验：WASDK `1.8.260710003`、`2.3.2-experimentala` 仍复现崩溃；`1.7.260224002` 缺 `Microsoft.Windows.Storage.Pickers`（`FileOpenPicker`/`FileSavePicker` 不可用）。
   - 上游参照：[microsoft/WindowsAppSDK#6248](https://github.com/microsoft/WindowsAppSDK/issues/6248)（1.8+ unpackaged self-contained 崩溃，1.7 及更早不重现；Open）。
   - 当前处置：SCD 构建保留 `SelfContained=true` + `WindowsAppSDKSelfContained=true`（均在 `SCD-x64.pubxml`）；FDD 用 `WindowsAppSDKSelfContained=false`（`FDD-x64.pubxml`）；构建顺序无关——每次 `BuildArtifacts.ps1` 运行都会 Clean（Pitfall 19 的崩溃仅在共享 obj/bin 中间产物时复现）。

20. **构建脚本 PowerShell 5.1 陷阱（BuildArtifacts.ps1）**:
   - **native stderr + `$ErrorActionPreference=Stop`**：`& exe 2>&1 | Out-Null` 中 stderr 行会抛 `RemoteException` 终止脚本。调用 7za 等外部命令时**不要合并 stderr**（`-bso0 -bsp0` 静默即可）。
   - **裸 token 通配符解析**：`-x!*.pdb` 作为裸参数会被 PS 5.1 做通配符解析导致参数错位（7za 归档名被当作输入文件）。排除模式须**经变量传递**（`$excludePdb = '-x!*.pdb'`）。
   - **`Expand-Archive` 仅接受 `.zip` 扩展名**（不校验内容）：nupkg 解包前需复制改名 `.zip`。
   - **`DefaultItemExcludes` vs `ItemGroup Remove`（Directory.Build.props）**：SDK 默认 Compile glob 在 targets 阶段（props 之后）添加，props 里的 `Remove` 不作用于后加项（CS0579 特性重复）。须用 `DefaultItemExcludes`（props 中设置，控制默认 glob 排除）。（历史条目：多模式 obj\fdd\obj\scd 时代必需；Profile 化后单 obj 由 SDK 默认排除覆盖，该配置已随 PDB 改动移除——见 Pitfall 24。）
   - **makepri dump 阻塞**：`makepri dump` 在 stdout 经管道时等待 stdin（覆盖确认/EOF）。须 `Start-Process` + `-RedirectStandardInput`（空文件）+ `WaitForExit(60s)` 超时 Kill + 以输出文件存在作成功判据。（历史条目：`param` 与 `$script:` 同名冲突、`Start-Process` ExitCode 空、子进程成功标记文件等坑已随并行子进程模式移除——2026-08 并行机制不再使用。）

21. **构建脚本 PublishProfile 化（BuildArtifacts.ps1 + Properties/PublishProfiles）**: 发布参数（`SelfContained`/`WindowsAppSDKSelfContained`/`PublishTrimmed`/`Platform`/`RuntimeIdentifier`/`Configuration`）**唯一来源是 `Properties/PublishProfiles/{FDD|SCD}-x64.pubxml`**，脚本/CI 只传 `-p:PublishProfile=…` + `-p:PublishDir=…` + `-p:Version=…`：
   - **Profile 与构建布线正交**：`BaseIntermediateOutputPath`/`BaseOutputPath` 不再需要按模式注入；脚本每次运行自带 Clean，单模式本机构建（FDD、SCD 分两次跑）天然无中间产物污染。
   - **本地不支持多实例并发**：同一时间只允许一个 `BuildArtifacts.ps1` 实例（默认 obj/bin 无隔离）；并行由 GitHub Actions matrix（`dotnet-ci.yml`/`dotnet-manual.yml`/`dotnet-tag.yml` 的 `[FDD, SCD]` 双 job，各自独立 VM）承担。
   - **Restore 单次**：`--locked-mode` 单 obj，不按模式区分。
   - **Worker 独立验证目录**：Worker 先 publish 到 `build/Worker-{mode}/`（不进 zip，仅验证其 Profile 可用），Main publish 后经 csproj `CopyWorkerBuildOutputToPublish` 把 Worker 的 **Build 输出**（非 Publish 输出）注入 Main 输出——Main SCD 的 .NET/WASDK 运行时由 Main 提供，Worker 共享同目录运行时。

22. **Worker copy target 路径必须含 RID 段（WTGWizard.Main.csproj）**: `CopyWorkerBuildOutput*` 的 `WorkerOutputPath` 为 `..\WTGWizard.Worker\bin\$(Platform)\$(Configuration)\$(TargetFramework)\$(RuntimeIdentifier)`（`RuntimeIdentifier` 非空时追加）。**缺 RID 段时 target 静默复制零文件**——早期版本仅因"Worker 先 publish 到同一目录 + `-o` 不清理残留"而表面上"工作"，Profile 化后该隐式链路已移除。另外：`dotnet publish -o`/`-p:PublishDir` **不会清理目标目录**；依赖"发布目录可能残留旧文件"的行为视为 bug。

23. **WASDK 子包白名单锁集（Main.csproj，产物瘦身）**: metapackage `Microsoft.WindowsAppSDK` 2.4.0 会拖入 AI/ML/Search/Widgets 组件（onnxruntime.dll 20.7MB + DirectML.dll 17.8MB + Search/Widgets/AI 投影，合计 ~45MB 解压），Main 源码零使用——`Main.csproj` 以 **11 引用锁集**替代裸 metapackage（SCD 解压 250→182MB / 497 文件）：
    - **结构**：metapackage 锚（`ExcludeAssets="all"`，仅参与版本解析）+ 白名单 5 正常引用（`WinUI 2.3.6`/`Foundation 2.3.9`/`Base 2.0.4`/`Runtime 2.4.0`/`DWrite 2.1.0`）+ 黑名单 5（`AI 2.4.4`/`ML 2.1.74`/`Search 2.4.4`/`Widgets 2.0.5`/`Windows.AI.MachineLearning 2.1.74`，均 `ExcludeAssets="all"` 静默其 buildTransitive 复制）。
    - **坑 a（锚必需）**：CommunityToolkit 三包传递要求 `Microsoft.WindowsAppSDK >= 1.6.250108002`——裸白名单（无锚）会让 1.x 旧 metapackage 浮上，其内嵌 WinUI targets 与 `microsoft.windowsappsdk.winui` 2.3.6 重复导入（MSB4011 + MSIX `CustomBeforeMicrosoftCommonTargets` 报错）。
    - **坑 b（MinVersion 检查）**：`Microsoft.Windows.AI.MachineLearning` 2.1.74 targets 强制 `SupportedOSPlatformVersion >= 18362`（项目 min 17763）——必须黑名单，不能靠"不引用"绕过（传递依赖仍在图中）。
    - **坑 c（Pickers 归属）**：`Microsoft.Windows.Storage.Pickers`（FileOpenPicker/FileSavePicker）的 winmd 在 **Foundation** 包——白名单勿移除 Foundation。
    - **副产物（desktop pack 顺带消失）**：WindowsDesktop.App runtime pack（WPF/WinForms 全家桶 ~50MB）的注入与 **AI/ML 资产导入在 build 期耦合**（.NET 10 windows TFM 框架 `Microsoft.Windows.SDK.NET.Ref.Windows`；evaluation 期无 desktop FrameworkReference，WASDK 全部包 targets 零命中，常规 `FrameworkReference Remove` 无处下手）——AI/ML 黑名单静默后 runtimeconfig `includedFrameworks`/deps.json/产物三处 desktop 全消失。产物仅剩 16KB `WindowsBase.dll`（trim 空壳，deps.json 一致，无害）。
    - **更新流程（版本互不相同，不能只改一个号）**：① 临时把锚改普通引用（或仓库外探针工程）restore，读 lock.json 各子包 resolved 版本；② 11 处版本号全部对齐，锚恢复 `ExcludeAssets="all"`；③ diff lock.json——metapackage 新增未知子包逐一判定（用到→白名单，没用→黑名单）；④ `dotnet restore` 更新 lock → 脚本 `--locked-mode` 全流程；⑤ 产物验证：probe publish diff 无 onnxruntime/DirectML/Search/Widgets/AI、白名单核心在位、**runtimeconfig includedFrameworks 仅 Microsoft.NETCore.App**（desktop pack 回流＝注入机制变了，停下排查）、FileOpenPicker/image verify/启动冒烟 + FDD 同过。
    - **逃生门**：一行回退裸 metapackage → 回到 ~250MB 状态，无功能代价。
    - 验证基线：SCD 启动冒烟 ✅ / Worker 命令分发 ✅ / FDD 79 文件 ✅。

24. **Release 无 PDB（源头消除）**: `src/Directory.Build.props` 对 Release 全仓设 `DebugType=None` + `DebugSymbols=false`——pubxml 属性非全局、ProjectReference 工程不可见，PDB 须在 props 层源头消除（BuildArtifacts.ps1 的 7za `-x!*.pdb`/ZipFile 回退排除降级为冗余保险）。**代价**：Release 构建无符号，崩溃栈无行号；排查需本地临时带符号重建（移除该块或改 `DebugType=embedded`）。同文件历史上的 `PathMap`/`DefaultItemExcludes`（多模式 obj 排除）已随本次重写移除——前者仅影响内嵌路径确定性，后者在 Profile 化单 obj 后由 SDK 默认排除覆盖（见 Pitfall 20）。
