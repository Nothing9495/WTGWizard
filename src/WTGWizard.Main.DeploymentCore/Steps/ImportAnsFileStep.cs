using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Models;
using WTGWizard.Main.DeploymentCore.Worker;

namespace WTGWizard.Main.DeploymentCore.Steps;

public sealed class ImportAnsFileStep : Contracts.IDeploymentStep
{
    public DeployTaskId TaskId => DeployTaskId.ImportAnswerFile;
    public string TitleKey => "Task.ImportAnswerFile.Title";
    public string DescriptionKey => "Task.ImportAnswerFile.Desc";
    public bool ShouldRun(DeploymentConfig config)
        => config.CustomAnsFileEnabled && !string.IsNullOrWhiteSpace(config.AnsFilePath);

    public async Task<StepResult> ExecuteAsync(Contracts.IStepContext ctx, CancellationToken ct)
    {
        ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Running, 0));

        if (string.IsNullOrWhiteSpace(ctx.Config.OsDriveLetter.ToString()) || ctx.Config.OsDriveLetter == '\0')
        {
            ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Failed, 0));
            return StepResult.Fail("osApplyDir is not resolved — partition step may not have run");
        }

        if (!File.Exists(ctx.Config.AnsFilePath))
        {
            ctx.Logger.Error("ImportAns", "Answer file not found: {Path}", ctx.Config.AnsFilePath);
            ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Failed, 0));
            return StepResult.Fail($"Answer file not found: {ctx.Config.AnsFilePath}");
        }

        string pantherDir = Path.Combine($"{ctx.Config.OsDriveLetter}:\\", "Windows", "Panther");
        string targetPath = Path.Combine(pantherDir, "unattend.xml");

        if (ctx.Config.CleanImageAnsFile && File.Exists(targetPath))
        {
            ctx.Logger.Debug("ImportAns", "Cleaning built-in answer file: {Path}", targetPath);
            File.Delete(targetPath);
        }

        Directory.CreateDirectory(pantherDir);
        ctx.Logger.Debug("ImportAns", "Copying answer file to: {Path}", targetPath);

        var cmd = new WorkerCommand("filecopy",
            $"--src \"{EscapeArg(ctx.Config.AnsFilePath)}\" --dst \"{EscapeArg(targetPath)}\"");
        var result = await ctx.ExecuteWorkerAsync(cmd, ct: ct);

        if (!result.Success)
        {
            ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Failed, 0));
            return StepResult.Fail(result.ErrorMessage ?? "Answer file copy failed");
        }

        ctx.Publish(new TaskUpdate(TaskId, DeployTaskStatus.Completed, 100));
        return StepResult.Ok();
    }

    private static string EscapeArg(string v) => v.Replace("\"", "\\\"");
}
