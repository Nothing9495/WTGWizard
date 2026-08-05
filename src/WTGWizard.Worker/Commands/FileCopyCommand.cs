using System;
using System.Threading;

namespace WTGWizard.Worker.Commands;

/// <summary>
/// FileCopy 命令 — 文件复制。
/// </summary>
internal static class FileCopyCommand
{
    /// <summary>
    /// 执行文件复制命令。
    /// </summary>
    /// <param name="args">命令参数（--src, --dst, --pipe）。</param>
    /// <param name="ct">取消令牌（检查点式）。</param>
    /// <returns>退出码。</returns>
    public static int Run(string[] args, CancellationToken ct)
    {
        string src = CommandArgs.GetArg(args, "--src");
        string dst = CommandArgs.GetArg(args, "--dst");
        string pipeName = CommandArgs.GetArg(args, "--pipe");

        WorkerDebug.Write($"FileCopy: src={src}, dst={dst}, pipe={pipeName}");

        using var pipe = PipeHelper.Connect(pipeName);
        pipe.WriteRunning("filecopy", $"Copying {src} -> {dst}");

        try
        {
            if (!System.IO.File.Exists(src))
            {
                pipe.WriteFailed("filecopy", 1, $"Source file not found: {src}");
                return 1;
            }

            // 自动创建目标目录
            string? dstDir = System.IO.Path.GetDirectoryName(dst);
            if (!string.IsNullOrEmpty(dstDir) && !System.IO.Directory.Exists(dstDir))
            {
                System.IO.Directory.CreateDirectory(dstDir);
                Console.WriteLine($"Created directory: {dstDir}");
            }

            // 检查点取消：复制前后各检查一次
            ct.ThrowIfCancellationRequested();
            System.IO.File.Copy(src, dst, overwrite: true);
            ct.ThrowIfCancellationRequested();
            Console.WriteLine($"Copied: {src} -> {dst}");

            pipe.WriteCompleted("filecopy", 0);
            return 0;
        }
        catch (OperationCanceledException)
        {
            pipe.WriteCancel();
            return 1;
        }
        catch (Exception ex)
        {
            pipe.WriteFailed("filecopy", 1, $"Error copying file: {ex.Message}");
            return 1;
        }
    }
}
