using System;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Builders;
using WTGWizard.Main.DeploymentCore.Models;
using WTGWizard.Main.DeploymentCore.Orchestrator;
using WTGWizard.Shared.Services.DiskServices;
using static WTGWizard.Main.DeploymentCore.Models.DeploymentConstants;

namespace WTGWizard.Main.DeploymentCore.Steps;

public sealed class CleanupStep : DeploymentStepBase
{
    public override DeployTaskId TaskId => DeployTaskId.RemoveDriveLetters;
    public override string TitleKey => "Task.RemoveDriveLetters.Title";
    public override string DescriptionKey => "Task.RemoveDriveLetters.Desc.Esp";
    public override bool ShouldRun(DeploymentConfig config) => true;

    protected override async Task<StepResult> ExecuteCoreAsync(Contracts.IStepContext ctx, CancellationToken ct)
    {
        uint espPartNum = ctx.Config.IsCleanInstall
            ? DiskConstants.CleanInstallEspPartNum : ctx.Config.EspVolumeId;
        uint osPartNum = ctx.Config.IsCleanInstall
            ? DiskConstants.CleanInstallOsPartNum : ctx.Config.OsDriveVolumeId;

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
            // 非致命失败：盘符移除失败不终止部署，任务以 Completed 呈现 + 警告日志
            ctx.Logger.Warn("Cleanup", "Cleanup script exited with: {Msg}", result.ErrorMessage);
            return StepResult.NonFatalFail(result.ErrorMessage ?? "Cleanup failed");
        }

        return StepResult.Ok();
    }

    private static string EscapeArg(string v) => v.Replace("\"", "\\\"");
}
