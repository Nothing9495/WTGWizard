using System;

namespace WTGWizard.Worker.Commands;

/// <summary>
/// DISM 命令 — 执行 DISM 操作。
/// </summary>
internal static class DismCommand
{
    /// <summary>
    /// 执行 DISM 命令。
    /// </summary>
    /// <param name="args">命令参数（--args, --pipe）。</param>
    /// <returns>退出码。</returns>
    public static int Run(string[] args)
    {
        string dismArgs = CommandArgs.GetArg(args, "--args");
        string pipeName = CommandArgs.GetArg(args, "--pipe");

        using var pipe = PipeHelper.Connect(pipeName);
        pipe.WriteRunning("dism", $"Executing DISM: {dismArgs}");

        try
        {
            int exitCode = ProcessRunner.Run("dism.exe", dismArgs);

            if (exitCode == 0)
                pipe.WriteCompleted("dism", 0);
            else
                pipe.WriteFailed("dism", exitCode, $"DISM failed with exit code {exitCode}");

            return exitCode;
        }
        catch (Exception ex)
        {
            pipe.WriteFailed("dism", 1, $"Error executing DISM: {ex.Message}");
            return 1;
        }
    }
}
