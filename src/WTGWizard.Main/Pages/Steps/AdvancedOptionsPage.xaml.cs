using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.Storage.Pickers;
using WTGWizard.Main;
using WTGWizard.Shared.Services.Wim;
using WTGWizard.ViewModels;

namespace WTGWizard.Pages.Steps;

public sealed partial class AdvancedOptionsPage : Page, ITabActivatable
{
    private readonly IWimService _wimService = App.Services.GetRequiredService<IWimService>();
    public WizardViewModel VM { get; private set; } = null!;

    public AdvancedOptionsPage()
    {
        VM = App.Services.GetRequiredService<WizardViewModel>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is WizardViewModel vm)
        {
            VM = vm;
            DataContext = VM;
        }
    }

    public void OnTabActivated() { }
    public void OnTabDeactivated() { }

    // ═══ 事件处理 ═══

    private void OnDriverFolderSelected(object sender, string path)
    {
        VM.Advanced.DriverPath = path;
    }

    private void OnAnsFileSelected(object sender, string path)
    {
        VM.Advanced.AnsFilePath = path;
    }

    private async void ExtractImageAnsFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        if (string.IsNullOrEmpty(VM?.Image?.FilePath))
            return;

        var foundPaths = VM.Image.AnsFileFoundPaths;
        var foundPath = foundPaths.Count > 0
            ? foundPaths[0]
            : @"\Windows\Panther\unattend.xml";

        button.IsEnabled = false;
        try
        {
            var selectedIndex = VM.Image.SelectedIndex >= 0
                && VM.Image.SelectedIndex < VM.Image.Indices.Count
                && int.TryParse(VM.Image.Indices[VM.Image.SelectedIndex], out var parsedIdx)
                ? parsedIdx
                : 1;

            var picker = new FileSavePicker(button.XamlRoot.ContentIslandEnvironment.AppWindowId)
            {
                SuggestedStartLocation = PickerLocationId.Desktop,
                SuggestedFileName = $"unattend_Index{selectedIndex}.xml",
                CommitButtonText = Lang.Page_WizStep_AdvOptions_Extract_CommitText,
            };
            picker.FileTypeChoices.Add("XML Files", new System.Collections.Generic.List<string> { ".xml" });
            picker.DefaultFileExtension = ".xml";

            var result = await picker.PickSaveFileAsync();
            if (result is null) return;

            await Task.Run(async () =>
            {
                await _wimService.ExtractFileAsync(
                    VM.Image.FilePath,
                    selectedIndex,
                    foundPath,
                    result.Path);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AnsFile] 提取失败: {ex.Message}");
        }
        finally
        {
            button.IsEnabled = true;
        }
    }
}
