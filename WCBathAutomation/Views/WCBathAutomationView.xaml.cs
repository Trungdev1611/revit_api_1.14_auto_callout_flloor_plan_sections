using WCBathAutomation.ViewModels;

namespace WCBathAutomation.Views;

public sealed partial class WCBathAutomationView
{
    public WCBathAutomationView(WCBathAutomationViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}