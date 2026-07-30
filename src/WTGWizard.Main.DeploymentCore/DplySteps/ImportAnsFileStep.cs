using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Main.DeploymentCore.Models;
using WTGWizard.Main.DeploymentCore.Orchestrator;
using WTGWizard.Main.DeploymentCore.WorkerCore;

namespace WTGWizard.Main.DeploymentCore.DplySteps;

/// <summary>
/// 步骤 4：导入自定义应答文件（可选）。
/// </summary>
public sealed class ImportAnsFileStep : IDeploymentStep
{
    public string TaskId => "import-ansfile";
    public bool ShouldRun(DeploymentConfig config)
        => config.CustomAnsFileEnabled && !string.IsNullOrWhiteSpace(config.AnsFilePath);

    public async Task ExecuteAsync(StepContext ctx, string? osApplyDir, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(osApplyDir))
            throw new InvalidOperationException("osApplyDir is null — partition step may not have run");

        ctx.SetTaskStatus("import-ansfile", DeployTaskStatus.Running);

        if (!File.Exists(ctx.Config.AnsFilePath))
        {
            ctx.Logger.Error("ImportAns", "Answer file not found: {Path}", ctx.Config.AnsFilePath);
            ctx.SetTaskStatus("import-ansfile", DeployTaskStatus.Failed);
            throw new InvalidOperationException($"Answer file not found: {ctx.Config.AnsFilePath}");
        }

        string pantherDir = Path.Combine(osApplyDir.TrimEnd('\\'), "Windows", "Panther");
        string targetPath = Path.Combine(pantherDir, "unattend.xml");

        if (ctx.Config.CleanImageAnsFile && File.Exists(targetPath))
        {
            ctx.Logger.Debug("ImportAns", "Cleaning built-in answer file: {Path}", targetPath);
            File.Delete(targetPath);
        }

        Directory.CreateDirectory(pantherDir);

        ctx.Logger.Debug("ImportAns", "Copying answer file to: {Path}", targetPath);

        var (cmd, args) = WorkerFactory.BuildFileCopy(ctx.Config.AnsFilePath, targetPath);
        var result = await ctx.WorkerManager.ExecuteCommandAsync(cmd, args, ct: ct);

        if (!result.Success)
        {
            ctx.SetTaskStatus("import-ansfile", DeployTaskStatus.Failed);
            throw new InvalidOperationException($"Answer file copy failed: {result.ErrorMessage}");
        }

        ctx.SetTaskStatus("import-ansfile", DeployTaskStatus.Completed, 100);
    }
}
