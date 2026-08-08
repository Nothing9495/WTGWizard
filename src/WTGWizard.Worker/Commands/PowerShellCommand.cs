using System;
using System.Threading;
using WTGWizard.Shared.Services.Logger;

namespace WTGWizard.Worker.Commands;

/// <summary>
/// PowerShell 命令 — 执行 PowerShell 脚本。
/// </summary>
internal static class PowerShellCommand
{
    /// <summary>
    /// 执行 PowerShell 脚本命令。
    /// </summary>
    /// <param name="args">命令参数（--script, --pipe）。</param>
    /// <param name="logger">日志服务（由 Program 传入，共享实例）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>退出码。</returns>
    public static int Run(string[] args, ILoggerService logger, CancellationToken ct)
    {
        string scriptPath = CommandArgs.GetArg(args, "--script");
        string pipeName = CommandArgs.GetArg(args, "--pipe");
        int timeoutMs = int.TryParse(CommandArgs.TryGetArg(args, "--timeout"), out var t) ? t : 0;

        WorkerDebug.Write($"PowerShell: script={scriptPath}, pipe={pipeName}, timeout={timeoutMs}ms");

        using var pipe = PipeHelper.Connect(pipeName);

        // 文件检查并入统一连接：缺失时仍须连接并回报失败（否则 Main 等待超时）
        if (!System.IO.File.Exists(scriptPath))
        {
            pipe.WriteFailed("pwsh", 1, $"Script file not found: {scriptPath}");
            return 1;
        }

        pipe.WriteRunning("pwsh", $"Executing PowerShell script: {scriptPath}");

        try
        {
            int exitCode = ProcessRunner.Run(
                "powershell.exe",
                $"-ExecutionPolicy Bypass -File \"{scriptPath}\"",
                timeoutMs,
                ct);

            if (exitCode == 0)
                pipe.WriteCompleted("pwsh", 0);
            else
                pipe.WriteFailed("pwsh", exitCode, $"PowerShell script failed with exit code {exitCode}");

            return exitCode;
        }
        catch (OperationCanceledException)
        {
            pipe.WriteCancelled("pwsh");
            return 1;
        }
        catch (Exception ex)
        {
            logger.Error("PowerShellCommand", "PowerShell script failed - ({Error}).", ex.ToString());
            pipe.WriteFailed("pwsh", 1, $"Error executing PowerShell script: {ex.Message}");
            return 1;
        }
    }
}
