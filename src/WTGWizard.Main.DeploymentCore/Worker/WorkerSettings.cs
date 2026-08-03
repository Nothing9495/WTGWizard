namespace WTGWizard.Main.DeploymentCore.Worker;

/// <summary>
/// Worker 运行设置（设置页 Toggle 写入，WorkerProcess 启动时读取）。
/// </summary>
public static class WorkerSettings
{
    /// <summary>启用调试输出 — 为 Worker 追加 --debug 参数。</summary>
    public static bool EnableDebugOutput { get; set; }
}
