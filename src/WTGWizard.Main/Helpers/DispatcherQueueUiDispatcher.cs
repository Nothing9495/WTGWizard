using System;
using Microsoft.UI.Dispatching;
using WTGWizard.Main.DeploymentCore.Contracts;

namespace WTGWizard.Helpers;

/// <summary>
/// 基于 <see cref="DispatcherQueue"/> 的 UI 投递器。
/// DispatcherQueue 是 FIFO 的，因此投递顺序即执行顺序，进度更新不会乱序。
/// </summary>
/// <remarks>
/// 实例在 App 启动（UI 线程）上以 <see cref="DispatcherQueue.GetForCurrentThread"/> 捕获队列，
/// 之后可从任意后台线程调用 <see cref="Post"/>。
/// </remarks>
internal sealed class DispatcherQueueUiDispatcher : IUiDispatcher
{
    private readonly DispatcherQueue _queue;

    public DispatcherQueueUiDispatcher(DispatcherQueue queue)
        => _queue = queue ?? throw new ArgumentNullException(nameof(queue));

    public bool Post(Action action) => _queue.TryEnqueue(() => action());
}