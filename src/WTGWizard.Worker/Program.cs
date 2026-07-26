using System;
using System.Threading.Tasks;
using WTGWizard.Shared.Services.Logger;
using WTGWizard.Worker.Commands;

namespace WTGWizard.Worker;

/// <summary>
/// WTGWizard.Worker — 独立子进程
/// NamedPipe 协议与主进程通信
/// </summary>
static class Program
{
    static int Main(string[] args)
    {
        // 初始化日志服务
        using var logService = new LoggerService();

        // 创建 UTF-8 stdout Writer（不依赖 Console.OutputEncoding，直接包装底层流）
        var stdoutStream = Console.OpenStandardOutput();
        var stdoutWriter = new System.IO.StreamWriter(stdoutStream, new System.Text.UTF8Encoding(false)) { AutoFlush = true };

        // 包装为双重输出（stdout + 日志文件）
        var logFileStream = new System.IO.StreamWriter(
            System.IO.Path.Combine(logService.LogDirectory, $"WTGWorker_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log"),
            append: true,
            encoding: new System.Text.UTF8Encoding(false));
        logFileStream.AutoFlush = true;

        var tee = new TeeWriter(stdoutWriter, logFileStream);
        Console.SetOut(tee);

        logService.Info("Worker", "Worker started, PID: {Pid}", Environment.ProcessId);

        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: WTGWizard.Worker <command> [options...] --pipe <name>");
            Console.Error.WriteLine("Commands:");
            Console.Error.WriteLine("  extract    --wim <path> --index <N> --target <dir> --pipe <name>");
            Console.Error.WriteLine("  pwsh       --script <path> --pipe <name>");
            Console.Error.WriteLine("  dism       --args \"<dism_args>\" --pipe <name>");
            Console.Error.WriteLine("  bcdboot    --args \"<bcdboot_args>\" --pipe <name>");
            Console.Error.WriteLine("  filecopy   --src <path> --dst <path> --pipe <name>");
            return 1;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "extract" => ExtractCommand.Run(args[1..]),
                "pwsh" => PowerShellCommand.Run(args[1..]),
                "dism" => DismCommand.Run(args[1..]),
                "bcdboot" => BcdbootCommand.Run(args[1..]),
                "filecopy" => FileCopyCommand.Run(args[1..]),
                _ => throw new ArgumentException($"Unknown command: {args[0]}")
            };
        }
        catch (Exception ex)
        {
            logService.Error("Worker", "Unhandled exception: {ErrorMessage}", ex.Message);
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
