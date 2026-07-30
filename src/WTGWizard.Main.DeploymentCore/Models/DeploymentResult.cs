namespace WTGWizard.Main.DeploymentCore.Models;

public sealed record DeploymentResult(bool IsSuccess, DeployTaskId? FailedAt = null, string? ErrorMessage = null)
{
    public static DeploymentResult Ok() => new(true);
    public static DeploymentResult Failed(DeployTaskId? step, string error) => new(false, step, error);
    public static DeploymentResult Cancelled() => new(false, null, "Cancelled");
}
