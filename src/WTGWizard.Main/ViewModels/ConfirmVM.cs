using System.ComponentModel;
using System.IO;
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
    [ObservableProperty] public partial string ImageDescription { get; set; } = "-";
    [ObservableProperty] public partial string DisplayDescription { get; set; } = "-";
    [ObservableProperty] public partial string ImageIndex { get; set; } = "-";
    [ObservableProperty] public partial string ImageVersion { get; set; } = "-";
    [ObservableProperty] public partial string FeatureUpdate { get; set; } = "-";
    [ObservableProperty] public partial string Architecture { get; set; } = "-";
    [ObservableProperty] public partial string BuildNumber { get; set; } = "-";
    [ObservableProperty] public partial string ExpandedSizeDisplay { get; set; } = "-";
    [ObservableProperty] public partial string DateCreated { get; set; } = "-";
    [ObservableProperty] public partial string ImageFileName { get; set; } = "-";

    // ═══ 部署方式组 ═══

    [ObservableProperty] public partial string MethodType { get; set; } = "-";
    [ObservableProperty] public partial string TargetDisk { get; set; } = "-";
    [ObservableProperty] public partial string TargetPartition { get; set; } = "-";
    [ObservableProperty] public partial Visibility PartitionConfigVisible { get; set; } = Visibility.Collapsed;
    [ObservableProperty] public partial Visibility TargetPartitionVisible { get; set; } = Visibility.Collapsed;

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
    [ObservableProperty] public partial bool ForceUnsignedDisplay { get; set; }
    [ObservableProperty] public partial bool CleanAnsFileDisplay { get; set; }

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
        RefreshOptionsGroup();
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
        if (e.PropertyName is nameof(DeployMethodVM.IsCleanInstall)
            or nameof(DeployMethodVM.SelectedDisk)
            or nameof(DeployMethodVM.SelectedPartition))
        {
            RefreshMethodGroup();
        }
    }

    private void OnOptionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshOptionsGroup();
    }

    private void OnAdvancedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AdvancedOptionsVM.DriverEnabled)
            or nameof(AdvancedOptionsVM.DriverPath)
            or nameof(AdvancedOptionsVM.ForceUnsigned)
            or nameof(AdvancedOptionsVM.CustomAnsFileEnabled)
            or nameof(AdvancedOptionsVM.AnsFilePath)
            or nameof(AdvancedOptionsVM.CleanImageAnsFile))
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
        ImageName = info?.Name ?? "-";
        ImageDescription = info?.Description ?? "-";
        DisplayDescription = info?.DisplayDescription ?? "-";
        ImageIndex = info?.Index.ToString() ?? "-";
        ImageVersion = info is not null ? $"{info.Sku} {info.FeatureVersion}" : "-";
        FeatureUpdate = info?.FeatureVersion ?? "-";
        Architecture = info?.Architecture ?? "-";
        BuildNumber = info?.BuildNumber ?? "-";
        ExpandedSizeDisplay = info is not null ? $"{info.ExpandedSizeGB:F1} GiB" : "-";
        DateCreated = info?.DateCreated ?? "-";
        ImageFileName = string.IsNullOrEmpty(_image.FilePath) ? "-" : Path.GetFileName(_image.FilePath);
    }

    private void RefreshMethodGroup()
    {
        MethodType = _method.IsCleanInstall
            ? Lang.Page_WizStep_Confirm_MethodType_Clean
            : Lang.Page_WizStep_Confirm_MethodType_Partition;

        var disk = _method.SelectedDisk;
        TargetDisk = disk is null ? "-" : $"Disk {disk.Index} - {disk.Model} ({disk.SizeBytes / (1024.0 * 1024 * 1024):F2} GiB)";

        var part = _method.SelectedPartition;
        TargetPartition = part is null ? "-" : part.DisplayName;

        PartitionConfigVisible = _method.IsCleanInstall ? Visibility.Visible : Visibility.Collapsed;
        TargetPartitionVisible = _method.IsCleanInstall ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RefreshOptionsGroup()
    {
        EspSize = $"{_method.EfiPartSize} MB";

        var osSize = _method.OsDriveSize;
        OsSize = osSize > 0 ? $"{osSize:F1} GiB" : Lang.Page_WizStep_Confirm_OsSize_Auto;

        OsLabel = string.IsNullOrEmpty(_method.OsDriveLabel) ? "OS" : _method.OsDriveLabel;

        ReservedVolVisible = _method.EnableReservedVol ? Visibility.Visible : Visibility.Collapsed;
        var reservedSize = _method.ReservedDriveSize;
        ReservedVolSize = reservedSize > 0 ? $"{reservedSize:F1} GiB" : "-";
        ReservedVolFs = _method.ReservedDriveFs?.ToUpper() ?? "-";
        ReservedVolLabel = _method.ReservedDriveLabel ?? "-";
    }

    private void RefreshAdvancedGroup()
    {
        DriverPathDisplay = _advanced.DriverEnabled ? _advanced.DriverPath ?? "-" : "-";
        AnsFilePathDisplay = _advanced.CustomAnsFileEnabled ? _advanced.AnsFilePath ?? "-" : "-";
        ForceUnsignedDisplay = _advanced.DriverEnabled && _advanced.ForceUnsigned;
        CleanAnsFileDisplay = _advanced.CustomAnsFileEnabled && _advanced.CleanImageAnsFile;
    }
}
