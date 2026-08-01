using System;
using System.Collections.Generic;
using WTGWizard.Shared.Services.Logger;
using WTGWizard.Shared.Services.WimService;

namespace WTGWizard.Worker.Commands;

/// <summary>
/// Extract 命令 — WIM 镜像提取。
/// 通过 SharedServices.WimService 执行（wimlib 由 Services 层统一加载）。
/// 双通道回传：阶段消息/节流进度行 → stdout（TerminalBox）；进度 → NamedPipe（卡片进度环）。
/// </summary>
internal static class ExtractCommand
{
    private static readonly Dictionary<WimExtractStage, string> StageMessages = new()
    {
        [WimExtractStage.ExtractImageBegin] = "Starting image extraction...",
        [WimExtractStage.ExtractTreeBegin] = "Creating directory structure...",
        [WimExtractStage.ExtractFileStructure] = "Creating blank files...",
        [WimExtractStage.ExtractMetadata] = "Applying metadata...",
    };

    /// <summary>
    /// 执行 WIM 提取命令。
    /// </summary>
    /// <param name="args">命令参数（--wim, --index, --target, --pipe）。</param>
    /// <param name="logger">日志服务（由 Program 传入，共享实例）。</param>
    /// <returns>退出码。</returns>
    public static int Run(string[] args, ILoggerService logger)
    {
        string wimPath = CommandArgs.GetArg(args, "--wim");
        int index = int.Parse(CommandArgs.GetArg(args, "--index"));
        string targetDir = CommandArgs.GetArg(args, "--target");
        string pipeName = CommandArgs.GetArg(args, "--pipe");

        using var pipe = PipeHelper.Connect(pipeName);
        pipe.WriteRunning("extract", $"Extracting {wimPath} #{index} -> {targetDir}");

        try
        {
            var wimService = new WimService(logger);
            Console.WriteLine($"Opened WIM, extracting image #{index}...");

            var lastProgressLog = DateTime.MinValue;
            var loggedStages = new HashSet<WimExtractStage>();

            var progress = new Progress<(ulong current, ulong total)>(p =>
            {
                if (p.total <= 0) return;

                // 通道 1: NamedPipe 进度（高频，驱动卡片确定环）
                double percent = p.current * 100.0 / p.total;
                pipe.WriteProgress("extract", percent);

                // 通道 2: stdout 进度行（5 秒节流，供终端阅读）
                var now = DateTime.UtcNow;
                if ((now - lastProgressLog).TotalSeconds >= 5.0)
                {
                    var doneMB = p.current / (1024.0 * 1024.0);
                    var totalMB = p.total / (1024.0 * 1024.0);
                    Console.WriteLine($"Extracting data: {percent:F1}% ({doneMB:F0} MB / {totalMB:F0} MB)");
                    lastProgressLog = now;
                }
            });

            wimService.ExtractImageAsync(wimPath, index, targetDir, progress,
                stageChanged: s =>
                {
                    // 通道 2: stdout 阶段消息（低频，每阶段一次，去重）
                    if (loggedStages.Add(s) && StageMessages.TryGetValue(s, out var msg))
                        Console.WriteLine(msg);
                }).GetAwaiter().GetResult();

            pipe.WriteCompleted("extract", 0);
            return 0;
        }
        catch (Exception ex)
        {
            pipe.WriteFailed("extract", 1, $"Extract failed: {ex.Message}");
            return 1;
        }
    }
}
