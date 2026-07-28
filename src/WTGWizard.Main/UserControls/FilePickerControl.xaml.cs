using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace WTGWizard.UserControls;

/// <summary>
/// 可复用的文件选择器控件，封装 FileOpenPicker 的逻辑。
/// </summary>
public sealed partial class FilePickerControl : UserControl
{
    public static readonly DependencyProperty ButtonTextProperty =
        DependencyProperty.Register(nameof(ButtonText), typeof(string),
            typeof(FilePickerControl), new PropertyMetadata(string.Empty, OnButtonTextChanged));

    public static readonly DependencyProperty ButtonContentProperty =
        DependencyProperty.Register(nameof(ButtonContent), typeof(object),
            typeof(FilePickerControl), new PropertyMetadata(null, OnButtonContentChanged));

    public static readonly DependencyProperty CommitButtonTextProperty =
        DependencyProperty.Register(nameof(CommitButtonText), typeof(string),
            typeof(FilePickerControl), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty FileTypeFilterProperty =
        DependencyProperty.Register(nameof(FileTypeFilter), typeof(string),
            typeof(FilePickerControl), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SuggestedStartLocationProperty =
        DependencyProperty.Register(nameof(SuggestedStartLocation), typeof(PickerLocationId),
            typeof(FilePickerControl), new PropertyMetadata(PickerLocationId.ComputerFolder));

    public static readonly DependencyProperty SelectedFilePathProperty =
        DependencyProperty.Register(nameof(SelectedFilePath), typeof(string),
            typeof(FilePickerControl), new PropertyMetadata(string.Empty));

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

    public string FileTypeFilter
    {
        get => (string)GetValue(FileTypeFilterProperty);
        set => SetValue(FileTypeFilterProperty, value);
    }

    public PickerLocationId SuggestedStartLocation
    {
        get => (PickerLocationId)GetValue(SuggestedStartLocationProperty);
        set => SetValue(SuggestedStartLocationProperty, value);
    }

    public string SelectedFilePath
    {
        get => (string)GetValue(SelectedFilePathProperty);
        set => SetValue(SelectedFilePathProperty, value);
    }

    public event EventHandler<string>? FileSelected;

    private static void OnButtonTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FilePickerControl ctrl && e.NewValue is string text && ctrl.ButtonContent is null)
            ctrl.PickFileButton.Content = text;
    }

    private static void OnButtonContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FilePickerControl ctrl && e.NewValue is object content)
            ctrl.PickFileButton.Content = content;
    }

    private void UpdateButtonContent()
    {
        PickFileButton.Content = ButtonContent ?? (object?)ButtonText ?? "Select File";
    }

    public FilePickerControl()
    {
        InitializeComponent();
        UpdateButtonContent();
    }

    private async void PickFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.IsEnabled = false;

            try
            {
                var picker = new FileOpenPicker(button.XamlRoot.ContentIslandEnvironment.AppWindowId)
                {
                    CommitButtonText = CommitButtonText,
                    SuggestedStartLocation = (PickerLocationId)SuggestedStartLocation,
                    ViewMode = PickerViewMode.List
                };

                if (!string.IsNullOrWhiteSpace(FileTypeFilter))
                {
                    foreach (var ext in FileTypeFilter.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        picker.FileTypeFilter.Add(ext.StartsWith('.') ? ext : "." + ext);
                }
                else
                {
                    picker.FileTypeFilter.Add("*");
                }

                var file = await picker.PickSingleFileAsync();
                if (file is not null)
                {
                    SelectedFilePath = file.Path;
                    FileSelected?.Invoke(this, file.Path);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FilePicker] 操作失败: 0x{ex.HResult:X8} {ex.Message}");
            }

            button.IsEnabled = true;
        }
    }

    public void ClearSelection()
    {
        SelectedFilePath = string.Empty;
    }
}
