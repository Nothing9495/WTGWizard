using System;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Builders;
using WTGWizard.Main.DeploymentCore.Models;
using WTGWizard.Main.DeploymentCore.Orchestrator;
using static WTGWizard.Main.DeploymentCore.Models.DeploymentConstants;

namespace WTGWizard.Main.DeploymentCore.Steps;

public sealed class PartitionStep : DeploymentStepBase
{
    public override DeployTaskId TaskId => DeployTaskId.CreateDiskLayout;
    public override string TitleKey => "Task.CreateDiskLayout.Title";
    public override string DescriptionKey => "Task.CreateDiskLayout.Desc";
    public override bool ShouldRun(DeploymentConfig config) => true;

    protected override async Task<StepResult> ExecuteCoreAsync(Contracts.IStepContext ctx, CancellationToken ct)
    {
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
            return StepResult.Fail(result.ErrorMessage ?? "Partition failed");

        return StepResult.Ok();
    }

    private static string EscapeArg(string v) => v.Replace("\"", "\\\"");
}
