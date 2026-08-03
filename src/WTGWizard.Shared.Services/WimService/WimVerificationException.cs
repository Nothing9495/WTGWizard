using System;

namespace WTGWizard.Shared.Services.WimService;

/// <summary>
/// 映像校验未通过异常 — VerifyWim 阶段失败（映像内容损坏等），
/// 区别于打开失败（WimLibException）与其他未知异常。
/// </summary>
public sealed class WimVerificationException : Exception
{
    public WimVerificationException(string message, Exception inner)
        : base(message, inner) { }
}
