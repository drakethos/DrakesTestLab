using DrakesTestLab.Bootstrap;
using UnityEngine;

namespace DrakesTestLab.UI.Toolbox;

/// <summary>Ward (guard stone) test bench: stage any owner / permission / on-off scenario on a ward without a second player.</summary>
internal sealed class WardsTab : ToolboxTab
{
    private string _custom = "Dave";
    private string _playAsName = "Dave";
    private bool _applyLook = true;
    private float _radius = 10f;
    private PrivateArea? _target;

    public override string Title => "Wards";

    public override string Group => "Scenario";

    public override void Draw()
    {
        var p = Player.m_localPlayer;
        if (p == null)
        {
            GUILayout.Label("No player yet.");
            return;
        }

        Ui.Header("Play as (changes your player id + name)");
        GUILayout.Label($"You are: <b>{p.GetPlayerName()}</b> ({p.GetPlayerID()})" + (WardTools.IsSwitched ? "  <color=#f0b45a>(switched)</color>" : ""));
        _applyLook = GUILayout.Toggle(_applyLook, "Also change look (model / hair / colours)");
        Ui.Row(
            ("Alice (girl)", () => WardTools.PlayAs(WardTools.Alice, _applyLook)),
            ("Amy (girl)", () => WardTools.PlayAs(WardTools.Amy, _applyLook)),
            ("Bob", () => WardTools.PlayAs(WardTools.Bob, _applyLook)),
            ("Carol (girl)", () => WardTools.PlayAs(WardTools.Carol, _applyLook)),
            ("Bjornita (girl)", () => WardTools.PlayAs(WardTools.Bjornita, _applyLook)),
            ("Back to me", WardTools.RestoreOriginal));
        GUILayout.BeginHorizontal();
        _playAsName = GUILayout.TextField(_playAsName, GUILayout.Width(160));
        if (GUILayout.Button("Play as this name"))
            WardTools.PlayAs(WardTools.Parse(_playAsName), false);
        GUILayout.EndHorizontal();
        Ui.Hint("Stand in a ward as its owner, a permitted player or a stranger, without a second game running.");

        Ui.Header("Player 2 (a second client in this world)");
        var player2 = TestLabConfig.Instance.Player2.Value;
        var connected = DrakesTestLab.Automation.VirtualPeers.Find(player2);
        GUILayout.Label(connected != null
            ? $"<b>{connected.Name}</b> is connected (peer {connected.Uid}), {Vector3.Distance(connected.Position, p.transform.position):0} m away."
            : $"<b>{player2}</b> is not connected.");
        Ui.Row(
            ($"Connect {player2} here", () => DrakesTestLab.Automation.VirtualPeers.Join(player2, p.transform.position + p.transform.right * 3f, true, 0f, 0f, false)),
            ($"Bring {player2} to me", () => { if (DrakesTestLab.Automation.VirtualPeers.Find(player2) is { } vp) DrakesTestLab.Automation.VirtualPeers.Move(vp, p.transform.position + p.transform.right * 3f); }),
            ($"Disconnect {player2}", () => { if (DrakesTestLab.Automation.VirtualPeers.Find(player2) is { } vp) DrakesTestLab.Automation.VirtualPeers.Leave(vp); }));
        Ui.Hint("She joins through the game's own connection code, owns her character and answers what is sent to her like a second client. [Automation] Player2 picks who.");

        Ui.Header("Test ward");
        _radius = Ui.Slider("New ward radius", _radius, 3f, 40f, "0");
        Ui.Row(
            ("Spawn ward (off, owned by me)", () => SpawnAhead(WardTools.Me(), false)),
            ("Spawn ward (off, owned by Alice)", () => SpawnAhead(WardTools.Alice, false)));

        _target = WardTools.Nearest(60f);
        if (_target == null)
        {
            GUILayout.Label("No ward within 60 m. Spawn one above.");
            return;
        }

        var ward = _target;
        GUILayout.Label($"Nearest ward: {Vector3.Distance(ward.transform.position, p.transform.position):0} m away");
        GUILayout.Label(WardTools.Describe(ward));
        GUILayout.Label($"Game's access test at your position: {(WardTools.HaveAccessHere() ? "<color=#80ff80>allowed</color>" : "<color=#ff8080>blocked</color>")}");

        Ui.Header("Switch");
        Ui.Row(
            ("Turn ON", () => WardTools.SetEnabled(ward, true)),
            ("Turn OFF", () => WardTools.SetEnabled(ward, false)));

        Ui.Header("Owner");
        Ui.Row(
            ("Owner = me", () => WardTools.SetOwner(ward, WardTools.Me())),
            ("Owner = Alice", () => WardTools.SetOwner(ward, WardTools.Alice)),
            ("Owner = Amy", () => WardTools.SetOwner(ward, WardTools.Amy)),
            ("Owner = Bob", () => WardTools.SetOwner(ward, WardTools.Bob)),
            ("Owner = Carol", () => WardTools.SetOwner(ward, WardTools.Carol)),
            ("Owner = Bjornita", () => WardTools.SetOwner(ward, WardTools.Bjornita)));
        GUILayout.BeginHorizontal();
        _custom = GUILayout.TextField(_custom, GUILayout.Width(160));
        if (GUILayout.Button("Owner = this name"))
            WardTools.SetOwner(ward, WardTools.Parse(_custom));
        GUILayout.EndHorizontal();
        GUILayout.Label("A name makes up a stable fake id; \"Name:12345\" picks the id.");

        Ui.Header("Permitted players");
        Ui.Row(
            ("Add me", () => WardTools.AddPermitted(ward, WardTools.Me())),
            ("Remove me", () => WardTools.RemovePermitted(ward, WardTools.Me())),
            ("Clear all", () => WardTools.ClearPermitted(ward)));
        Ui.Row(
            ("Add Alice", () => WardTools.AddPermitted(ward, WardTools.Alice)),
            ("Add Amy", () => WardTools.AddPermitted(ward, WardTools.Amy)),
            ("Add Bob", () => WardTools.AddPermitted(ward, WardTools.Bob)),
            ("Add Carol", () => WardTools.AddPermitted(ward, WardTools.Carol)),
            ("Add Bjornita", () => WardTools.AddPermitted(ward, WardTools.Bjornita)),
            ("Add this name", () => WardTools.AddPermitted(ward, WardTools.Parse(_custom))));
        Ui.Row(
            ("Remove Alice", () => WardTools.RemovePermitted(ward, WardTools.Alice)),
            ("Remove Bob", () => WardTools.RemovePermitted(ward, WardTools.Bob)),
            ("Remove Carol", () => WardTools.RemovePermitted(ward, WardTools.Carol)),
            ("Remove Bjornita", () => WardTools.RemovePermitted(ward, WardTools.Bjornita)));

        Ui.Header("Ready-made scenarios (on this ward)");
        Ui.Row(
            ("Reset: mine, on, nobody else", () => WardTools.Reset(ward)),
            ("Stranger's ward, ON, no access", () => Scenario(ward, WardTools.Alice, true)));
        Ui.Row(
            ("Stranger's ward, ON, I'm permitted", () => Scenario(ward, WardTools.Alice, true, WardTools.Me())),
            ("Stranger's ward, OFF", () => Scenario(ward, WardTools.Alice, false)));
        Ui.Row(
            ("My ward, ON, Alice permitted", () => Scenario(ward, WardTools.Me(), true, WardTools.Alice)),
            ("My ward, OFF", () => Scenario(ward, WardTools.Me(), false)));

        Ui.Header("Overlap test");
        Ui.Row(("Spawn a second, overlapping ward (Bob, ON)", () => SpawnOverlapping(ward)));
        if (GUILayout.Button("Remove nearest ward"))
        {
            if (ZNetScene.instance != null)
                ZNetScene.instance.Destroy(ward.gameObject);
        }
    }

    private static void Scenario(PrivateArea ward, WardTools.Identity owner, bool on, params WardTools.Identity[] permitted)
    {
        WardTools.SetOwner(ward, owner);
        WardTools.ClearPermitted(ward);
        foreach (var who in permitted)
            WardTools.AddPermitted(ward, who);
        WardTools.SetEnabled(ward, on);
    }

    private void SpawnAhead(WardTools.Identity owner, bool enabled)
    {
        var p = Player.m_localPlayer;
        if (p == null)
            return;

        var pos = p.transform.position + p.transform.forward * 4f;
        WardTools.Spawn(pos, owner, enabled, null, _radius);
    }

    private void SpawnOverlapping(PrivateArea existing)
    {
        var pos = existing.transform.position + existing.transform.right * (existing.m_radius * 0.8f);
        WardTools.Spawn(pos, WardTools.Bob, true, null, _radius);
    }
}
