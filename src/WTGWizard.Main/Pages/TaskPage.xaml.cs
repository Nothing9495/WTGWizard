using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WTGWizard.Main;
using WTGWizard.Messages;
using WTGWizard.ViewModels;

namespace WTGWizard.Pages;

public sealed partial class TaskPage : Page, ITabActivatable
{
    private readonly WizardViewModel _vm;

    public TaskPage()
    {
        _vm = App.Services.GetRequiredService<WizardViewModel>();
        InitializeComponent();
    }

    public void OnTabActivated()
    {
        // TODO: 激活任务执行
    }

    public void OnTabDeactivated()
    {
        // TODO: 停用任务执行
    }
}
