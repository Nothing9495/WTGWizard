using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using WTGWizard.ViewModels;

namespace WTGWizard.Pages;

/// <summary>
/// Wizard 宿主 — Frame 导航切换步骤页面。
/// </summary>
public sealed partial class WizardHost : UserControl
{
    private static readonly Type[] StepTypes =
    [
        typeof(Steps.ImageConfigPage),
        typeof(Steps.DeployMethodPage),
        typeof(Steps.DeployOptionsPage),
        typeof(Steps.AdvancedOptionsPage),
        // typeof(Steps.Step5_Confirm),
    ];

    private static readonly string[] StepResourceKeys =
    [
        "Page.WizStep.ImageConfig.Title",
        "Page.WizStep.DeployMethod.Title",
        "Page.WizStep.DeployOptions.Title",
        "Page.WizStep.AdvOptions.Title",
        // "Page.WizStep.Confirm.Title",
    ];

    private int _lastStep = -1;
    private bool _suppressNextStepTransition;

    public WizardViewModel VM { get; }

    public WizardHost(WizardViewModel vm)
    {
        VM = vm;
        InitializeComponent();
        VM.PropertyChanged += OnVmPropertyChanged;
        NavigateToStep(VM.CurrentStep);
        UpdateButtons();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        VM.GoBackCommand.Execute(null);
        NavigateToStep(VM.CurrentStep);
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        VM.GoForwardCommand.Execute(null);
        NavigateToStep(VM.CurrentStep);
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WizardViewModel.CanGoBack)
            or nameof(WizardViewModel.CanGoForward))
        {
            UpdateButtons();
        }
    }

    private void UpdateButtons()
    {
        BackButton.IsEnabled = VM.CanGoBack;
        ForwardButton.IsEnabled = VM.CanGoForward;
    }

    private void NavigateToStep(int step)
    {
        if (step < 0 || step >= StepTypes.Length)
            return;

        NavigationTransitionInfo transitionInfo;

        if (_suppressNextStepTransition)
        {
            transitionInfo = new SuppressNavigationTransitionInfo();
            _suppressNextStepTransition = false;
        }
        else
        {
            var effect = _lastStep < step
                ? SlideNavigationTransitionEffect.FromRight
                : SlideNavigationTransitionEffect.FromLeft;
            transitionInfo = new SlideNavigationTransitionInfo { Effect = effect };
        }

        StepFrame.Navigate(StepTypes[step], VM, transitionInfo);
        StepFrame.BackStack.Clear();
        _lastStep = step;

        // 更新步骤标题（ViewModel 提供原始数据）
        VM.UpdateStepTitle(StepResourceKeys);
    }

    /// <summary>Tab 切入时调用，转发给当前步骤页面。</summary>
    public void OnTabActivated()
    {
        _suppressNextStepTransition = true;
        NavigateToStep(_lastStep);
        if (StepFrame.Content is ITabActivatable page)
            page.OnTabActivated();
    }

    /// <summary>Tab 切出时调用，转发给当前步骤页面。</summary>
    public void OnTabDeactivated()
    {
        if (StepFrame.Content is ITabActivatable page)
            page.OnTabDeactivated();
    }

    /// <summary>
    /// 格式化指示器文本：StepTitle (current/total)
    /// </summary>
    public static string FormatStepIndicator(string stepTitle, int currentStep, int totalSteps)
    {
        return $"{stepTitle} ({currentStep + 1}/{totalSteps})";
    }
}
