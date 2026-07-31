using System;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Builders;
using WTGWizard.Main.DeploymentCore.Models;
using static WTGWizard.Main.DeploymentCore.Models.DeploymentConstants;

namespace WTGWizard.Main.DeploymentCore.Steps;

public sealed class PartitionStep : Contracts.IDeploymentStep
{
    public DeployTaskId TaskId => DeployTaskId.CreateDiskLayout;
    public string TitleKey => "Task.CreateDiskLayout.Title";
    public string DescriptionKey => "Task.CreateDiskLayout.Desc";
    public bool ShouldRun(DeploymentConfig config) => true;

    public async Task<StepResult> ExecuteAsync(Contracts.IStepContext ctx, CancellationToken ct)
    {
        ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Running, 0));

        string script = ctx.Config.IsCleanInstall
            ? DiskScriptBuilder.BuildCleanInstall(ctx.Config)
            : DiskScriptBuilder.BuildPartitionInstall(ctx.Config);

        string prefix = ctx.Config.IsCleanInstall ? "DiskLayout-CleanInst" : "DiskLayout-PartInst";
        string fileName = $"{prefix}-{DateTime.Now:yyMMddHHmmss}.ps1";
        string scriptPath = ctx.SaveTempScript(fileName, script);

        ctx.Logger.Debug("Partition", "Executing partition script: {Path}", scriptPath);

        var cmd = new WorkerCommand("pwsh", $"--script \"{EscapeArg(scriptPath)}\" --timeout {TimeoutPartitionMs}");
        var result = await ctx.ExecuteWorkerAsync(cmd, ct: ct);

        if (!result.Success)
        {
            ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Failed, 0));
            return StepResult.Fail(result.ErrorMessage ?? "Partition failed");
        }

        ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Completed, 100));
        return StepResult.Ok();
    }

    private static string EscapeArg(string v) => v.Replace("\"", "\\\"");
}
