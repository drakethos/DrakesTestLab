using DrakesTestLab.UI.Toolbox;

namespace DrakesTestLab.UI;

/// <summary>Entry point used by hotkeys and patches; the real UI lives in <see cref="ToolboxHost"/>.</summary>
internal static class TestControlPanel
{
    public static bool IsVisible => ToolboxHost.IsOpen;

    public static void Toggle() => ToolboxHost.Toggle();

    public static void Show() => ToolboxHost.Set(true);

    public static void Hide() => ToolboxHost.Set(false);
}
