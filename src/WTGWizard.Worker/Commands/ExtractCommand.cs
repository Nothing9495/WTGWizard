using System;

namespace WTGWizard.Worker.Commands;

/// <summary>
/// Extract 命令 — WIM 镜像提取。
/// 占位空代码，等待 Shared.Services 实现后接入。
/// </summary>
internal static class ExtractCommand
{
    /// <summary>
    /// 执行 WIM 提取命令。
    /// </summary>
    /// <param name="args">命令参数（--wim, --index, --target, --pipe）。</param>
    /// <returns>退出码。</returns>
    public static int Run(string[] args)
    {
        // TODO: 等待 Shared.Services 的 WimService 实现后接入
        throw new NotImplementedException("ExtractCommand is not implemented yet.");
    }
}
