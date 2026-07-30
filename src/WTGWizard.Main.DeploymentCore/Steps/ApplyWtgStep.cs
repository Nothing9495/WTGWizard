using System;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Builders;
using WTGWizard.Main.DeploymentCore.Models;
using WTGWizard.Main.DeploymentCore.Worker;
using static WTGWizard.Main.DeploymentCore.Models.DeploymentConstants;

namespace WTGWizard.Main.DeploymentCore.Steps;

public sealed class ApplyWtgStep : Contracts.IDeploymentStep
{
    public DeployTaskId TaskId => DeployTaskId.ApplyWtg;
    public bool ShouldRun(DeploymentConfig config)
        => config.HideLocalDisks || config.PreventDeviceEncryption;

    public async Task<StepResult> ExecuteAsync(Contracts.IStepContext ctx, CancellationToken ct)
    {
        ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Running, 0));

        if (string.IsNullOrWhiteSpace(ctx.Config.OsDriveLetter.ToString()) || ctx.Config.OsDriveLetter == '\0')
        {
            ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Failed, 0));
            return StepResult.Fail("osApplyDir is not resolved — partition step may not have run");
        }

        string? unattendFile = AnswerFileGenerator.GenerateAndSave(ctx.Config);
        if (unattendFile is null)
        {
            ctx.Logger.Debug("ApplyWtg", "No WTG settings to apply");
            ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Completed, 100));
            return StepResult.Ok();
        }

        string unattendArgs = CommandBuilder.BuildApplyUnattendArgs($"{ctx.Config.OsDriveLetter}:\\", unattendFile);
        ctx.Logger.Debug("ApplyWtg", "Applying WTG settings: {Args}", unattendArgs);

        var cmd = new WorkerCommand("dism", $"--args \"{EscapeArg(unattendArgs)}\" --timeout {TimeoutApplyWtgMs}");
        var result = await ctx.ExecuteWorkerAsync(cmd, ct: ct);

        if (!result.Success)
        {
            ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Failed, 0));
            return StepResult.Fail(result.ErrorMessage ?? "Apply WTG settings failed");
        }

        ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Completed, 100));
        return StepResult.Ok();
    }

    private static string EscapeArg(string v) => v.Replace("\"", "\\\"");
}
