using System;
using WTGWizard.Main.DeploymentCore.Contracts;

namespace WTGWizard.Main.DeploymentCore.Orchestrator;

/// <summary>
/// 无 UI 场景的 <see cref="IUiDispatcher"/> 退化实现：同步就地执行。
/// 仅在编排器未注入真实调度器时启用（此时通常没有 UI 绑定任务列表）。
/// </summary>
internal sealed class InlineUiDispatcher : IUiDispatcher
{
    public bool Post(Action action)
    {
        action();
        return true;
    }
}