using System;
using System.Threading;

namespace WTGWizard.Main.DeploymentCore.Orchestrator;

/// <summary>
/// 活动状态门控的进度转发器 —— 在**投递之前**短路，而不是投递之后再判断。
/// <para>
/// <see cref="Progress{T}"/> 的实现会把回调 <c>Post</c> 到构造时捕获的
/// SynchronizationContext。步骤已经结束后仍继续投递，只会白白占用 UI 线程队列；
/// 而队列里的回调最终又会被 <c>StepContext.Publish</c> 的已释放判断丢弃。
/// </para>
/// <para>
/// 门控信号必须是 <see cref="IStepContext.IsActive"/>（Dispose 时失效），
/// 不能用步骤自身的 CancellationToken —— 后者只在 Abort 时取消，
/// 覆盖不到失败与握手/终态超时这些真正会产生迟到回调的路径。
/// </para>
/// </summary>
public sealed class GatedProgress<T> : IProgress<T>
{
    private readonly IProgress<T> _inner;
    private readonly Func<bool> _isActive;

    public GatedProgress(IProgress<T> inner, Func<bool> isActive)
    {
        _inner = inner;
        _isActive = isActive;
    }

    public void Report(T value)
    {
        // 门控在 Post 之前：步骤结束后不再产生任何 UI 线程排队
        if (!_isActive()) return;
        _inner.Report(value);
    }
}