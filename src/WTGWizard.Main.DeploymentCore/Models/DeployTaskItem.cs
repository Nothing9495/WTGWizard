using CommunityToolkit.Mvvm.ComponentModel;

namespace WTGWizard.Main.DeploymentCore.Models;

public sealed partial class DeployTaskItem : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    [ObservableProperty]
    private DeployTaskStatus _status = DeployTaskStatus.Pending;

    [ObservableProperty]
    private double _progressValue;
}
