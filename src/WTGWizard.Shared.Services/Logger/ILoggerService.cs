namespace WTGWizard.Shared.Services.Logger;

/// <summary>
/// 日志服务接口 — 支持日志分级。
/// </summary>
public interface ILoggerService
{
    /// <summary>调试日志。</summary>
    void Debug(string category, string message);

    /// <summary>信息日志。</summary>
    void Info(string category, string message);

    /// <summary>警告日志。</summary>
    void Warn(string category, string message);

    /// <summary>错误日志。</summary>
    void Error(string category, string message);

    /// <summary>致命错误日志。</summary>
    void Fatal(string category, string message);

    /// <summary>关闭日志服务，排空队列。</summary>
    void Shutdown();
}
