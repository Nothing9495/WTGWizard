using System;
using System.Globalization;
using System.IO;

namespace WTGWizard.Worker.Encoding;

/// <summary>
/// 外部程序输出编码解析器 — 按可执行文件名解析解码编码。
/// Worker 内部统一使用 Unicode string，编码只在此处管理。
/// </summary>
internal static class EncodingResolver
{
    private static readonly System.Text.Encoding SystemEncoding;

    static EncodingResolver()
    {
        // .NET Core 默认不含代码页编码（GBK/437/932 等），必须注册
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        // 系统 OEM 代码页，自动适配运行系统语言：中文 936 / 英文 437 / 日文 932
        SystemEncoding = System.Text.Encoding.GetEncoding(
            CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
    }

    /// <summary>
    /// 解析外部程序的输出编码。
    /// </summary>
    /// <param name="fileName">可执行文件路径或名称。</param>
    public static System.Text.Encoding Resolve(string fileName)
    {
        string name = Path.GetFileName(fileName).ToLowerInvariant();

        return name switch
        {
            // PowerShell 脚本已强制 [Console]::OutputEncoding=UTF8
            // （见 DiskScriptBuilder.WritePreamble），任何语言系统输出一致
            "powershell.exe" or "pwsh.exe" => System.Text.Encoding.UTF8,

            // DISM / BCDBoot 等系统组件：输出使用系统代码页
            _ => SystemEncoding
        };
    }
}
