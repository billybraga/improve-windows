// using System.Text.RegularExpressions;
// using System.Windows.Automation;
// using ImproveWindows.Ui.Extensions;
using ImproveWindows.Core.Services;

namespace ImproveWindows.Ui;

internal partial class VpnService : AppService
{
    protected override async Task StartAsync(CancellationToken cancellationToken)
    {
        // var outlook = AutomationElement
        //     .RootElement
        //     .FindAll(
        //         TreeScope.Children,
        //         new AndCondition(
        //             new PropertyCondition(AutomationElement.ProcessIdProperty, 9980),
        //             // new OrCondition(
        //             //     new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
        //                 new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Pane)
        //             // )
        //         )
        //     )
        //     .OfType<AutomationElement>()
        //     .Single(x => string.IsNullOrEmpty(x.Current.Name));
        // // .Single(x => OutlookMailRegex().IsMatch(x.Current.Name));
        //
        // var items = outlook
        //     .FindAll(
        //         TreeScope.Descendants,
        //         new OrCondition(
        //             new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text),
        //             new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Pane),
        //             new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Menu),
        //             new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
        //             new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
        //             new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
        //             new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
        //             new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem),
        //             new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem)
        //         )
        //     ).OfType<AutomationElement>()
        //     // .Where(x => x.Current.Name.Contains("Bluetooth & devices"))
        //     .ToArray();
        //
        // foreach (var item in items)
        // {
        //     LogInfo($"{item.Current.ControlType.ProgrammaticName}: {item.Current.Name}");
        //     // item.Click();
        // }

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(5000, cancellationToken);
        }
    }
}