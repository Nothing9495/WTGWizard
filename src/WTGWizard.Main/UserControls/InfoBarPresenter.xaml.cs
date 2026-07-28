using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WTGWizard.Models;

namespace WTGWizard.UserControls;

public sealed partial class InfoBarPresenter : UserControl
{
    public static readonly DependencyProperty InfoProperty = DependencyProperty.Register(
        nameof(Info), typeof(InfoBarState), typeof(InfoBarPresenter),
        new PropertyMetadata(null, OnInfoChanged));

    public static readonly DependencyProperty InfoBarCornerRadiusProperty = DependencyProperty.Register(
        nameof(InfoBarCornerRadius), typeof(CornerRadius), typeof(InfoBarPresenter),
        new PropertyMetadata(null, OnStylePropertyChanged));

    public static readonly DependencyProperty InfoBarHorizontalAlignmentProperty = DependencyProperty.Register(
        nameof(InfoBarHorizontalAlignment), typeof(HorizontalAlignment), typeof(InfoBarPresenter),
        new PropertyMetadata(HorizontalAlignment.Stretch, OnStylePropertyChanged));

    public static readonly DependencyProperty InfoBarVerticalAlignmentProperty = DependencyProperty.Register(
        nameof(InfoBarVerticalAlignment), typeof(VerticalAlignment), typeof(InfoBarPresenter),
        new PropertyMetadata(VerticalAlignment.Stretch, OnStylePropertyChanged));

    public static readonly DependencyProperty InfoBarIsIconVisibleProperty = DependencyProperty.Register(
        nameof(InfoBarIsIconVisible), typeof(bool), typeof(InfoBarPresenter),
        new PropertyMetadata(true));

    public static readonly DependencyProperty InfoBarBorderThicknessProperty = DependencyProperty.Register(
        nameof(InfoBarBorderThickness), typeof(Thickness), typeof(InfoBarPresenter),
        new PropertyMetadata(null, OnStylePropertyChanged));

    public InfoBarPresenter()
    {
        InitializeComponent();
        ActionButton.Click += (_, _) => Info?.ActionClicked?.Invoke(this, EventArgs.Empty);
        UpdateActionButtonVisibility();
    }

    public InfoBarState? Info
    {
        get => (InfoBarState?)GetValue(InfoProperty);
        set => SetValue(InfoProperty, value);
    }

    public event EventHandler? Closed;

    public CornerRadius InfoBarCornerRadius
    {
        get => (CornerRadius)GetValue(InfoBarCornerRadiusProperty);
        set => SetValue(InfoBarCornerRadiusProperty, value);
    }

    public HorizontalAlignment InfoBarHorizontalAlignment
    {
        get => (HorizontalAlignment)GetValue(InfoBarHorizontalAlignmentProperty);
        set => SetValue(InfoBarHorizontalAlignmentProperty, value);
    }

    public VerticalAlignment InfoBarVerticalAlignment
    {
        get => (VerticalAlignment)GetValue(InfoBarVerticalAlignmentProperty);
        set => SetValue(InfoBarVerticalAlignmentProperty, value);
    }

    public bool InfoBarIsIconVisible
    {
        get => (bool)GetValue(InfoBarIsIconVisibleProperty);
        set => SetValue(InfoBarIsIconVisibleProperty, value);
    }

    public Thickness InfoBarBorderThickness
    {
        get => (Thickness)GetValue(InfoBarBorderThicknessProperty);
        set => SetValue(InfoBarBorderThicknessProperty, value);
    }

    private static void OnInfoChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((InfoBarPresenter)d).UpdateActionButtonVisibility();
    }

    private static void OnStylePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var presenter = (InfoBarPresenter)d;
        if (presenter.InnerInfoBar is null) return;

        if (e.Property == InfoBarCornerRadiusProperty)
            presenter.InnerInfoBar.CornerRadius = (CornerRadius)e.NewValue;
        else if (e.Property == InfoBarHorizontalAlignmentProperty)
            presenter.InnerInfoBar.HorizontalAlignment = (HorizontalAlignment)e.NewValue;
        else if (e.Property == InfoBarVerticalAlignmentProperty)
            presenter.InnerInfoBar.VerticalAlignment = (VerticalAlignment)e.NewValue;
        else if (e.Property == InfoBarBorderThicknessProperty)
            presenter.InnerInfoBar.BorderThickness = (Thickness)e.NewValue;
    }

    private void UpdateActionButtonVisibility()
    {
        if (ActionButton is null) return;
        var show = Info?.IsOpen == true && !string.IsNullOrEmpty(Info.ActionContent);
        ActionButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void InnerInfoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        // Info 为 null 或 Info.IsOpen 为 false → 程序化隐藏（Info=null 导致绑定传播 IsOpen=false），不转发
        if (Info?.IsOpen is not true)
            return;
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
