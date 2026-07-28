using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WTGWizard.Shared.Services.DiskServices;

namespace WTGWizard.ViewModels;

/// <summary>
/// Step 2 状态：部署方式选择 + 磁盘/分区。
/// </summary>
public sealed partial class DeployMethod : ObservableObject
{
    // ═══ 磁盘/分区选择 ═══

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiskSelected))]
    [NotifyPropertyChangedFor(nameof(CanUsePartitionInstall))]
    [NotifyPropertyChangedFor(nameof(IsPartitionInstallSelected))]
    [NotifyPropertyChangedFor(nameof(IsPartitionConfigEnabled))]
    [NotifyPropertyChangedFor(nameof(CanToggleNoDefaultDriveLetter))]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial DiskBasicInfo? SelectedDisk { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial PartitionBasicInfo? SelectedPartition { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPartitionInstallSelected))]
    [NotifyPropertyChangedFor(nameof(IsPartitionConfigEnabled))]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial bool IsCleanInstall { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial string? DiskSafetyError { get; set; }

    // ═══ InfoBar 状态 ═══

    [ObservableProperty] public partial bool ShowDataSecurity { get; set; }
    [ObservableProperty] public partial bool ShowEspWarning { get; set; }
    [ObservableProperty] public partial bool ShowReservedSizeWarning { get; set; }
    [ObservableProperty] public partial bool ShowPartitionNoLetter { get; set; }

    // ═══ 集合 ═══

    [ObservableProperty] public partial ObservableCollection<DiskBasicInfo> Disks { get; set; } = [];
    [ObservableProperty] public partial ObservableCollection<PartitionBasicInfo> Partitions { get; set; } = [];

    // ═══ 计算属性 ═══

    public bool IsDiskSelected => SelectedDisk is not null;
    public bool CanUsePartitionInstall => IsDiskSelected && (SelectedDisk?.HasEspPartition ?? false);
    public bool IsPartitionInstallSelected => IsDiskSelected && !IsCleanInstall;
    public bool IsPartitionConfigEnabled => IsDiskSelected && IsCleanInstall;
    public bool CanToggleNoDefaultDriveLetter => IsDiskSelected && SelectedDisk?.MediaType != "Removable Media";

    public bool IsValid => IsDiskSelected
        && DiskSafetyError is null
        && (IsCleanInstall || (SelectedPartition is not null
            && !string.IsNullOrEmpty(SelectedPartition.DriveLetter)
            && (SelectedDisk?.HasEspPartition ?? false)));

    // ═══ 变更通知链 ═══

    partial void OnSelectedDiskChanged(DiskBasicInfo? oldValue, DiskBasicInfo? newValue)
    {
        // ESP 丢失时自动切回全新安装
        if (newValue?.HasEspPartition == false && !IsCleanInstall)
            IsCleanInstall = true;

        // 更新 InfoBar 状态
        ShowEspWarning = newValue is not null && !newValue.HasEspPartition;
        ShowDataSecurity = DiskSafetyError is not null;
    }

    partial void OnSelectedPartitionChanged(PartitionBasicInfo? value)
    {
        ShowPartitionNoLetter = !IsCleanInstall && value is { DriveLetter: null or "" };
    }

    partial void OnDiskSafetyErrorChanged(string? value)
    {
        ShowDataSecurity = value is not null;
    }

    partial void OnIsCleanInstallChanged(bool value)
    {
        // 切换模式时更新 InfoBar 状态
        if (SelectedPartition is not null)
            ShowPartitionNoLetter = !value && SelectedPartition.DriveLetter is null or "";
    }
}
