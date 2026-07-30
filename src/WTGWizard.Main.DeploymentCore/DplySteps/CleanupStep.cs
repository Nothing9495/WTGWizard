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
/// 步骤 7：移除盘符（部署后清理）。
/// </summary>
public sealed class CleanupStep : IDeploymentStep
{
    public string TaskId => "remove-letter";
    public bool ShouldRun(DeploymentConfig config) => true;

    public async Task ExecuteAsync(StepContext ctx, string? _, CancellationToken ct)
    {
        ctx.SetTaskStatus("remove-letter", DeployTaskStatus.Running);

        uint espPartNum = ctx.Config.IsCleanInstall
            ? CleanInstallEspPartNum
            : ctx.Config.EspVolumeId;
        uint osPartNum = ctx.Config.IsCleanInstall
            ? CleanInstallOsPartNum
            : ctx.Config.OsDriveVolumeId;

        ctx.Logger.Debug("Cleanup", "Removing letters: ESP=#{Esp}, OS=#{Os}, AutoRemoveOs={Auto}",
            espPartNum, osPartNum, ctx.Config.AutoRemoveOsDriveLetter);

        string script = DiskScriptBuilder.BuildCleanup(
            ctx.Config.DiskSelectedId, ctx.Config.AutoRemoveOsDriveLetter, espPartNum, osPartNum);

        string fileName = $"RemoveDriveLetter-{DateTime.Now:yyMMddHHmmss}.ps1";
        string scriptPath = SaveTempScript(fileName, script);

        var (cmd, args) = WorkerFactory.BuildPwsh(scriptPath, TimeoutCleanupMs);
        var result = await ctx.WorkerManager.ExecuteCommandAsync(cmd, args, ct: ct);

        if (!result.Success)
        {
            ctx.SetTaskStatus("remove-letter", DeployTaskStatus.Failed);
            ctx.Logger.Warn("Cleanup", "Cleanup script exited with: {Msg}", result.ErrorMessage);
        }
        else
        {
            ctx.SetTaskStatus("remove-letter", DeployTaskStatus.Completed, 100);
        }
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
