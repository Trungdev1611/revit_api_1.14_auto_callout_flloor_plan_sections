using Autodesk.Revit.Attributes;
using Nice3point.Revit.Toolkit.External;
using WCBathAutomation.ViewModels;
using WCBathAutomation.Views;

namespace WCBathAutomation.Commands;

/// <summary>
///     External command entry point.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class StartupCommand : ExternalCommand
{
    public override void Execute()
    {
        var viewModel = new WCBathAutomationViewModel();
        var view = new WCBathAutomationView(viewModel);
        view.ShowDialog();
    }
}