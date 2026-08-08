using System;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Builders;
using WTGWizard.Main.DeploymentCore.Models;
using WTGWizard.Main.DeploymentCore.Orchestrator;
using WTGWizard.Main.DeploymentCore.Worker;
using static WTGWizard.Main.DeploymentCore.Models.DeploymentConstants;

namespace WTGWizard.Main.DeploymentCore.Steps;

public sealed class ApplyWtgStep : DeploymentStepBase
{
    public override DeployTaskId TaskId => DeployTaskId.ApplySysSettings;
    public override string TitleKey => "Task.ApplySysSettings.Title";
    public override string DescriptionKey => "Task.ApplySysSettings.Desc";
    public override bool ShouldRun(DeploymentConfig config)
        => config.HideLocalDisks || config.PreventDeviceEncryption;

    protected override async Task<StepResult> ExecuteCoreAsync(Contracts.IStepContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ctx.Config.OsDriveLetter.ToString()) || ctx.Config.OsDriveLetter == '\0')
            return StepResult.Fail("osApplyDir is not resolved — partition step may not have run");

        string? unattendFile = AnswerFileGenerator.GenerateAndSave(ctx.Config);
        if (unattendFile is null)
        {
            ctx.Logger.Debug("ApplyWtg", "No WTG settings to apply");
            return StepResult.Ok();
        }

        string unattendArgs = CommandBuilder.BuildApplyUnattendArgs($"{ctx.Config.OsDriveLetter}:\\", unattendFile);
        ctx.Logger.Debug("ApplyWtg", "Applying WTG settings: {Args}", unattendArgs);

        var cmd = new WorkerCommand("dism", $"--args \"{EscapeArg(unattendArgs)}\" --timeout {TimeoutApplyWtgMs}");
        var result = await ctx.ExecuteWorkerAsync(cmd, ct: ct);

        if (!result.Success)
            return StepResult.Fail(result.ErrorMessage ?? "Apply WTG settings failed");

        return StepResult.Ok();
    }

    private static string EscapeArg(string v) => v.Replace("\"", "\\\"");
}
