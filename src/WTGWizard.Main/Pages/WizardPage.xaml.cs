using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WTGWizard.Main;
using WTGWizard.ViewModels;

namespace WTGWizard.Pages;

public sealed partial class WizardPage : Page, ITabActivatable
{
    private readonly WizardViewModel _vm;
    private WizardHost? _host;

    public WizardPage()
    {
        _vm = App.Services.GetRequiredService<WizardViewModel>();
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (_host is null)
        {
            _host = new WizardHost(_vm);
            HostContent.Content = _host;
        }
    }

    public void OnTabActivated()
    {
        _host?.OnTabActivated();
    }

    public void OnTabDeactivated()
    {
        _host?.OnTabDeactivated();
    }
}
