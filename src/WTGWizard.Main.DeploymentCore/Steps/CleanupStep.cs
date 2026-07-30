using System;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Builders;
using WTGWizard.Main.DeploymentCore.Models;
using static WTGWizard.Main.DeploymentCore.Models.DeploymentConstants;

namespace WTGWizard.Main.DeploymentCore.Steps;

public sealed class CleanupStep : Contracts.IDeploymentStep
{
    public DeployTaskId TaskId => DeployTaskId.RemoveLetter;
    public bool ShouldRun(DeploymentConfig config) => true;

    public async Task<StepResult> ExecuteAsync(Contracts.IStepContext ctx, CancellationToken ct)
    {
        ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Running, 0));

        uint espPartNum = ctx.Config.IsCleanInstall
            ? CleanInstallEspPartNum : ctx.Config.EspVolumeId;
        uint osPartNum = ctx.Config.IsCleanInstall
            ? CleanInstallOsPartNum : ctx.Config.OsDriveVolumeId;

        ctx.Logger.Debug("Cleanup", "Removing letters: ESP=#{Esp}, OS=#{Os}, AutoRemoveOs={Auto}",
            espPartNum, osPartNum, ctx.Config.AutoRemoveOsDriveLetter);

        string script = DiskScriptBuilder.BuildCleanup(
            ctx.Config.DiskSelectedId, ctx.Config.AutoRemoveOsDriveLetter, espPartNum, osPartNum);

        string fileName = $"RemoveDriveLetter-{DateTime.Now:yyMMddHHmmss}.ps1";
        string scriptPath = ctx.SaveTempScript(fileName, script);

        var cmd = new WorkerCommand("pwsh", $"--script \"{EscapeArg(scriptPath)}\" --timeout {TimeoutCleanupMs}");
        var result = await ctx.ExecuteWorkerAsync(cmd, ct: ct);

        if (!result.Success)
        {
            ctx.Logger.Warn("Cleanup", "Cleanup script exited with: {Msg}", result.ErrorMessage);
            ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Failed, 0));
            return StepResult.Ok();
        }

        ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Completed, 100));
        return StepResult.Ok();
    }

    private static string EscapeArg(string v) => v.Replace("\"", "\\\"");
}
