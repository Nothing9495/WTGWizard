using CommunityToolkit.Mvvm.ComponentModel;

namespace WTGWizard.ViewModels;

/// <summary>
/// Step 3 状态：ESP/OS/保留卷配置。
/// </summary>
public sealed partial class DeployOptions : ObservableObject
{
    [ObservableProperty] public partial int EfiPartSize { get; set; } = 300;
    [ObservableProperty] public partial double OsDriveSize { get; set; }
    [ObservableProperty] public partial bool EnableReservedVol { get; set; }
    [ObservableProperty] public partial string OsDriveLabel { get; set; } = "OS";
    [ObservableProperty] public partial string ReservedDriveLabel { get; set; } = "Reserved";
    [ObservableProperty] public partial string ReservedDriveFs { get; set; } = "ntfs";
    [ObservableProperty] public partial bool HideLocalDisks { get; set; } = true;
    [ObservableProperty] public partial bool PreventDeviceEncryption { get; set; } = true;
    [ObservableProperty] public partial bool NoDefaultDriveLetter { get; set; }
    [ObservableProperty] public partial bool AutoRemoveOsDriveLetter { get; set; }
    [ObservableProperty] public partial bool UseDismToDeploy { get; set; }
}
