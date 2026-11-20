using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Automation;

/// <summary>
/// The sender block of a chat RPC, written exactly as the game's UserInfo writes it (name, then the platform user id as
/// text), so the receiving game reads a normal UserInfo. Kept separate so TestLab needs no Splatform reference.
/// </summary>
internal sealed class PeerUserInfo : ISerializableParameter
{
    public string Name;
    public string UserId;

    public PeerUserInfo(string name, string userId)
    {
        Name = name;
        UserId = userId;
    }

    public void Serialize(ref ZPackage pkg)
    {
        pkg.Write(Name);
        pkg.Write(UserId);
    }

    public void Deserialize(ref ZPackage pkg)
    {
        Name = pkg.ReadString();
        UserId = pkg.ReadString();
    }
}

/// <summary>One chat message that reached this game's chat (Chat.OnNewChatMessage), whoever sent it.</summary>
internal sealed class ChatEntry
{
    public string From = "";
    public long Sender;
    public Talker.Type Type;
    public string Text = "";
    public float Time;

    public override string ToString() => $"[{TypeName(Type)}] {From}: {(Type == Talker.Type.Ping ? "(ping)" : Text)}";

    public static string TypeName(Talker.Type t) => t switch
    {
        Talker.Type.Whisper => "whisper",
        Talker.Type.Normal => "say",
        Talker.Type.Shout => "shout",
        Talker.Type.Ping => "ping",
        _ => t.ToString().ToLowerInvariant()
    };
}

/// <summary>
/// Talking, emotes and pings, both ways between you (player 1, this game) and a virtual player 2, through the same
/// messages real clients send:
///  - say / whisper: the Talker "Say" RPC on the speaker's character; shout and ping: the "ChatMessage" routed RPC.
///  - emotes: the emote / emoteID / emoteOneshot values on the character's ZDO (Player.StartEmote).
/// Steps: chat (you type into chat, commands included: "/s hi", "/w hi", "/wave"), peerChat (player 2 types the same),
/// peerEmote, peerPing, chatClear. Checks: chatSeen (what reached your chat), peerHeard (what reached player 2),
/// emote (who is doing which emote).
/// </summary>
internal static class ChatSteps
{
    public static readonly List<ChatEntry> Log = new();

    public static readonly string[] StepNames = { "chat", "peerChat", "peerEmote", "peerPing", "chatClear" };
    public static readonly string[] CheckNames = { "chatSeen", "peerHeard", "emote" };

    // ---- you type into chat ----

    /// <summary>
    /// What pressing Enter in the chat box does (Chat.InputText): "/cmd args" runs the command (s, w, say, an emote, or a
    /// mod's command), anything else is said aloud.
    /// </summary>
    public static void Type(string text)
    {
        var chat = Chat.instance ?? throw new StepFailed("no chat (not in a world)");
        if (text.Length == 0)
            throw new StepFailed("chat needs \"text\"");
        var input = AccessTools.Field(typeof(Terminal), "m_input")?.GetValue(chat) ?? throw new StepFailed("chat input not found (game update?)");
        var textProp = input.GetType().GetProperty("text") ?? throw new StepFailed("chat input has no text (game update?)");
        var inputText = AccessTools.Method(typeof(Chat), "InputText") ?? throw new StepFailed("Chat.InputText not found (game update?)");
        textProp.SetValue(input, text);
        try { inputText.Invoke(chat, null); }
        finally { textProp.SetValue(input, ""); }
    }

    // ---- player 2 types into chat ----

    public static PeerUserInfo UserOf(VirtualPeer vp) => new(vp.Name, "Steam_" + VirtualPeers.SteamIdOf(vp.PlayerId));

    private static Vector3 HeadOf(VirtualPeer vp)
    {
        var go = vp.Puppet;
        var ch = go != null ? go.GetComponent<Character>() : null;
        return ch != null ? ch.GetHeadPoint() : vp.Position + Vector3.up * 1.8f;
    }

    /// <summary>Player 2's chat line, read the way their Chat.InputText would: "/s", "/shout", "/w", "/whisper", "/say", an emote, or plain text.</summary>
    public static string PeerType(VirtualPeer vp, string text)
    {
        if (text.Length == 0)
            throw new StepFailed("peerChat needs \"text\"");
        if (text[0] != '/')
            return Say(vp, Talker.Type.Normal, text);
        var space = text.IndexOf(' ');
        var cmd = (space > 0 ? text.Substring(1, space - 1) : text.Substring(1)).ToLowerInvariant();
        var rest = space > 0 ? text.Substring(space + 1) : "";
        switch (cmd)
        {
            case "s":
            case "shout":
                return Shout(vp, rest);
            case "w":
            case "whisper":
                return Say(vp, Talker.Type.Whisper, rest);
            case "say":
                return Say(vp, Talker.Type.Normal, rest);
        }
        if (Enum.TryParse<Emotes>(cmd, true, out var emote) && emote != Emotes.Count)
            return Emote(vp, cmd, false);
        throw new StepFailed($"peerChat: '/{cmd}' is not a chat command a virtual player can run (s, shout, w, whisper, say, or an emote); a mod's own command runs on its client, which a virtual player does not have");
    }

    /// <summary>Talker.Say from their character: to everybody, each game decides by distance whether it is heard (RPC_Say).</summary>
    public static string Say(VirtualPeer vp, Talker.Type type, string text)
    {
        var zdo = vp.PuppetZdo ?? throw new StepFailed($"{vp.Name} has no character (peerJoin with \"puppet\": true)");
        vp.SendRoutedRpc(0L, zdo.m_uid, "Say", new object[] { (int)type, UserOf(vp), text });
        return $"{ChatEntry.TypeName(type)} \"{text}\"";
    }

    /// <summary>Chat.SendText(Shout): a ChatMessage from their head to everybody, heard at any distance.</summary>
    public static string Shout(VirtualPeer vp, string text)
    {
        vp.SendRoutedRpc(0L, ZDOID.None, "ChatMessage", new object[] { HeadOf(vp), (int)Talker.Type.Shout, UserOf(vp), text });
        return $"shout \"{text}\"";
    }

    /// <summary>Chat.SendPing: a map ping at a point (at their height), to everybody.</summary>
    public static string Ping(VirtualPeer vp, Vector3 at)
    {
        at.y = vp.Position.y;
        vp.SendRoutedRpc(0L, ZDOID.None, "ChatMessage", new object[] { at, (int)Talker.Type.Ping, UserOf(vp), "" });
        return $"ping at {at.x:0.#}, {at.z:0.#}";
    }

    /// <summary>Player.StartEmote / StopEmote on their character: what every other game reads to play it.</summary>
    public static string Emote(VirtualPeer vp, string name, bool stop)
    {
        var zdo = vp.PuppetZdo ?? throw new StepFailed($"{vp.Name} has no character (peerJoin with \"puppet\": true)");
        var id = zdo.GetInt(ZDOVars.s_emoteID);
        zdo.Set(ZDOVars.s_emoteID, id + 1);
        if (stop)
        {
            zdo.Set(ZDOVars.s_emote, "");
            return "stops the emote";
        }
        if (!Enum.TryParse<Emotes>(name, true, out var emote) || emote == Emotes.Count)
            throw new StepFailed($"no emote '{name}' (wave, sit, cheer, point, dance, ...)");
        var oneShot = typeof(Emotes).GetField(emote.ToString())?.GetCustomAttributes(typeof(Emote), false).OfType<Emote>().FirstOrDefault()?.OneShot ?? true;
        zdo.Set(ZDOVars.s_emote, emote.ToString().ToLowerInvariant());
        zdo.Set(ZDOVars.s_emoteOneshot, oneShot);
        return $"emote {emote.ToString().ToLowerInvariant()}{(oneShot ? "" : " (held)")}";
    }

    // ---- checks ----

    private static bool TypeMatches(string want, Talker.Type t) =>
        want.Length == 0 || want == "any" || ChatEntry.TypeName(t) == want || (want == "normal" && t == Talker.Type.Normal);

    private static long SenderOf(string who) =>
        who.Length == 0 || who.Equals("me", StringComparison.OrdinalIgnoreCase) || who.Equals("host", StringComparison.OrdinalIgnoreCase)
            ? ZDOMan.GetSessionID()
            : VirtualPeers.Find(who)?.Uid ?? -1L;

    /// <summary>"from" (me / a player 2 name / any), "type" (say, shout, whisper, ping, any), "contains", "count" (at least, default 1), "max".</summary>
    public static (bool, string) Seen(Dictionary<string, object?> a)
    {
        var from = a.First("from", "peer");
        var type = a.Str("type").ToLowerInvariant();
        var contains = a.Str("contains");
        var sender = from.Length == 0 || from.Equals("any", StringComparison.OrdinalIgnoreCase) ? 0L : SenderOf(from);
        var hits = Log.Where(e => (sender == 0L || e.Sender == sender) && TypeMatches(type, e.Type)
                                  && (contains.Length == 0 || e.Text.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
        return Count(a, hits.Count, Log.Select(e => e.ToString()).ToList(), "in your chat");
    }

    /// <summary>What reached player 2: the Say / ChatMessage RPCs in their inbox. Same fields as chatSeen.</summary>
    public static (bool, string) Heard(VirtualPeer vp, Dictionary<string, object?> a)
    {
        var from = a.Str("from");
        var type = a.Str("type").ToLowerInvariant();
        var contains = a.Str("contains");
        var sender = from.Length == 0 || from.Equals("any", StringComparison.OrdinalIgnoreCase) ? 0L : SenderOf(from);
        var say = "Say".GetStableHashCode();
        var msg = "ChatMessage".GetStableHashCode();
        var all = new List<ChatEntry>();
        foreach (var m in vp.Inbox)
        {
            if (m.Hash != say && m.Hash != msg)
                continue;
            try
            {
                var pkg = new ZPackage(m.Parameters);
                if (m.Hash == msg)
                    pkg.ReadVector3();
                var t = (Talker.Type)pkg.ReadInt();
                var name = pkg.ReadString();
                pkg.ReadString(); // platform user id
                var text = pkg.ReadString();
                all.Add(new ChatEntry { From = name, Sender = m.Sender, Type = t, Text = text, Time = m.Time });
            }
            catch
            {
                // not a chat line this reader knows
            }
        }
        var hits = all.Where(e => (sender == 0L || e.Sender == sender) && TypeMatches(type, e.Type)
                                  && (contains.Length == 0 || e.Text.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
        return Count(a, hits.Count, all.Select(e => e.ToString()).ToList(), $"heard by {vp.Name}");
    }

    private static (bool, string) Count(Dictionary<string, object?> a, int n, List<string> seen, string where)
    {
        var min = (int)a.Num("count", a.Has("max") ? 0 : 1);
        var max = a.Has("max") ? (int)a.Num("max") : int.MaxValue;
        var recent = seen.Count > 6 ? seen.Skip(seen.Count - 6).ToList() : seen;
        return (n >= min && n <= max, $"{n} matching {where} (need {(max == int.MaxValue ? $"{min}+" : $"{min}..{max}")}); lines: {(recent.Count > 0 ? string.Join(" | ", recent) : "none")}");
    }

    /// <summary>"who": me / a player 2 name; "expect": an emote name, or "none".</summary>
    public static (bool, string) EmoteCheck(Dictionary<string, object?> a)
    {
        var who = a.First("who", "peer");
        ZDO? zdo;
        if (who.Length == 0 || who.Equals("me", StringComparison.OrdinalIgnoreCase))
        {
            var view = Player.m_localPlayer != null ? Player.m_localPlayer.GetComponent<ZNetView>() : null;
            zdo = view != null && view.IsValid() ? view.GetZDO() : null;
        }
        else
        {
            zdo = (VirtualPeers.Find(who) ?? throw new StepFailed($"no virtual player '{who}'")).PuppetZdo;
        }
        if (zdo == null)
            throw new StepFailed($"{(who.Length == 0 ? "you" : who)}: no character");
        var now = zdo.GetString(ZDOVars.s_emote);
        var want = a.Str("expect", "none").ToLowerInvariant();
        var ok = want == "none" ? now.Length == 0 : now.Equals(want, StringComparison.OrdinalIgnoreCase);
        return (ok, $"emote is {(now.Length > 0 ? now : "none")}");
    }
}

/// <summary>Every chat line, say / shout / whisper / ping, that reaches this game's chat: the record chatSeen reads.</summary>
[HarmonyPatch(typeof(Chat), nameof(Chat.OnNewChatMessage))]
internal static class ChatSteps_OnNewChatMessage_Patch
{
    private static void Prefix(long senderID, Talker.Type type, UserInfo sender, string text)
    {
        try
        {
            ChatSteps.Log.Add(new ChatEntry { From = sender?.Name ?? "", Sender = senderID, Type = type, Text = text ?? "", Time = Time.realtimeSinceStartup });
            if (ChatSteps.Log.Count > 500)
                ChatSteps.Log.RemoveRange(0, ChatSteps.Log.Count - 500);
        }
        catch
        {
            // the record is for tests only: never get in the way of chat
        }
    }
}
