using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DrakesTestLab.Automation;
using DrakesTestLab.Cases;

namespace DrakesTestLab.Combat;

/// <summary>
/// The game difficulty baseline: for each biome / foe / stars, how a mid player of that biome does alone (win rate,
/// kill time, damage taken) and how many such players it takes to win at least half the time. Written by "difficulty"
/// scenarios into combat/difficulty-db.json (merged: a rebuilt row replaces the old one, other rows stay). A copy
/// ships with the mod so a fresh install has numbers to compare with; rebuilding it all takes hours.
/// </summary>
internal sealed class DifficultyRow
{
    public string Biome = "", Foe = "", Loadout = "", Bot = "", Scenario = "";
    public int Stars;
    public int PlayersNeeded;               // 0 = even the largest team tried did not win often enough
    public int TeamTried;
    public float Clock = 1f;
    public double NeedWin;
    public int SoloRuns, SoloWins;
    public double SoloTtk, SoloDeathTime, SoloDamagePct;
    public List<(int Size, int Wins, int Runs, double Fell)> Teams = new();

    public string Key => $"{Biome}/{Foe}/{Stars}";
}

internal static class CombatDifficultyDb
{
    public static string FilePath => Path.Combine(CombatData.Folder, "difficulty-db.json");

    private static readonly List<DifficultyRow> Pending = new();

    public static void Record(DifficultyRow row) => Pending.Add(row);

    /// <summary>Merges this run's rows into the DB file. The settings that made them go with every row.</summary>
    public static void Save()
    {
        if (Pending.Count == 0) return;
        try
        {
            var rows = new Dictionary<string, object?>();
            Dictionary<string, object?>? oldDoc = null;
            if (File.Exists(FilePath) && MiniJson.Parse(File.ReadAllText(FilePath)) is Dictionary<string, object?> old)
            {
                oldDoc = old;
                if (old.Obj("rows") is { } oldRows)
                    foreach (var kv in oldRows) rows[kv.Key] = kv.Value;
            }
            var when = DateTime.Now.ToString("s");
            var game = Version.GetVersionString();
            foreach (var r in Pending)
            {
                rows[r.Key] = new Dictionary<string, object?>
                {
                    ["biome"] = r.Biome, ["foe"] = r.Foe, ["stars"] = r.Stars, ["loadout"] = r.Loadout, ["bot"] = r.Bot, ["scenario"] = r.Scenario,
                    ["playersNeeded"] = r.PlayersNeeded, ["teamTried"] = r.TeamTried, ["needWin"] = r.NeedWin,
                    ["solo"] = new Dictionary<string, object?>
                    {
                        ["runs"] = r.SoloRuns, ["wins"] = r.SoloWins, ["winRate"] = r.SoloRuns == 0 ? 0 : Math.Round((double)r.SoloWins / r.SoloRuns, 3),
                        ["ttk"] = Math.Round(r.SoloTtk, 1), ["timeToDie"] = Math.Round(r.SoloDeathTime, 1), ["damageTakenPct"] = Math.Round(r.SoloDamagePct, 1)
                    },
                    ["teams"] = r.Teams.Select(t => (object?)new Dictionary<string, object?> { ["size"] = t.Size, ["wins"] = t.Wins, ["runs"] = t.Runs, ["fell"] = Math.Round(t.Fell, 2) }).ToList(),
                    ["when"] = when, ["game"] = game, ["testlab"] = TestLabPlugin.Version, ["clock"] = r.Clock
                };
            }
            var doc = new Dictionary<string, object?>
            {
                ["about"] = "DrakesTestLab Combat Lab: game difficulty baseline. A mid player of each biome (q2 gear of the tier, the tier's skills, real meals, the 'fighter' bot) " +
                            "against each foe and star level: solo results and how many players it takes to win at least half the time. Rows written by your own runs replace " +
                            "the shipped ones. Full rebuild: -testlab-combat \"cal-*\" then tools/combat_predict.py and tools/combat_build_db.py (takes hours).",
                ["updated"] = when,
            };
            if (oldDoc != null)                      // keep what the shipped file carries beside the rows (the model's settings)
                foreach (var kv in oldDoc)
                    if (!doc.ContainsKey(kv.Key) && kv.Key != "rows") doc[kv.Key] = kv.Value;
            doc["rows"] = rows.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(kv => kv.Key, kv => kv.Value);
            Directory.CreateDirectory(CombatData.Folder);
            File.WriteAllText(FilePath, JsonWriter.Write(doc));
            TestLabPlugin.Log?.LogInfo($"Combat: difficulty DB updated ({Pending.Count} row(s)): {FilePath}");
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning("Combat: could not save the difficulty DB: " + ex.Message);
        }
        finally
        {
            Pending.Clear();
        }
    }
}
