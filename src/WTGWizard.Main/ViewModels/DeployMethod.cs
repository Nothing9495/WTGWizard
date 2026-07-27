using CommunityToolkit.Mvvm.ComponentModel;

namespace WTGWizard.ViewModels;

/// <summary>
/// Step 2 状态：部署方式选择 + 磁盘/分区。
/// </summary>
public sealed partial class DeployMethod : ObservableObject
{
    [ObservableProperty] public partial bool IsCleanInstall { get; set; } = true;
    [ObservableProperty] public partial string? SelectedDiskId { get; set; }
    [ObservableProperty] public partial string? SelectedPartitionId { get; set; }

    public bool IsValid => !string.IsNullOrEmpty(SelectedDiskId);
}
