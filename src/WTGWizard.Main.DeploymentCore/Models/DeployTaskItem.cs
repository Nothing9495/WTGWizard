namespace WTGWizard.Main.DeploymentCore.Models;

/// <summary>
/// 单个部署步骤的 UI 绑定模型 — ObservableObject，供 TaskPage 的 ItemsControl 绑定。
/// </summary>
public sealed partial class DeployTaskItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    private DeployTaskStatus _status = DeployTaskStatus.Pending;
    public DeployTaskStatus Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    private double _progressValue;
    public double ProgressValue
    {
        get => _progressValue;
        set => SetProperty(ref _progressValue, value);
    }
}
