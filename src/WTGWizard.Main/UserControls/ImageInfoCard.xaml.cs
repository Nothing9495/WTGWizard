using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WTGWizard.Main.Language;

namespace WTGWizard.UserControls;

/// <summary>
/// 映像信息卡片 — 固定双列布局（Logo 左、信息右）。
/// </summary>
public sealed partial class ImageInfoCard : UserControl
{
    public ImageInfoCard()
    {
        InitializeComponent();
    }

    // ================================================================
    //   Dependency Properties
    // ================================================================

    public static readonly DependencyProperty LogoSourceProperty =
        DependencyProperty.Register(nameof(LogoSource), typeof(ImageSource), typeof(ImageInfoCard), null);
    public ImageSource LogoSource
    {
        get => (ImageSource)GetValue(LogoSourceProperty);
        set => SetValue(LogoSourceProperty, value);
    }

    public static readonly DependencyProperty LogoVisibleProperty =
        DependencyProperty.Register(nameof(LogoVisible), typeof(Visibility), typeof(ImageInfoCard),
            new PropertyMetadata(Visibility.Collapsed));
    public Visibility LogoVisible
    {
        get => (Visibility)GetValue(LogoVisibleProperty);
        set => SetValue(LogoVisibleProperty, value);
    }

    public static readonly DependencyProperty LoadingVisibleProperty =
        DependencyProperty.Register(nameof(LoadingVisible), typeof(Visibility), typeof(ImageInfoCard),
            new PropertyMetadata(Visibility.Collapsed));
    public Visibility LoadingVisible
    {
        get => (Visibility)GetValue(LoadingVisibleProperty);
        set => SetValue(LoadingVisibleProperty, value);
    }

    public static readonly DependencyProperty IsLoadingProperty =
        DependencyProperty.Register(nameof(IsLoading), typeof(bool), typeof(ImageInfoCard),
            new PropertyMetadata(false));
    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public static readonly DependencyProperty MajorVersionProperty =
        DependencyProperty.Register(nameof(MajorVersion), typeof(string), typeof(ImageInfoCard), new PropertyMetadata("-"));
    public string MajorVersion
    {
        get => (string)GetValue(MajorVersionProperty);
        set => SetValue(MajorVersionProperty, value);
    }

    public static readonly DependencyProperty ImageIndexProperty =
        DependencyProperty.Register(nameof(ImageIndex), typeof(string), typeof(ImageInfoCard), new PropertyMetadata("-"));
    public string ImageIndex
    {
        get => (string)GetValue(ImageIndexProperty);
        set => SetValue(ImageIndexProperty, value);
    }

    public static readonly DependencyProperty ImageVersionProperty =
        DependencyProperty.Register(nameof(ImageVersion), typeof(string), typeof(ImageInfoCard), new PropertyMetadata("-"));
    public string ImageVersion
    {
        get => (string)GetValue(ImageVersionProperty);
        set => SetValue(ImageVersionProperty, value);
    }

    public static readonly DependencyProperty FeatureUpdateProperty =
        DependencyProperty.Register(nameof(FeatureUpdate), typeof(string), typeof(ImageInfoCard), new PropertyMetadata("-"));
    public string FeatureUpdate
    {
        get => (string)GetValue(FeatureUpdateProperty);
        set => SetValue(FeatureUpdateProperty, value);
    }

    public static readonly DependencyProperty ArchitectureProperty =
        DependencyProperty.Register(nameof(Architecture), typeof(string), typeof(ImageInfoCard), new PropertyMetadata("-"));
    public string Architecture
    {
        get => (string)GetValue(ArchitectureProperty);
        set => SetValue(ArchitectureProperty, value);
    }

    public static readonly DependencyProperty BuildNumberProperty =
        DependencyProperty.Register(nameof(BuildNumber), typeof(string), typeof(ImageInfoCard), new PropertyMetadata("-"));
    public string BuildNumber
    {
        get => (string)GetValue(BuildNumberProperty);
        set => SetValue(BuildNumberProperty, value);
    }

    public static readonly DependencyProperty ExpandedSizeProperty =
        DependencyProperty.Register(nameof(ExpandedSize), typeof(string), typeof(ImageInfoCard), new PropertyMetadata("-"));
    public string ExpandedSize
    {
        get => (string)GetValue(ExpandedSizeProperty);
        set => SetValue(ExpandedSizeProperty, value);
    }

    public static readonly DependencyProperty DateCreatedProperty =
        DependencyProperty.Register(nameof(DateCreated), typeof(string), typeof(ImageInfoCard), new PropertyMetadata("-"));
    public string DateCreated
    {
        get => (string)GetValue(DateCreatedProperty);
        set => SetValue(DateCreatedProperty, value);
    }

    public static readonly DependencyProperty ImageNameProperty =
        DependencyProperty.Register(nameof(ImageName), typeof(string), typeof(ImageInfoCard), new PropertyMetadata("-"));
    public string ImageName
    {
        get => (string)GetValue(ImageNameProperty);
        set => SetValue(ImageNameProperty, value);
    }

    public static readonly DependencyProperty ImageDescriptionProperty =
        DependencyProperty.Register(nameof(ImageDescription), typeof(string), typeof(ImageInfoCard), new PropertyMetadata("-"));
    public string ImageDescription
    {
        get => (string)GetValue(ImageDescriptionProperty);
        set => SetValue(ImageDescriptionProperty, value);
    }

    public static readonly DependencyProperty DisplayDescriptionProperty =
        DependencyProperty.Register(nameof(DisplayDescription), typeof(string), typeof(ImageInfoCard), new PropertyMetadata("-"));
    public string DisplayDescription
    {
        get => (string)GetValue(DisplayDescriptionProperty);
        set => SetValue(DisplayDescriptionProperty, value);
    }

}
