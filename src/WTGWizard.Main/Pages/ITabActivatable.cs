namespace WTGWizard.Pages;

/// <summary>
/// Tab 切换生命周期接口。
/// MainWindow 在 Tab 切换时调用这些方法。
/// </summary>
public interface ITabActivatable
{
    /// <summary>Tab 切入时调用。</summary>
    void OnTabActivated();

    /// <summary>Tab 切出时调用。</summary>
    void OnTabDeactivated();
}
