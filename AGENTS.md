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
├── WTGWizard.Shared.Services/ # 核心服务：磁盘、WIM、日志
├── WTGWizard.Shared.Common/   # Named Pipe IPC 协议
└── WTGWizard.Worker/          # Worker 子进程 (Exe)
```

### Dependency Graph

```
Main ──┬──> Shared.Services
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
| 1 | `ImageConfigPage` | WIM/ESD file selection + index picker |
| 2 | `DeployMethodPage` | Disk selection, clean/partition install |
| 3 | `DeployOptionsPage` | WTG settings (hide disks, drive letter, etc.) |
| 4 | `AdvancedOptionsPage` | Driver integration, answer files, boot options |
| 5 | `ConfirmPage` | Summary + "Start Deployment" button |

**ViewModels**: `WizardViewModel` is the orchestrator, composing 5 sub-VMs (`ImageConfigVM`, `DeployMethodVM`, `DeployOptionsVM`, `AdvancedOptionsVM`, `ConfirmVM`).

### WTGWizard.Shared.Services — Service Layer

| Service | Purpose |
|---------|---------|
| `DiskIOService` | Disk enumeration (SetupAPI), partition queries, safety checks, device monitoring |
| `DriveLetterService` | Two-phase drive letter assignment |
| `WimService` | WIM operations (ManagedWimLib): enumerate indices, extract, verify |
| `LoggerService` | Serilog-based logging: Debug output + file output |

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
| `extract` | ⚠️ Stub | WIM extraction (NotImplementedException) |

### WTGWizard.Main.Language — Localization

- `Lang.resx` — English (default)
- `Lang.zh-CN.resx` — Chinese (Simplified)
- `Lang.Designer.cs` — Auto-generated strongly-typed accessor
- All UI strings are localized

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
WeakReferenceMessenger.Default.Send(new NavigateToPageMessage("wizard"));

// Receive (in MainWindow)
WeakReferenceMessenger.Default.Register<NavigateToPageMessage>(this, (r, m) => { ... });
```

### IPC Protocol

Newline-delimited JSON over Named Pipes:

```json
{"type":"task_progress","data":{"percent":50,"message":"Extracting..."}}
```

- AOT-compatible (hand-rolled JSON, no System.Text.Json source generators)
- Pipe naming: `WTGWizardWorker_{PID}`
- 15-second connection timeout

---

## Conventions

### Resource Key Naming

Resource keys use `.` separator in `.resx` files:

```
Page.WizStep.Confirm.Title
Page.WizStep.Confirm.MethodType.Clean
```

C# property names use `_` separator (auto-converted):

```csharp
Lang.Page_WizStep_Confirm_Title
```

XAML bindings use the C# property name:

```xml
<TextBlock Text="{x:Bind lang:Lang.Page_WizStep_Confirm_Title}" />
```

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
| Shared.Services | `WTGWizard.Shared.Services` |
| Shared.Common | `WTGWizard.Shared.Common` |
| Main.Language | `WTGWizard.Main.Language` |
| Worker | `WTGWizard.Worker` |

### Unattend → AnsFile

All "Unattend" references have been renamed to "AnsFile" in the current codebase:

| Old | New |
|-----|-----|
| `UnattendPath` | `AnsFilePath` |
| `CustomUnattendEnabled` | `CustomAnsFileEnabled` |
| `CleanImageUnattend` | `CleanImageAnsFile` |
| `HasUnattend` | `HasUnattend` (kept for compatibility) |

### Logging

```csharp
// Via ILoggerService
_logger.Debug("DiskService", "GetPartitions for disk {Index}", diskIndex);
_logger.Error("WimService", "Extract failed: {Error}", ex.Message);

// Category is the first parameter, message template uses {Placeholder} syntax
```

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

1. Create `Commands/{Name}Command.cs` with static `Run(string[] args, PipeWriter pipe)` method
2. Register in `Program.cs` command dispatch switch
3. Parse args via `CommandArgs.GetArg(args, "--name")`
4. Report status via `pipe.WriteRunning()`, `pipe.WriteCompleted()`, `pipe.WriteFailed()`

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
| Disk Services | ✅ Complete | `Shared.Services/DiskServices/` |
| WIM Service | ✅ Complete | `Shared.Services/WimService/` |
| IPC Protocol | ✅ Complete | `Shared.Common/` |
| Worker Commands | ✅ 4/5 Complete | `Worker/Commands/` |
| DiskIOWriter | ⚠️ Stub | `Shared.Services/DiskServices/DiskIOService/DiskIOWriter.cs` |
| ExtractCommand | ⚠️ Stub | `Worker/Commands/ExtractCommand.cs` |
| TaskPage | ⚠️ Stub | `Pages/TaskPage.xaml` |
| SettingsPage | ⚠️ Stub | `Pages/SettingsPage.xaml` |
| DeploymentOrchestrator | ❌ Not started | TODO in `WizardViewModel.StartDeploy()` |
| Deployment Steps | ❌ Not started | 7 steps: Partition, Extract, Driver, ImportUnattend, ApplyWtg, Bcdboot, Cleanup |
| WimService.Cleanup() | ⚠️ Disabled | Commented out in `App.OnMainWindowClosed` (Access Violation) |

---

## Pitfalls & Gotchas

1. **XAML Compiler Error WMC9999**: Pre-existing Windows App SDK issue, not caused by code changes. Ignore.

2. **File Locking**: If `dotnet build` fails with file lock errors, close the running WTGWizard.Main process first.

3. **TwoWay Binding Cascade**: `Partitions.Clear()` triggers ComboBox TwoWay binding to set `SelectedPartition = null`. Always save/restore selection before clearing collections.

4. **Tab Activation Double Refresh**: `OnNavigatedTo` + `OnTabActivated` both fire on tab switch. Avoid calling refresh methods in both.

5. **Resource Key Format**: `.resx` uses `.` separator, C# uses `_` separator, XAML uses `_` separator.

6. **Worker Process**: Worker is NOT a project reference. It's copied via MSBuild targets. Don't add it as a `<ProjectReference>`.

7. **Native Library**: `libwim-15.dll` must be in output directory. It's configured via `<CopyToOutputDirectory>` in Main's `.csproj`.

8. **AOT Compatibility**: Pipe protocol uses hand-rolled JSON (no System.Text.Json source generators). Don't introduce reflection-based serialization.

9. **Serilog.Sinks.Debug**: Must be restored via `dotnet restore` before building. If `FileNotFoundException`, run `dotnet restore WTGWizard.slnx`.

10. **Lang.Designer.cs**: Not auto-updated by `dotnet build`. Must be manually regenerated in VS (right-click → Run Custom Tool) or manually edit.
