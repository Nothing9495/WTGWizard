namespace WTGWizard.Messages;

/// <summary>
/// 统一的页面导航消息。
/// </summary>
public sealed class NavigateToPageMessage
{
    public string Tag { get; }

    public NavigateToPageMessage(string tag)
    {
        Tag = tag;
    }
}
