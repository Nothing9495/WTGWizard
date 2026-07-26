using System;

namespace WTGWizard.Worker.Commands;

/// <summary>
/// 命令参数解析辅助类。
/// </summary>
internal static class CommandArgs
{
    /// <summary>
    /// 获取必需参数。
    /// </summary>
    public static string GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        throw new ArgumentException($"Missing required argument: {name}");
    }

    /// <summary>
    /// 获取可选参数。
    /// </summary>
    public static string? TryGetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }
}
