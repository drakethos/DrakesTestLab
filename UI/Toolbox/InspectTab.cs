using System.Text;
using DrakesTestLab.Util;
using UnityEngine;

namespace DrakesTestLab.UI.Toolbox;

internal sealed class InspectTab : ToolboxTab
{
    private readonly SpawnTab _spawn;

    public InspectTab(SpawnTab spawn) => _spawn = spawn;

    public override string Title => "Inspect";

    /// <summary>One-line live status, shared with the overlay.</summary>
    public static string StatusLine()
    {
        var p = Player.m_localPlayer;
        if (p == null)
            return $"{Mathf.RoundToInt(1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f))} fps";

        var pos = p.transform.position;
        var env = EnvMan.instance;
        var time = env != null ? $"  day {env.GetDay()} {WorldTab.Clock(env.GetDayFraction())}" : "";
        return $"{Mathf.RoundToInt(1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f))} fps   " +
               $"{pos.x:0},{pos.y:0},{pos.z:0}   {p.GetCurrentBiome()}{time}   HP {p.GetHealth():0}/{p.GetMaxHealth():0}";
    }

    public override void Draw()
    {
        var p = Player.m_localPlayer;
        if (p == null)
        {
            GUILayout.Label("No player yet.");
            return;
        }

        Ui.Header("Player");
        var pos = p.transform.position;
        GUILayout.Label($"Position: {pos.x:0.0}, {pos.y:0.0}, {pos.z:0.0}    Biome: {p.GetCurrentBiome()}");
        GUILayout.Label($"Health {p.GetHealth():0}/{p.GetMaxHealth():0}   Stamina {p.GetStamina():0}/{p.GetMaxStamina():0}   " +
                        $"Weight {p.GetInventory().GetTotalWeight():0.#}");
        if (ZDOMan.instance != null)
            GUILayout.Label($"ZDOs in world: {ZDOMan.instance.NrOfObjects()}");
        if (GUILayout.Button("Copy position"))
            Ui.Copy($"{pos.x:0.##},{pos.y:0.##},{pos.z:0.##}");

        if (GUILayout.Button("Dump roof/wall/floor piece data to config/DrakesTestLab/piece-dump.txt"))
            DrakesTestLab.Bootstrap.PieceDump.Start();

        Ui.Header("Looking at");
        var target = Raycast();
        if (target == null)
        {
            GUILayout.Label("Nothing in range - aim at an object.");
            return;
        }

        var root = target.gameObject;
        var prefab = Utils.GetPrefabName(root);
        GUILayout.Label($"<b>{prefab}</b>   ({Vector3.Distance(p.transform.position, root.transform.position):0.0} m)");

        var sb = new StringBuilder();
        foreach (var c in root.GetComponents<Component>())
        {
            if (c != null)
                sb.Append(c.GetType().Name).Append("  ");
        }

        GUILayout.Label($"Components: {sb}");

        var zdo = target.GetZDO();
        if (zdo != null)
            GUILayout.Label($"ZDO: {zdo.m_uid}   owner: {zdo.GetOwner()}");

        var character = root.GetComponent<Character>();
        if (character != null)
            GUILayout.Label($"Creature: {character.m_name}   HP {character.GetHealth():0}/{character.GetMaxHealth():0}   level {character.GetLevel()}");

        var wear = root.GetComponent<WearNTear>();
        if (wear != null)
            GUILayout.Label($"Structure health: {wear.GetHealthPercentage() * 100f:0}%");

        Ui.Row(
            ("Copy prefab name", () => Ui.Copy(prefab)),
            ("Use in Spawn tab", () => _spawn.Select(prefab)),
            ("Destroy it", () =>
            {
                if (ZNetScene.instance != null)
                    ZNetScene.instance.Destroy(root);
            }));
    }

    private static ZNetView? Raycast()
    {
        var cam = Camera.main;
        if (cam == null)
            return null;

        var t = cam.transform;
        if (!Physics.Raycast(t.position, t.forward, out var hit, 80f, ~0, QueryTriggerInteraction.Ignore))
            return null;

        return hit.collider.GetComponentInParent<ZNetView>();
    }
}
