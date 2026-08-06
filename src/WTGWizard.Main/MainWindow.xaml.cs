using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Threading.Tasks;
using WTGWizard.Helpers;
using WTGWizard.Messages;
using WTGWizard.Pages;
using WTGWizard.ViewModels;

namespace WTGWizard.Main;

public sealed partial class MainWindow : Window
{
    private OverlappedPresenter? _windowPresenter;
    private OverlappedPresenterState _currentWindowState;
    private readonly WizardViewModel _vm;
    private ITabActivatable? _currentPage;
    private string _currentTag = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        SetWindowProperties();
        _vm = App.Services.GetRequiredService<WizardViewModel>();

        // 注册消息
        WeakReferenceMessenger.Default.Register<NavigateToPageMessage>(this, (_, msg) => NavigateToTag(msg.Tag));

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

        // 部署进行中关闭窗口：拦截 + 警告 + 强制终止（详见 OnAppWindowClosing）
        AppWindow.Closing += OnAppWindowClosing;

        // 响应式导航：窗口宽度 >= 1300 DIP 时使用 Left 模式，否则 Top
        RootGrid.SizeChanged += (_, e) => UpdateNavViewPaneMode(e.NewSize.Width);

        // 默认选中第一个 Tab
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private void SetWindowProperties()
    {
        //TitleBar样式及高度设置
        this.ExtendsContentIntoTitleBar = true;
        this.SetTitleBar(AppTitleBar);
        this.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        //标题栏图标与系统菜单由系统从窗口图标绘制
        this.AppWindow.TitleBar.IconShowOptions = IconShowOptions.ShowIconAndSystemMenu;
        //任务栏/Alt+Tab/任务栏缩略图图标（实验1：MAUI 方式 ExtractAssociatedIcon + SetIcon(IconId)）
        WindowHelper.SetIcon(this);
        //标题栏图标（从 exe 内嵌资源加载）
        _ = SetTitleBarIconAsync();
    }

    private async Task SetTitleBarIconAsync()
    {
        try
        {
            using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("WTGWizard.Assets.AppLogo.ico");
            if (stream is null)
                return;
            using var memStream = new MemoryStream();
            await stream.CopyToAsync(memStream);
            memStream.Position = 0;
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(memStream.AsRandomAccessStream());
            AppTitleBar.IconSource = new ImageIconSource { ImageSource = bitmap };
        }
        catch { }
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

    private void UpdateNavViewPaneMode(double width)
    {
        var breakpoint = (double)Application.Current.Resources["NavViewPaneBreakpoint"];
        var mode = width >= breakpoint
            ? NavigationViewPaneDisplayMode.Left
            : NavigationViewPaneDisplayMode.Top;
        var paneToggle = width >= breakpoint
            ? true : false;
        if (NavView.PaneDisplayMode == mode) return;
        NavView.PaneDisplayMode = mode;
        NavView.IsPaneToggleButtonVisible = paneToggle;
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        TitleBarHelper.ApplySystemThemeToCaptionButtons(this, RootGrid.ActualTheme);
        // Window 样式控制
        WindowHelper.SetWindowSize(this, 1150, 800);
        WindowHelper.SetWindowMinSize(this, 970, 680);
        UpdateNavViewPaneMode(RootGrid.ActualWidth);
#if DEBUG
        ShowDebugBuildWarning();
#endif
    }

    /// <summary>
    /// 调试版本启动警告：仅 DEBUG 构建编译，提示用户当前为测试版本。
    /// </summary>
    private async void ShowDebugBuildWarning()
    {
        var dialog = new ContentDialog
        {
            Title = WTGWizard.Main.Language.Lang.App_Dialog_DebugBuild_Title,
            Content = WTGWizard.Main.Language.Lang.App_Dialog_DebugBuild_ContentText,
            CloseButtonText = WTGWizard.Main.Language.Lang.App_Dialog_DebugBuild_CloseButtonText,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot,
            RequestedTheme = RootGrid.ActualTheme
        };
        await dialog.ShowAsync();
    }


    /// <summary>
    /// 部署进行中关闭窗口：拦截首次关闭 → 警告对话框 → 确认后强制终止部署 → 二次关闭放行。
    /// </summary>
    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_vm.IsDeploying) return;
        args.Cancel = true;

        var dialog = new ContentDialog
        {
            Title = WTGWizard.Main.Language.Lang.App_Dialog_CloseWhileDeploying_Title,
            Content = WTGWizard.Main.Language.Lang.App_Dialog_CloseWhileDeploying_ContentText,
            PrimaryButtonText = WTGWizard.Main.Language.Lang.App_Dialog_CloseWhileDeploying_PrimaryButtonText,
            CloseButtonText = WTGWizard.Main.Language.Lang.App_Dialog_CloseWhileDeploying_CloseButtonText,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot,
            RequestedTheme = RootGrid.ActualTheme
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        if (_vm.StopDeploymentForClose is { } stop)
            await stop();

        Close();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        // 通知当前页面 Tab 切出
        _currentPage?.OnTabDeactivated();

        var transition = args.RecommendedNavigationTransitionInfo;

        // 处理设置页（齿轮图标）
        if (args.IsSettingsSelected)
        {
            if (_currentTag == "settings") return;
            _currentTag = "settings";
            RootFrame.Navigate(typeof(SettingsPage), null, transition);
            _currentPage = null;
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
                    _currentPage = null;
                    break;
                case "WizardPage":
                    RootFrame.Navigate(typeof(WizardPage), _vm, transition);
                    _currentPage = RootFrame.Content as ITabActivatable;
                    break;
                case "TaskPage":
                    RootFrame.Navigate(typeof(TaskPage), _vm, transition);
                    _currentPage = RootFrame.Content as ITabActivatable;
                    break;
            }

            // 通知新页面 Tab 切入
            _currentPage?.OnTabActivated();
        }
    }

    public void NavigateToTag(string tag)
    {
        foreach (var item in NavView.MenuItems)
        {
            if (item is NavigationViewItem navItem && navItem.Tag is string t && t == tag)
            {
                NavView.SelectedItem = navItem;
                break;
            }
        }
    }
}
