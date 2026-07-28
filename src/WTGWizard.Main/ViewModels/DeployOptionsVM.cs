using CommunityToolkit.Mvvm.ComponentModel;

namespace WTGWizard.ViewModels;

/// <summary>
/// Step 3 状态：部署设置。
/// </summary>
public sealed partial class DeployOptionsVM : ObservableObject
{
    [ObservableProperty] public partial bool HideLocalDisks { get; set; } = true;
    [ObservableProperty] public partial bool PreventDeviceEncryption { get; set; } = true;
    [ObservableProperty] public partial bool NoDefaultDriveLetter { get; set; }
    [ObservableProperty] public partial bool AutoRemoveOsDriveLetter { get; set; }
    [ObservableProperty] public partial bool UseDismToDeploy { get; set; }
}
