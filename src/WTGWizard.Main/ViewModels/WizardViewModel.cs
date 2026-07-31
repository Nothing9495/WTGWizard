using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using WTGWizard.Main.DeploymentCore.Builders;
using WTGWizard.Main.DeploymentCore.Models;
using WTGWizard.Main.DeploymentCore.Orchestrator;
using WTGWizard.Main.DeploymentCore.Steps;
using WTGWizard.Main.DeploymentCore.Worker;
using WTGWizard.Messages;
using WTGWizard.Shared.Services.DiskServices;
using WTGWizard.Shared.Services.Logger;

namespace WTGWizard.ViewModels;

/// <summary>
/// Wizard 协调器 VM — 步骤导航 + 全局状态容器。
/// </summary>
public sealed partial class WizardViewModel : ObservableObject
{
    // ═══ 步骤导航 ═══

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(CanGoForward))]
    [NotifyPropertyChangedFor(nameof(IsCurrentStepValid))]
    public partial int CurrentStep { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(CanGoForward))]
    public partial bool IsDeploying { get; set; }
    [ObservableProperty] public partial string CurrentStepTitle { get; set; } = string.Empty;

    public bool CanGoBack => CurrentStep > 0 && !IsDeploying;
    public bool CanGoForward => CurrentStep < MaxStep && IsCurrentStepValid;
    public int MaxStep => 4; // 0-4, 共 5 步
    public int TotalSteps => 5;

    // ═══ 状态子对象 ═══

    public ImageConfigVM Image { get; } = new();
    public DeployOptionsVM Options { get; } = new();
    public DeployMethodVM Method { get; }
    public AdvancedOptionsVM Advanced { get; } = new();

    // ═══ 服务 ═══

    private readonly IDriveLetterService _driveLetterService;
    private readonly ILoggerService _logger;

    // ═══ 部署状态 ═══

    public DeploymentOrchestrator? Orchestrator { get; private set; }

    // ═══ 构造函数 ═══

    public WizardViewModel(IDriveLetterService driveLetterService, ILoggerService logger)
    {
        _driveLetterService = driveLetterService;
        _logger = logger;

        Method = new DeployMethodVM();
        Image.PropertyChanged += OnSubPropertyChanged;
        Image.PropertyChanged += OnImagePropertyChanged;
        Method.PropertyChanged += OnSubPropertyChanged;
        Advanced.PropertyChanged += OnSubPropertyChanged;
        Advanced.UpdateAnsFileIndicator(Image);
    }

    private void OnSubPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "IsValid")
        {
            OnPropertyChanged(nameof(IsCurrentStepValid));
            OnPropertyChanged(nameof(CanGoForward));
        }
    }

    private void OnImagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ImageConfigVM.FilePath)
            or nameof(ImageConfigVM.AnsFileFoundPaths)
            or nameof(ImageConfigVM.HasImage)
            or nameof(ImageConfigVM.SelectedIndex))
        {
            Advanced.UpdateAnsFileIndicator(Image);
        }
    }

    // ═══ 派生属性 ═══

    public bool IsCurrentStepValid => CurrentStep switch
    {
        0 => Image.IsValid,
        1 => Method.IsValid,
        2 => true,
        3 => Advanced.IsValid,
        4 => true,
        _ => false
    };

    // ═══ 命令 ═══

    [RelayCommand]
    private void GoBack()
    {
        if (CanGoBack)
            CurrentStep--;
    }

    [RelayCommand]
    private void GoForward()
    {
        if (CanGoForward)
            CurrentStep++;
    }

    [RelayCommand]
    private void Reset()
    {
        CurrentStep = 0;
    }

    /// <summary>
    /// 取走并清空当前 Orchestrator（由 TaskPage 在部署开始时调用，防止重复部署）。
    /// </summary>
    public DeploymentOrchestrator? TakeOrchestrator()
    {
        var orchestrator = Orchestrator;
        Orchestrator = null;
        return orchestrator;
    }

    [RelayCommand]
    private void StartDeploy()
    {
        var config = BuildDeploymentConfig();

        var pipeline = new DeploymentPipeline()
            .AddStep<PartitionStep>()
            .AddStep<ExtractStep>()
            .AddStep<DriverStep>()
            .AddStep<ImportAnsFileStep>()
            .AddStep<ApplyWtgStep>()
            .AddStep<BcdbootStep>()
            .AddStep<CleanupStep>();
        var worker = new WorkerProcess(_logger);
        var commands = new WorkerCommandFactory();
        var tempFiles = new TempFileManager();

        Orchestrator = new DeploymentOrchestrator(pipeline, config, _driveLetterService, _logger,
            worker, commands, tempFiles);
        IsDeploying = true;
        WeakReferenceMessenger.Default.Send(new NavigateToPageMessage("TaskPage"));
    }

    private DeploymentConfig BuildDeploymentConfig()
    {
        var imageInfo = Image.ImageInfo;
        var disk = Method.SelectedDisk;

        if (disk is null || imageInfo is null)
            throw new InvalidOperationException("Disk or image not selected");

        char espDriveLetter;
        char osDriveLetter;

        if (Method.IsCleanInstall)
        {
            var (esp, os) = _driveLetterService.ReserveForCleanInstall();
            espDriveLetter = esp;
            osDriveLetter = os;
        }
        else
        {
            espDriveLetter = _driveLetterService.ReserveForPartitionInstall();
            osDriveLetter = Method.SelectedPartition?.DriveLetter is { Length: >= 1 } letter
                ? letter[0]
                : throw new InvalidOperationException("No partition selected for partition install");
        }

        uint espVolumeId = 0;
        uint osDriveVolumeId = 0;

        if (Method.IsCleanInstall)
        {
            espVolumeId = DeploymentConstants.CleanInstallEspPartNum;
            osDriveVolumeId = DeploymentConstants.CleanInstallOsPartNum;
        }
        else
        {
            espVolumeId = Method.SelectedDisk?.EspPartitionNumber ?? 0;
            osDriveVolumeId = Method.SelectedPartition?.PartitionNumber ?? 0;
        }

        return new DeploymentConfig
        {
            // ── 映像 ──
            SrcImageFile = Image.FilePath,
            ImageSelectedIndex = Image.WimIndex,
            ImageWindowsArch = imageInfo.Architecture,
            ImageWinBuildNum = imageInfo.BuildNumber,
            ImageExpandedSize = imageInfo.ExpandedSizeGB,
            UseDismToDeploy = Options.UseDismToDeploy,

            // ── 磁盘 ──
            DiskSelectedId = (int)disk.Index,
            DiskSizeBytes = disk.SizeBytes,
            IsCleanInstall = Method.IsCleanInstall,
            EnableReservedVol = Method.EnableReservedVol,

            // ── 分区（Clean 模式）──
            EfiPartSize = Method.EfiPartSize,
            OsDriveSize = Method.OsDriveSize,
            OsDriveLabel = Method.OsDriveLabel,
            ReservedDriveLabel = Method.ReservedDriveLabel,
            ReservedDriveFs = Method.ReservedDriveFs,
            NoDefaultDriveLetter = Options.NoDefaultDriveLetter,
            AutoRemoveOsDriveLetter = Options.AutoRemoveOsDriveLetter,
            MaxOsDriveSize = Method.MaxOsDriveSize,

            // ── 分区（Partition Install 模式）──
            EspVolumeId = espVolumeId,
            OsDriveVolumeId = osDriveVolumeId,
            SelectedPartitionDriveLetter = Method.SelectedPartition?.DriveLetter,

            // ── 盘符 ──
            EspDriveLetter = espDriveLetter,
            OsDriveLetter = osDriveLetter,

            // ── 驱动集成 ──
            DriverIntegrationEnabled = Advanced.DriverEnabled,
            DriversDirectoryPath = Advanced.DriverPath,
            ForceUnsignedDriver = Advanced.ForceUnsigned,

            // ── 应答文件 ──
            CustomAnsFileEnabled = Advanced.CustomAnsFileEnabled,
            AnsFilePath = Advanced.AnsFilePath,
            CleanImageAnsFile = Advanced.CleanImageAnsFile,

            // ── WTG 设置 ──
            HideLocalDisks = Options.HideLocalDisks,
            PreventDeviceEncryption = Options.PreventDeviceEncryption,

            // ── BCDBoot ──
            EnableBootEx = Advanced.EnableBootEx,
            EnableBootVerbose = Advanced.EnableBootVerbose,
        };
    }

    // ═══ 步骤指示器 ═══

    /// <summary>
    /// 更新当前步骤标题（由 WizardHost 调用）。
    /// </summary>
    public void UpdateStepTitle(string[] stepResourceKeys)
    {
        if (CurrentStep >= 0 && CurrentStep < stepResourceKeys.Length)
        {
            CurrentStepTitle = Localization.GetString(stepResourceKeys[CurrentStep]);
        }
    }

}
