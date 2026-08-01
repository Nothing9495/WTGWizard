using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WTGWizard.Main.DeploymentCore.Models;

namespace WTGWizard.UserControls;

public sealed partial class TaskContentCard : UserControl, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _isPointerInside;

    public TaskContentCard()
    {
        InitializeComponent();
        ActualThemeChanged += OnActualThemeChanged;
    }

    #region 依赖属性

    public new CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    // 1. 标题
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(TaskContentCard), new PropertyMetadata(string.Empty));
    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    // 2. 描述
    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(TaskContentCard), new PropertyMetadata(string.Empty));
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    // 3. 任务状态
    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(nameof(Status), typeof(DeployTaskStatus), typeof(TaskContentCard),
            new PropertyMetadata(DeployTaskStatus.Pending, OnStatusChanged));
    public DeployTaskStatus Status
    {
        get => (DeployTaskStatus)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    // 4. 进度值 (0-100)
    public static readonly DependencyProperty ProgressValueProperty =
        DependencyProperty.Register(nameof(ProgressValue), typeof(double), typeof(TaskContentCard),
            new PropertyMetadata(0.0, OnProgressValueChanged));
    public double ProgressValue
    {
        get => (double)GetValue(ProgressValueProperty);
        set => SetValue(ProgressValueProperty, value);
    }

    // 5. 自定义高亮画刷
    public static readonly DependencyProperty HoverBrushProperty =
        DependencyProperty.Register(nameof(HoverBrush), typeof(Brush), typeof(TaskContentCard), new PropertyMetadata(null));
    public Brush? HoverBrush
    {
        get => (Brush?)GetValue(HoverBrushProperty);
        set => SetValue(HoverBrushProperty, value);
    }

    #endregion

    #region 状态可见性（内部控制，不暴露为依赖属性）

    private bool _isRunning;
    private bool _isIndeterminateMode;
    private Visibility _showCompletedIcon = Visibility.Collapsed;
    private Visibility _showFailedIcon = Visibility.Collapsed;

    public bool IsRunning => _isRunning;
    public bool IsIndeterminateMode => _isIndeterminateMode;
    public Visibility ShowCompletedIcon => _showCompletedIcon;
    public Visibility ShowFailedIcon => _showFailedIcon;

    private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((TaskContentCard)d).UpdateStateVisibility();
    }

    private static void OnProgressValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((TaskContentCard)d).UpdateStateVisibility();
    }

    private void UpdateStateVisibility()
    {
        var isRunning = Status == DeployTaskStatus.Running;
        var hasProgress = ProgressValue > 0;

        SetBool(ref _isRunning, nameof(IsRunning), isRunning);
        SetBool(ref _isIndeterminateMode, nameof(IsIndeterminateMode), isRunning && !hasProgress);
        SetVisibility(ref _showCompletedIcon, nameof(ShowCompletedIcon),
            Status == DeployTaskStatus.Completed ? Visibility.Visible : Visibility.Collapsed);
        SetVisibility(ref _showFailedIcon, nameof(ShowFailedIcon),
            Status == DeployTaskStatus.Failed ? Visibility.Visible : Visibility.Collapsed);
    }

    private void SetBool(ref bool field, string propertyName, bool value)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new(propertyName));
    }

    private void SetVisibility(ref Visibility field, string propertyName, Visibility value)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new(propertyName));
    }

    #endregion

    #region 鼠标悬浮高亮

    private void OnPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _isPointerInside = true;
        UpdateHoverOverlay();
    }

    private void OnPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _isPointerInside = false;
        HoverOverlay.Background = new SolidColorBrush(Colors.Transparent);
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        if (_isPointerInside)
            UpdateHoverOverlay();
    }

    private void UpdateHoverOverlay()
    {
        if (HoverBrush is not null)
        {
            HoverOverlay.Background = HoverBrush;
            return;
        }

        var theme = ActualTheme;
        if (theme == ElementTheme.Light)
            HoverOverlay.Background = new SolidColorBrush(Color.FromArgb(4, 0, 0, 0));
        else
            HoverOverlay.Background = new SolidColorBrush(Color.FromArgb(9, 255, 255, 255));
    }

    #endregion
}
