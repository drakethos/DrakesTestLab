using DrakesTestLab.Bootstrap;
using UnityEngine;

namespace DrakesTestLab.UI;

internal sealed class TestLabInputDriver : MonoBehaviour
{
    private static bool KeyPressed(string keyName) =>
        !string.IsNullOrWhiteSpace(keyName)
        && System.Enum.TryParse<KeyCode>(keyName, true, out var key)
        && Input.GetKeyDown(key);

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && TestControlPanel.IsVisible)
        {
            TestControlPanel.Hide();
            return;
        }

        if (KeyPressed(TestLabConfig.Instance.OverlayKey.Value))
        {
            var overlay = TestLabConfig.Instance.ShowOverlay;
            overlay.Value = !overlay.Value;
        }

        if (Automation.TestRunner.Running)
        {
            if (KeyPressed(TestLabConfig.Instance.StopTestsKey.Value))
                Automation.TestRunner.Stop();
            // next step / pass / fail are on the lab bar (UI/Toolbox/LabBar.cs)
        }

        var keyName = TestLabConfig.Instance.MenuKey.Value;
        if (string.IsNullOrWhiteSpace(keyName))
            return;

        if (!System.Enum.TryParse<KeyCode>(keyName, true, out var key))
            return;

        if (Input.GetKeyDown(key))
            TestControlPanel.Toggle();
    }
}
