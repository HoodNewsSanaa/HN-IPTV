using System.Windows;
using System.Windows.Controls;
namespace HnIptv;

internal static class ToggleVisual
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.RegisterAttached(
        "IsActive", typeof(bool), typeof(ToggleVisual), new FrameworkPropertyMetadata(false));
    public static bool GetIsActive(DependencyObject element) => (bool)element.GetValue(IsActiveProperty);
    public static void SetIsActive(DependencyObject element, bool value)
    {
        element.SetValue(IsActiveProperty, value);
        if (element is Button button)
            System.Windows.Automation.AutomationProperties.SetItemStatus(button, value ? "On" : "Off");
    }
}
