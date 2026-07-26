using System;
using System.IO;
using Serilog;
using Serilog.Events;

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
    public LoggerService(string? logDirectory = null)
    {
        _logDir = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WTGWizard", "logs");
        Directory.CreateDirectory(_logDir);

        _logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                path: Path.Combine(_logDir, "WTGWizard-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    public void Debug(string category, string message)
        => _logger.ForContext("Category", category).Debug("{Message}", message);

    public void Info(string category, string message)
        => _logger.ForContext("Category", category).Information("{Message}", message);

    public void Warn(string category, string message)
        => _logger.ForContext("Category", category).Warning("{Message}", message);

    public void Error(string category, string message)
        => _logger.ForContext("Category", category).Error("{Message}", message);

    public void Fatal(string category, string message)
        => _logger.ForContext("Category", category).Fatal("{Message}", message);

    public void Shutdown()
    {
        Log.CloseAndFlush();
    }

    public void Dispose()
    {
        Shutdown();
    }
}
