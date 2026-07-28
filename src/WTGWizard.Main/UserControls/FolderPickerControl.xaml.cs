using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace WTGWizard.UserControls;

/// <summary>
/// 可复用的文件夹选择器控件，封装 FolderPicker 的逻辑。
/// </summary>
public sealed partial class FolderPickerControl : UserControl
{
    public static readonly DependencyProperty ButtonTextProperty =
        DependencyProperty.Register(nameof(ButtonText), typeof(string),
            typeof(FolderPickerControl), new PropertyMetadata(string.Empty, OnButtonTextChanged));

    public static readonly DependencyProperty ButtonContentProperty =
        DependencyProperty.Register(nameof(ButtonContent), typeof(object),
            typeof(FolderPickerControl), new PropertyMetadata(null, OnButtonContentChanged));

    public static readonly DependencyProperty CommitButtonTextProperty =
        DependencyProperty.Register(nameof(CommitButtonText), typeof(string),
            typeof(FolderPickerControl), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SuggestedStartLocationProperty =
        DependencyProperty.Register(nameof(SuggestedStartLocation), typeof(PickerLocationId),
            typeof(FolderPickerControl), new PropertyMetadata(PickerLocationId.ComputerFolder));

    public static readonly DependencyProperty SelectedFolderPathProperty =
        DependencyProperty.Register(nameof(SelectedFolderPath), typeof(string),
            typeof(FolderPickerControl), new PropertyMetadata(string.Empty));

    public string ButtonText
    {
        get => (string)GetValue(ButtonTextProperty);
        set => SetValue(ButtonTextProperty, value);
    }

    public object ButtonContent
    {
        get => GetValue(ButtonContentProperty);
        set => SetValue(ButtonContentProperty, value);
    }

    public string CommitButtonText
    {
        get => (string)GetValue(CommitButtonTextProperty);
        set => SetValue(CommitButtonTextProperty, value);
    }

    public PickerLocationId SuggestedStartLocation
    {
        get => (PickerLocationId)GetValue(SuggestedStartLocationProperty);
        set => SetValue(SuggestedStartLocationProperty, value);
    }

    public string SelectedFolderPath
    {
        get => (string)GetValue(SelectedFolderPathProperty);
        set => SetValue(SelectedFolderPathProperty, value);
    }

    public event EventHandler<string>? FolderSelected;

    private static void OnButtonTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FolderPickerControl ctrl && e.NewValue is string text && ctrl.ButtonContent is null)
            ctrl.PickFolderButton.Content = text;
    }

    private static void OnButtonContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FolderPickerControl ctrl && e.NewValue is object content)
            ctrl.PickFolderButton.Content = content;
    }

    private void UpdateButtonContent()
    {
        PickFolderButton.Content = ButtonContent ?? (object?)ButtonText ?? "Select Folder";
    }

    public FolderPickerControl()
    {
        InitializeComponent();
        UpdateButtonContent();
    }

    private async void PickFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        button.IsEnabled = false;

        try
        {
            var picker = new FolderPicker(button.XamlRoot.ContentIslandEnvironment.AppWindowId)
            {
                CommitButtonText = CommitButtonText,
                SuggestedStartLocation = (PickerLocationId)SuggestedStartLocation,
                ViewMode = PickerViewMode.List
            };

            var result = await picker.PickSingleFolderAsync();
            if (result is not null)
            {
                SelectedFolderPath = result.Path;
                FolderSelected?.Invoke(this, result.Path);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FolderPicker] 操作失败: 0x{ex.HResult:X8} {ex.Message}");
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    public void ClearSelection()
    {
        SelectedFolderPath = string.Empty;
    }
}
