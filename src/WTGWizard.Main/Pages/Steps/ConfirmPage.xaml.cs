using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WTGWizard.Main;
using WTGWizard.Main.Language;
using WTGWizard.ViewModels;

namespace WTGWizard.Pages.Steps;

public sealed partial class ConfirmPage : Page, ITabActivatable
{
    public WizardViewModel VM { get; private set; } = null!;
    public ConfirmVM Display { get; private set; } = null!;

    public ConfirmPage()
    {
        VM = App.Services.GetRequiredService<WizardViewModel>();
        Display = new ConfirmVM(VM);
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

    private async void StartDeploy_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = Lang.Page_WizStep_Confirm_DeployDialog_Title,
            Content = Lang.Page_WizStep_Confirm_DeployDialog_ContentText,
            PrimaryButtonText = Lang.Page_WizStep_Confirm_DeployDialog_PrimaryButtonText,
            CloseButtonText = Lang.Page_WizStep_Confirm_DeployDialog_CloseButtonText,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            VM.StartDeployCommand.Execute(null);
        }
    }
}
