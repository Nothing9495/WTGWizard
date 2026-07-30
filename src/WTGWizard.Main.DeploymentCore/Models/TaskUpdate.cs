namespace WTGWizard.Main.DeploymentCore.Models;

public sealed record TaskUpdate(DeployTaskId TaskId, DeployTaskStatus Status, double Progress);
