using System;

namespace WTGWizard.Main.DeploymentCore.Contracts;

/// <summary>
/// UI 线程投递器 —— 部署状态机运行在后台线程时，所有触碰 UI 对象（集合元素属性、
/// WinRT 窗口句柄、DispatcherTimer）的写入都必须经它回到 UI 线程。
/// </summary>
/// <remarks>
/// 契约：<list type="bullet">
/// <item>保序（FIFO）——投递顺序即执行顺序；</item>
/// <item>返回 false 表示 UI 线程已不可用（应用正在退出），调用方应静默放弃而非阻塞；</item>
/// <item>实现必须是线程安全的，调用方来自任意后台线程。</item>
/// </list>
/// </remarks>
public interface IUiDispatcher
{
    /// <summary>把动作投递到 UI 线程执行。返回 false 表示未能投递（UI 线程已关闭）。</summary>
    bool Post(Action action);
}