using System;

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
    /// <returns>退出码。</returns>
    public static int Run(string[] args)
    {
        string scriptPath = CommandArgs.GetArg(args, "--script");
        string pipeName = CommandArgs.GetArg(args, "--pipe");
        int timeoutMs = int.TryParse(CommandArgs.TryGetArg(args, "--timeout"), out var t) ? t : 0;

        WorkerDebug.Write($"PowerShell: script={scriptPath}, pipe={pipeName}, timeout={timeoutMs}ms");

        if (!System.IO.File.Exists(scriptPath))
        {
            using var pipe0 = PipeHelper.Connect(pipeName);
            pipe0.WriteFailed("pwsh", 1, $"Script file not found: {scriptPath}");
            return 1;
        }

        using var pipe = PipeHelper.Connect(pipeName);
        pipe.WriteRunning("pwsh", $"Executing PowerShell script: {scriptPath}");

        try
        {
            int exitCode = ProcessRunner.Run(
                "powershell.exe",
                $"-ExecutionPolicy Bypass -File \"{scriptPath}\"",
                timeoutMs);

            if (exitCode == 0)
                pipe.WriteCompleted("pwsh", 0);
            else
                pipe.WriteFailed("pwsh", exitCode, $"PowerShell script failed with exit code {exitCode}");

            return exitCode;
        }
        catch (Exception ex)
        {
            pipe.WriteFailed("pwsh", 1, $"Error executing PowerShell script: {ex.Message}");
            return 1;
        }
    }
}
