using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WTGWizard.Main;
using WTGWizard.ViewModels;

namespace WTGWizard.Pages.Steps;

public sealed partial class DeployOptionsPage : Page, ITabActivatable
{
    public WizardViewModel VM { get; private set; } = null!;

    public DeployOptionsPage()
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
}
