namespace WTGWizard.Main.DeploymentCore.Models;

public sealed record StepResult(bool IsSuccess, string? ErrorMessage = null)
{
    public static StepResult Ok() => new(true);
    public static StepResult Fail(string msg) => new(false, msg);
}
