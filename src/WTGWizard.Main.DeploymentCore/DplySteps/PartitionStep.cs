using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static WTGWizard.Main.DeploymentCore.Models.DeploymentConstants;
using WTGWizard.Main.DeploymentCore.WorkerCore;
using WTGWizard.Main.DeploymentCore.Models;
using WTGWizard.Main.DeploymentCore.Orchestrator;
using WTGWizard.Main.DeploymentCore.Builders;

namespace WTGWizard.Main.DeploymentCore.DplySteps;

/// <summary>
/// 步骤 1：磁盘分区。
/// </summary>
public sealed class PartitionStep : IDeploymentStep
{
    public string TaskId => "partition";
    public bool ShouldRun(DeploymentConfig config) => true;

    public async Task ExecuteAsync(StepContext ctx, string? _, CancellationToken ct)
    {
        ctx.SetTaskStatus("partition", DeployTaskStatus.Running);

        string script = ctx.Config.IsCleanInstall
            ? DiskScriptBuilder.BuildCleanInstall(ctx.Config)
            : DiskScriptBuilder.BuildPartitionInstall(ctx.Config);

        string prefix = ctx.Config.IsCleanInstall ? "DiskLayout-CleanInst" : "DiskLayout-PartInst";
        string fileName = $"{prefix}-{DateTime.Now:yyMMddHHmmss}.ps1";
        string scriptPath = SaveTempScript(fileName, script);

        ctx.Logger.Debug("Partition", "Executing partition script: {Path}", scriptPath);

        var (cmd, args) = WorkerFactory.BuildPwsh(scriptPath, TimeoutPartitionMs);
        var result = await ctx.WorkerManager.ExecuteCommandAsync(cmd, args, ct: ct);

        if (!result.Success)
        {
            ctx.SetTaskStatus("partition", DeployTaskStatus.Failed);
            throw new InvalidOperationException($"Partition failed: {result.ErrorMessage}");
        }

        ctx.SetTaskStatus("partition", DeployTaskStatus.Completed, 100);
    }

    private static string SaveTempScript(string fileName, string content)
    {
        string dir = Path.Combine(Path.GetTempPath(), "WTGWizard", "WorkerCache", "Scripts");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName);
        File.WriteAllText(path, content, System.Text.Encoding.UTF8);
        return path;
    }
}
