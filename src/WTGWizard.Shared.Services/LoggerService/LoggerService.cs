using System;
using System.IO;
using Serilog;

namespace WTGWizard.Shared.Services.Logger;

/// <summary>
/// 统一日志服务 — 基于 Serilog 实现。
/// 异步写入，自动轮转，线程安全。
/// </summary>
public sealed class LoggerService : ILoggerService, IDisposable
{
    private readonly Serilog.ILogger _logger;
    private readonly string _logDir;

    /// <summary>获取当前日志目录。</summary>
    public string LogDirectory => _logDir;

    /// <summary>
    /// 创建日志服务实例。
    /// </summary>
    /// <param name="logDirectory">日志目录，null 时使用默认目录（%LOCALAPPDATA%\WTGWizard\logs）。</param>
    /// <param name="enableFile">是否写入日志文件。</param>
    /// <param name="fileNameTemplate">日志文件名模板（Serilog 自动追加滚动日期）。</param>
    public LoggerService(string? logDirectory = null, bool enableFile = true,
        string fileNameTemplate = "WTGWizard-.log")
    {
        _logDir = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WTGWizard", "logs");
        Directory.CreateDirectory(_logDir);

        var config = new LoggerConfiguration();

#if DEBUG
        config.MinimumLevel.Debug();
#else
        config.MinimumLevel.Information();
#endif

        config.WriteTo.Debug(
            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [{Category}] {Message:lj}{NewLine}{Exception}");

        if (enableFile)
        {
            config.WriteTo.File(
                path: Path.Combine(_logDir, fileNameTemplate),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [{Category}] {Message:lj}{NewLine}{Exception}");
        }

        _logger = config.CreateLogger();
    }

    public void Debug(string category, string message, params object?[] args)
        => _logger.ForContext("Category", category).Debug(message, args);

    public void Info(string category, string message, params object?[] args)
        => _logger.ForContext("Category", category).Information(message, args);

    public void Warn(string category, string message, params object?[] args)
        => _logger.ForContext("Category", category).Warning(message, args);

    public void Error(string category, string message, params object?[] args)
        => _logger.ForContext("Category", category).Error(message, args);

    public void Fatal(string category, string message, params object?[] args)
        => _logger.ForContext("Category", category).Fatal(message, args);

    public void Shutdown()
    {
        (_logger as IDisposable)?.Dispose();
    }

    public void Dispose()
    {
        Shutdown();
    }
}
