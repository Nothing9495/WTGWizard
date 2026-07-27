using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WTGWizard.Messages;

namespace WTGWizard.ViewModels;

/// <summary>
/// Wizard 协调器 VM — 步骤导航 + 全局状态容器。
/// </summary>
public sealed partial class WizardViewModel : ObservableObject
{
    // ═══ 步骤导航 ═══

    [ObservableProperty] public partial int CurrentStep { get; set; }
    [ObservableProperty] public partial bool IsDeploying { get; set; }

    public bool CanGoBack => CurrentStep > 0 && !IsDeploying;
    public bool CanGoForward => CurrentStep < MaxStep && IsCurrentStepValid;
    public int MaxStep => 4; // 0-4, 共 5 步

    // ═══ 状态子对象 ═══

    public ImageConfig Image { get; } = new();
    public DeployOptions Options { get; } = new();
    public DeployMethod Method { get; }
    public AdvancedOptions Advanced { get; } = new();

    // ═══ 构造函数 ═══

    public WizardViewModel()
    {
        Method = new DeployMethod();
        Image.PropertyChanged += OnSubPropertyChanged;
        Method.PropertyChanged += OnSubPropertyChanged;
        Advanced.PropertyChanged += OnSubPropertyChanged;
    }

    private void OnSubPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "IsValid")
            OnPropertyChanged(nameof(IsCurrentStepValid));
    }

    // ═══ 派生属性 ═══

    public bool IsCurrentStepValid => CurrentStep switch
    {
        0 => Image.IsValid,
        1 => Method.IsValid,
        2 => true,
        3 => Advanced.IsValid,
        4 => true,
        _ => false
    };

    // ═══ 命令 ═══

    [RelayCommand]
    private void GoBack()
    {
        if (CanGoBack)
            CurrentStep--;
    }

    [RelayCommand]
    private void GoForward()
    {
        if (CanGoForward)
            CurrentStep++;
    }

    [RelayCommand]
    private void Reset()
    {
        CurrentStep = 0;
    }

    /// <summary>
    /// 请求导航到任务执行页（由 MainWindow 处理）。
    /// </summary>
    public event Action? NavigateToTaskRequested;

    [RelayCommand]
    private void StartDeploy()
    {
        // TODO: 创建 DeploymentOrchestrator
        IsDeploying = true;
        NavigateToTaskRequested?.Invoke();
    }
}
