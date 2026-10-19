using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Bootstrap;

internal static class MenuSuppressor
{
    private static readonly string[] PanelFields =
    {
        "m_mainMenu",
        "m_startGamePanel",
        "m_characterSelectScreen",
        "m_selectCharacterPanel",
        "m_worldListPanel",
        "m_createWorldPanel",
        "m_ndaPanel",
        "m_serverListPanel",
        "m_creditsPanel",
        "m_connectionFailedPanel"
    };

    public static void Suppress(FejdStartup fejd)
    {
        if (fejd == null)
            return;

        foreach (var fieldName in PanelFields)
        {
            if (AccessTools.Field(typeof(FejdStartup), fieldName)?.GetValue(fejd) is GameObject panel)
                panel.SetActive(false);
        }

        if (AccessTools.Field(typeof(FejdStartup), "m_menuAnimator")?.GetValue(fejd) is Animator animator)
        {
            animator.SetBool("FirstStartup", false);
            animator.Update(0f);
        }
    }
}
