using System;

namespace WTGWizard.Worker.Commands;

/// <summary>
/// BCDBoot 命令 — 执行 BCDBoot 启动配置。
/// </summary>
internal static class BcdbootCommand
{
    /// <summary>
    /// 执行 BCDBoot 命令。
    /// </summary>
    /// <param name="args">命令参数（--args, --pipe）。</param>
    /// <returns>退出码。</returns>
    public static int Run(string[] args)
    {
        string bcdbootArgs = CommandArgs.GetArg(args, "--args");
        string pipeName = CommandArgs.GetArg(args, "--pipe");
        int timeoutMs = int.TryParse(CommandArgs.TryGetArg(args, "--timeout"), out var t) ? t : 0;

        WorkerDebug.Write($"BCDBoot: args={bcdbootArgs}, pipe={pipeName}, timeout={timeoutMs}ms");

        using var pipe = PipeHelper.Connect(pipeName);
        pipe.WriteRunning("bcdboot", $"Executing bcdboot: {bcdbootArgs}");

        try
        {
            int exitCode = ProcessRunner.Run("bcdboot.exe", bcdbootArgs, timeoutMs);

            if (exitCode == 0)
                pipe.WriteCompleted("bcdboot", 0);
            else
                pipe.WriteFailed("bcdboot", exitCode, $"bcdboot failed with exit code {exitCode}");

            return exitCode;
        }
        catch (Exception ex)
        {
            pipe.WriteFailed("bcdboot", 1, $"Error executing bcdboot: {ex.Message}");
            return 1;
        }
    }
}
