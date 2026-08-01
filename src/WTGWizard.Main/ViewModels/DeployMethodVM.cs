using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WTGWizard.Shared.Services.DiskServices;

namespace WTGWizard.ViewModels;

/// <summary>
/// Step 2 状态：部署方式选择 + 磁盘/分区。
/// </summary>
public sealed partial class DeployMethodVM : ObservableObject
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

    [ObservableProperty] public partial bool ShowEspWarning { get; set; }
    [ObservableProperty] public partial bool ShowPartitionNoLetter { get; set; }

    // ═══ 分区配置 ═══

    [ObservableProperty] public partial int EfiPartSize { get; set; } = 300;
    [ObservableProperty] public partial double MaxOsDriveSize { get; set; }
    [ObservableProperty] public partial double OsDriveSize { get; set; }
    [ObservableProperty] public partial string OsDriveLabel { get; set; } = "OS";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowReservedSizeWarning))]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    [NotifyPropertyChangedFor(nameof(ReservedSizeDisplay))]
    public partial double ReservedDriveSize { get; set; }
    [ObservableProperty] public partial string ReservedDriveLabel { get; set; } = "Reserved";
    [ObservableProperty] public partial string ReservedDriveFs { get; set; } = "ntfs";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowReservedSizeWarning))]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial bool EnableReservedVol { get; set; }

    // ═══ 集合 ═══

    [ObservableProperty] public partial ObservableCollection<DiskBasicInfo> Disks { get; set; } = [];
    [ObservableProperty] public partial ObservableCollection<PartitionBasicInfo> Partitions { get; set; } = [];

    // ═══ 计算属性 ═══

    public bool IsDiskSelected => SelectedDisk is not null;
    public bool CanUsePartitionInstall => IsDiskSelected && (SelectedDisk?.HasEspPartition ?? false);
    public bool IsPartitionInstallSelected => IsDiskSelected && !IsCleanInstall;
    public bool IsPartitionConfigEnabled => IsDiskSelected && IsCleanInstall;
    public bool CanToggleNoDefaultDriveLetter => IsDiskSelected && SelectedDisk?.MediaType != "Removable Media";

    /// <summary>保留分区有效：未启用或计算容量不为 0。</summary>
    public bool IsReservedValid => !EnableReservedVol || ReservedDriveSize > 0;

    /// <summary>保留分区大小警告。</summary>
    public bool ShowReservedSizeWarning => IsDiskSelected && IsCleanInstall
        && EnableReservedVol && ReservedDriveSize == 0;

    /// <summary>保留分区大小显示文本。</summary>
    public string ReservedSizeDisplay => ReservedDriveSize.ToString("F2");

    public bool IsValid => IsDiskSelected
        && DiskSafetyError is null
        && (!IsCleanInstall || IsReservedValid)
        && (IsCleanInstall || (SelectedPartition is not null
            && !string.IsNullOrEmpty(SelectedPartition.DriveLetter)
            && (SelectedDisk?.HasEspPartition ?? false)));

    // ═══ 分区配置管理 ═══

    /// <summary>重置分区配置（当 SelectedDisk 变化时调用）。</summary>
    public void ResetPartitionConfig()
    {
        EfiPartSize = 300;
        OsDriveLabel = "OS";
        EnableReservedVol = false;
        ReservedDriveSize = 0;
        ReservedDriveLabel = "Reserved";
        ReservedDriveFs = "ntfs";
    }

    /// <summary>更新最大 OS 分区容量。</summary>
    public void UpdateMaxOsDriveSize()
    {
        if (SelectedDisk is not { } disk) return;
        var diskGB = disk.SizeBytes / (1024.0 * 1024 * 1024);
        var espGB = EfiPartSize / 1024.0;
        var msrGB = 16.0 / 1024;
        MaxOsDriveSize = Math.Max(0, Math.Round(diskGB - espGB - msrGB, 2));
    }

    /// <summary>更新保留卷大小。</summary>
    public void UpdateReservedDriveSize()
    {
        if (!EnableReservedVol)
        {
            ReservedDriveSize = 0;
            return;
        }
        var osGB = OsDriveSize;
        ReservedDriveSize = Math.Max(0, Math.Round(MaxOsDriveSize - osGB, 2));
    }

    // ═══ 变更通知链 ═══

    partial void OnSelectedDiskChanged(DiskBasicInfo? oldValue, DiskBasicInfo? newValue)
    {
        // ESP 丢失时自动切回全新安装
        if (newValue?.HasEspPartition == false && !IsCleanInstall)
            IsCleanInstall = true;

        // 更新 InfoBar 状态
        ShowEspWarning = newValue is not null && !newValue.HasEspPartition;

        // 重置分区配置并更新容量
        if (newValue is not null)
        {
            ResetPartitionConfig();
            UpdateMaxOsDriveSize();
            OsDriveSize = MaxOsDriveSize;
        }
    }

    partial void OnSelectedPartitionChanged(PartitionBasicInfo? value)
    {
        ShowPartitionNoLetter = !IsCleanInstall && value is { DriveLetter: null or "" };
    }

    partial void OnDiskSafetyErrorChanged(string? value)
    {
        // DiskSafetyError 变化时的处理
    }

    partial void OnIsCleanInstallChanged(bool value)
    {
        // 切换模式时更新 InfoBar 状态
        if (SelectedPartition is not null)
            ShowPartitionNoLetter = !value && SelectedPartition.DriveLetter is null or "";
    }

    partial void OnEfiPartSizeChanged(int value)
    {
        var oldMax = MaxOsDriveSize;
        UpdateMaxOsDriveSize();
        var delta = oldMax - MaxOsDriveSize; // ESP 增加时为正值，减少时为负值

        if (EnableReservedVol && ReservedDriveSize > 0)
        {
            // 保留卷 > 0：从保留卷中扣除/归还
            ReservedDriveSize = Math.Max(0, ReservedDriveSize - delta);
        }
        else
        {
            // 保留卷 = 0 或未启用：从 OS 分区中扣除/归还
            OsDriveSize = Math.Max(0, Math.Min(OsDriveSize - delta, MaxOsDriveSize));
        }
    }

    partial void OnOsDriveSizeChanged(double value)
    {
        UpdateReservedDriveSize();
    }
}
