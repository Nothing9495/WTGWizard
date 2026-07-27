using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using WTGWizard.Helpers;
using WTGWizard.Pages;

namespace WTGWizard.Main;

public sealed partial class MainWindow : Window
{
    private OverlappedPresenter? _windowPresenter;
    private OverlappedPresenterState _currentWindowState;
    private string _currentTag = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        SetWindowProperties();

        // 标题栏按钮主题适配
        RootGrid.ActualThemeChanged += (_, _) =>
            TitleBarHelper.ApplySystemThemeToCaptionButtons(this, RootGrid.ActualTheme);

        // 修复 WinUI Issue #9934：最大化时标题栏与导航面板之间的 1px 缝隙
        if (AppWindow.Presenter is OverlappedPresenter windowPresenter)
        {
            _windowPresenter = windowPresenter;
            _currentWindowState = windowPresenter.State;
            AdjustNavigationViewMargin(force: true);
            AppWindow.Changed += (_, _) => AdjustNavigationViewMargin();
        }

        // 默认选中第一个 Tab
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private void SetWindowProperties()
    {
        this.ExtendsContentIntoTitleBar = true;
        this.SetTitleBar(AppTitleBar);
        this.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
    }

    private void AdjustNavigationViewMargin(bool? force = null)
    {
        if (_windowPresenter is null ||
            (_windowPresenter.State == _currentWindowState && force is not true))
            return;

        NavView.Margin = _windowPresenter.State == OverlappedPresenterState.Maximized
            ? new Thickness(0, -1, 0, 0)
            : new Thickness(0, -2, 0, 0);
        _currentWindowState = _windowPresenter.State;
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        TitleBarHelper.ApplySystemThemeToCaptionButtons(this, RootGrid.ActualTheme);
        WindowHelper.SetWindowSize(this, 1100, 740);
        WindowHelper.SetWindowMinSize(this, 1100, 680);
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        // 获取推荐的过渡动画
        var transition = args.RecommendedNavigationTransitionInfo;

        // 处理设置页（齿轮图标）
        if (args.IsSettingsSelected)
        {
            if (_currentTag == "settings") return;
            _currentTag = "settings";
            RootFrame.Navigate(typeof(SettingsPage), null, transition);
            return;
        }

        // 处理菜单项
        if (args.SelectedItemContainer is NavigationViewItem item && item.Tag is string tag)
        {
            if (tag == _currentTag) return;

            _currentTag = tag;

            switch (tag)
            {
                case "WelcomePage":
                    RootFrame.Navigate(typeof(WelcomePage), null, transition);
                    break;
                case "WizardPage":
                    RootFrame.Navigate(typeof(WizardPage), null, transition);
                    break;
                case "TaskPage":
                    RootFrame.Navigate(typeof(TaskPage), null, transition);
                    break;
            }
        }
    }
}
