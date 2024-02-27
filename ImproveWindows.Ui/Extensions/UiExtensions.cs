using System.Windows.Automation;

namespace ImproveWindows.Ui.Extensions;

public static class UiExtensions
{
    public static string? Click(this AutomationElement menuItem)
    {
        if (menuItem.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
        {
            if (pattern is not InvokePattern invokePattern)
            {
                throw new InvalidOperationException($"{pattern} is not {nameof(InvokePattern)}");
            }
            
            invokePattern.Invoke();
            return null;
        }
        
        if (menuItem.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern))
        {
            if (pattern is not SelectionItemPattern selectionPattern)
            {
                throw new InvalidOperationException($"{pattern} is not {nameof(SelectionItemPattern)}");
            }
            
            selectionPattern.Select();
            return null;
        }

        return $"Did not find matching pattern for {menuItem.Current.Name}";
    }
}