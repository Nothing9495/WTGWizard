using System;
using System.Collections.ObjectModel;
using WTGWizard.Main.DeploymentCore.Contracts;
using WTGWizard.Main.DeploymentCore.Models;

namespace WTGWizard.Main.DeploymentCore.Orchestrator;

/// <summary>
/// 把 <see cref="TaskUpdate"/> 落到任务列表 —— <b>并保证写入发生在 UI 线程</b>。
/// </summary>
/// <remarks>
/// 单独抽成类型的原因：这段"按 TaskId 找到元素并改属性"的逻辑是整个 UI 解耦里唯一
/// 会跨线程触碰 UI 绑定对象的地方，把它收敛到一个可独立测试的单元里，
/// 就不必靠真实部署来验证线程正确性。
/// <para>
/// 注意：<b>集合结构（增删）不在这里做</b>。任务列表在编排器构造时一次性建好，
/// 之后整个部署期间只有元素属性变化 —— 元素是 <see cref="DeployTaskItem"/>（INPC），
/// 因此每次更新只需归线程的属性写入，不必重建集合。
/// </para>
/// </remarks>
public sealed class TaskListUpdater
{
    private readonly ObservableCollection<DeployTaskItem> _items;
    private readonly IUiDispatcher _dispatch;

    public TaskListUpdater(ObservableCollection<DeployTaskItem> items, IUiDispatcher dispatch)
    {
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
    }

    /// <summary>
    /// 应用一次任务更新。立即返回 —— 实际写入被投递到 UI 线程（保序）。
    /// </summary>
    public void Apply(TaskUpdate update)
    {
        _dispatch.Post(() =>
        {
            var task = Find(update.TaskId);
            if (task is null) return;   // 该步骤被跳过/不在列表中
            task.Status = update.Status;
            task.ProgressValue = update.Progress;
        });
    }

    private DeployTaskItem? Find(DeployTaskId id)
    {
        foreach (var t in _items)
        {
            if (string.Equals(t.Id, id.Value, StringComparison.Ordinal))
                return t;
        }
        return null;
    }
}