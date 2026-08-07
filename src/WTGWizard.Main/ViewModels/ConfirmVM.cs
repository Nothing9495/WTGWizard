using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using WTGWizard.Main.Language;

namespace WTGWizard.ViewModels;

/// <summary>
/// Step5 确认部署页展示层 VM — 全部配置摘要派生属性。
/// </summary>
public sealed partial class ConfirmVM : ObservableObject
{
    private readonly ImageConfigVM _image;
    private readonly DeployMethodVM _method;
    private readonly DeployOptionsVM _options;
    private readonly AdvancedOptionsVM _advanced;
    private readonly WizardViewModel _wizard;

    // ═══ 映像组 ═══

    [ObservableProperty] public partial string ImageName { get; set; } = "-";
    [ObservableProperty] public partial string ImageIndex { get; set; } = "-";
    [ObservableProperty] public partial string ImageSku { get; set; } = "-";
    [ObservableProperty] public partial string ImageSkuVersion { get; set; } = "-";
    [ObservableProperty] public partial string FeatureVersion { get; set; } = "-";
    [ObservableProperty] public partial string Architecture { get; set; } = "-";
    [ObservableProperty] public partial string BuildNumber { get; set; } = "-";

    // ═══ 部署方式组 ═══

    [ObservableProperty] public partial string MethodType { get; set; } = "-";
    [ObservableProperty] public partial string TargetDisk { get; set; } = "-";
    [ObservableProperty] public partial string TargetPartition { get; set; } = "-";

    // ═══ 分区配置组 ═══

    [ObservableProperty] public partial string EspSize { get; set; } = "300 MB";
    [ObservableProperty] public partial string OsSize { get; set; } = "-";
    [ObservableProperty] public partial string OsLabel { get; set; } = "OS";

    // ═══ 保留卷组 ═══

    [ObservableProperty] public partial Visibility ReservedVolVisible { get; set; } = Visibility.Collapsed;
    [ObservableProperty] public partial string ReservedVolSize { get; set; } = "-";
    [ObservableProperty] public partial string ReservedVolFs { get; set; } = "-";
    [ObservableProperty] public partial string ReservedVolLabel { get; set; } = "-";

    // ═══ 高级设置组 ═══

    [ObservableProperty] public partial string DriverPathDisplay { get; set; } = "-";
    [ObservableProperty] public partial string AnsFilePathDisplay { get; set; } = "-";

    // ═══ 行状态文本（Deploy Options / Advanced Options 卡片）═══

    [ObservableProperty] public partial string HideLocalDisksStateText { get; set; } = string.Empty;
    [ObservableProperty] public partial string PreventEncryptionStateText { get; set; } = string.Empty;
    [ObservableProperty] public partial string NoDefaultDriveLetterStateText { get; set; } = string.Empty;
    [ObservableProperty] public partial string AutoRemoveOsLetterStateText { get; set; } = string.Empty;
    [ObservableProperty] public partial string UseDismToDeployStateText { get; set; } = string.Empty;
    [ObservableProperty] public partial string DriversIntegrationStateText { get; set; } = string.Empty;
    [ObservableProperty] public partial string AllowUnsignedStateText { get; set; } = string.Empty;
    [ObservableProperty] public partial string ImportAnsFileStateText { get; set; } = string.Empty;
    [ObservableProperty] public partial string CleanAnsFileStateText { get; set; } = string.Empty;
    [ObservableProperty] public partial string BootVerboseStateText { get; set; } = string.Empty;
    [ObservableProperty] public partial string EnableBootExStateText { get; set; } = string.Empty;

    // ═══ 状态 ═══

    [ObservableProperty] public partial bool IsStartDeployEnabled { get; set; } = true;

    // ═══ 构造函数 ═══

    public ConfirmVM(WizardViewModel wizard)
    {
        _wizard = wizard;
        _image = wizard.Image;
        _method = wizard.Method;
        _options = wizard.Options;
        _advanced = wizard.Advanced;

        _image.PropertyChanged += OnImageChanged;
        _method.PropertyChanged += OnMethodChanged;
        _options.PropertyChanged += OnOptionsChanged;
        _advanced.PropertyChanged += OnAdvancedChanged;
        _wizard.PropertyChanged += OnWizardChanged;

        RefreshImageGroup();
        RefreshMethodGroup();
        RefreshPartitionGroup();
        RefreshOptionsStateText();
        RefreshAdvancedGroup();
        IsStartDeployEnabled = !_wizard.IsDeploying;
    }

    // ═══ 变更监听 ═══

    private void OnImageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ImageConfigVM.ImageInfo)
            or nameof(ImageConfigVM.FilePath))
        {
            RefreshImageGroup();
        }
    }

    private void OnMethodChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DeployMethodVM.IsCleanInstall):
            case nameof(DeployMethodVM.SelectedDisk):
            case nameof(DeployMethodVM.SelectedPartition):
                RefreshMethodGroup();
                break;
            case nameof(DeployMethodVM.EfiPartSize):
            case nameof(DeployMethodVM.OsDriveSize):
            case nameof(DeployMethodVM.OsDriveLabel):
            case nameof(DeployMethodVM.EnableReservedVol):
            case nameof(DeployMethodVM.ReservedVolSize):
            case nameof(DeployMethodVM.ReservedVolLabel):
            case nameof(DeployMethodVM.ReservedVolFs):
                RefreshPartitionGroup();
                break;
        }
    }

    private void OnOptionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshOptionsStateText();
    }

    private void OnAdvancedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AdvancedOptionsVM.DriverEnabled)
            or nameof(AdvancedOptionsVM.DriverPath)
            or nameof(AdvancedOptionsVM.ForceUnsigned)
            or nameof(AdvancedOptionsVM.CustomAnsFileEnabled)
            or nameof(AdvancedOptionsVM.AnsFilePath)
            or nameof(AdvancedOptionsVM.CleanImageAnsFile)
            or nameof(AdvancedOptionsVM.EnableBootVerbose)
            or nameof(AdvancedOptionsVM.EnableBootEx))
        {
            RefreshAdvancedGroup();
        }
    }

    private void OnWizardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WizardViewModel.IsDeploying))
            IsStartDeployEnabled = !_wizard.IsDeploying;
    }

    // ═══ 分组刷新 ═══

    private void RefreshImageGroup()
    {
        var info = _image.ImageInfo;
        ImageName = D(info?.Name);
        ImageIndex = info?.Index.ToString() ?? "-";
        ImageSku = D(info?.Sku);
        ImageSkuVersion = info is not null ? $"{info.Sku} {info.FeatureVersion}" : "-";
        FeatureVersion = D(info?.FeatureVersion);
        Architecture = D(info?.Architecture);
        BuildNumber = D(info?.BuildNumber);
    }

    private void RefreshMethodGroup()
    {
        MethodType = _method.IsCleanInstall
            ? Lang.Page_WizStep_Confirm_MethodType_Clean
            : Lang.Page_WizStep_Confirm_MethodType_Partition;

        TargetDisk = _method.SelectedDisk?.DisplayName ?? "-";
        TargetPartition = _method.SelectedPartition?.DisplayName ?? "-";
    }

    private void RefreshPartitionGroup()
    {
        EspSize = $"{_method.EfiPartSize} MB";

        var osSize = _method.OsDriveSize;
        OsSize = osSize > 0 ? $"{osSize:F1} GiB" : Lang.Page_WizStep_Confirm_OsSize_Auto;

        OsLabel = string.IsNullOrEmpty(_method.OsDriveLabel) ? "OS" : _method.OsDriveLabel;

        ReservedVolVisible = _method.EnableReservedVol ? Visibility.Visible : Visibility.Collapsed;
        ReservedVolSize = _method.ReservedVolSize > 0 ? $"{_method.ReservedVolSize:F1} GiB" : "-";
        ReservedVolFs = _method.ReservedVolFs?.ToUpper() ?? "-";
        ReservedVolLabel = D(_method.ReservedVolLabel);
    }

    private void RefreshOptionsStateText()
    {
        HideLocalDisksStateText = StateText(_options.HideLocalDisks);
        PreventEncryptionStateText = StateText(_options.PreventDeviceEncryption);
        NoDefaultDriveLetterStateText = StateText(_options.NoDefaultDriveLetter);
        AutoRemoveOsLetterStateText = StateText(_options.AutoRemoveOsDriveLetter);
        UseDismToDeployStateText = StateText(_options.UseDismToDeploy);
    }

    private void RefreshAdvancedGroup()
    {
        DriverPathDisplay = _advanced.DriverEnabled ? D(_advanced.DriverPath) : "-";
        AnsFilePathDisplay = _advanced.CustomAnsFileEnabled ? D(_advanced.AnsFilePath) : "-";

        DriversIntegrationStateText = StateText(_advanced.DriverEnabled);
        AllowUnsignedStateText = StateText(_advanced.DriverEnabled && _advanced.ForceUnsigned);
        ImportAnsFileStateText = StateText(_advanced.CustomAnsFileEnabled);
        CleanAnsFileStateText = StateText(_advanced.CustomAnsFileEnabled && _advanced.CleanImageAnsFile);
        BootVerboseStateText = StateText(_advanced.EnableBootVerbose);
        EnableBootExStateText = StateText(_advanced.EnableBootEx);
    }

    // ═══ Helper ═══

    private static string D(string? value) => string.IsNullOrEmpty(value) ? "-" : value;

    private static string StateText(bool enabled) =>
        enabled ? Lang.Page_WizStep_Confirm_InfoCard_Enabled : Lang.Page_WizStep_Confirm_InfoCard_Disabled;
}
