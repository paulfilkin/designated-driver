using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;

namespace DesignatedDriver.Helpers;

/// <summary>Screen reader support: live regions and announcements.</summary>
public static class AccessibilityHelper
{
    public static void SetupLiveRegion(FrameworkElement element, AutomationLiveSetting setting = AutomationLiveSetting.Polite)
    {
        AutomationProperties.SetLiveSetting(element, setting);
    }

    /// <summary>Raises the live region changed event on an element so assistive technology reads its new content.</summary>
    public static void Announce(FrameworkElement element, string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        AutomationProperties.SetName(element, message);
        if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
        {
            var peer = UIElementAutomationPeer.FromElement(element) ?? UIElementAutomationPeer.CreatePeerForElement(element);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }
}
