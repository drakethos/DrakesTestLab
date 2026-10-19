using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using DrakesTestLab.UI.Toolbox;
using DrakesTestLab.Util;
using UnityEngine;

namespace DrakesTestLab.Bootstrap;

/// <summary>
/// Writes a report of the building pieces that matter for roofs/cover: collider tags ("leaky" pieces do not count as a roof),
/// layers, sizes, the slope direction of angled pieces and the structural flags. Output: config/DrakesTestLab/piece-dump.txt
/// </summary>
internal static class PieceDump
{
    private static readonly string[] Keywords = { "roof", "floor", "wall", "beam", "pole", "ridge", "door", "gate", "stair" };

    private static string ConfigDir => Path.Combine(Paths.ConfigPath, "DrakesTestLab");
    private static string FlagPath => Path.Combine(ConfigDir, "dump.flag");
    public static string ReportPath => Path.Combine(ConfigDir, "piece-dump.txt");

    public static void RunIfFlagged()
    {
        if (!File.Exists(FlagPath) || ToolboxHost.Instance == null)
            return;

        try
        {
            File.Delete(FlagPath);
        }
        catch
        {
            // best effort
        }

        Start();
    }

    public static void Start()
    {
        if (ToolboxHost.Instance != null)
            ToolboxHost.Instance.StartCoroutine(Run());
    }

    private static IEnumerator Run()
    {
        for (var i = 0; i < 600 && (Player.m_localPlayer == null || ZNetScene.instance == null); i++)
            yield return null;

        // let the world settle (and the arena build finish) before spawning hundreds of probes
        yield return new WaitForSeconds(20f);
        while (ArenaBuilder.Busy)
            yield return null;

        var scene = ZNetScene.instance;
        if (scene == null)
            yield break;

        PrefabIndex.Ensure();
        var names = new List<string>();
        foreach (var n in PrefabIndex.Of(PrefabKind.Piece))
        {
            var l = n.ToLowerInvariant();
            foreach (var k in Keywords)
            {
                if (l.Contains(k))
                {
                    names.Add(n);
                    break;
                }
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# DrakesTestLab piece dump {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {names.Count} pieces");
        sb.AppendLine("# tag=leaky means the piece does NOT count as a roof (Cover.IsUnderRoof / WearNTear.RoofCheck ignore it).");
        sb.AppendLine("# slope: surface height above the piece bottom at the four edges (a flat piece shows equal values).");
        sb.AppendLine();

        var index = 0;
        foreach (var name in names)
        {
            var prefab = scene.GetPrefab(name);
            if (prefab == null)
                continue;

            var origin = new Vector3(index % 20 * 40f, 6000f + index / 20 * 40f, 0f);
            index++;
            GameObject? go = null;
            try
            {
                go = UnityEngine.Object.Instantiate(prefab, origin, Quaternion.identity);
                Physics.SyncTransforms();
                sb.AppendLine(Describe(name, prefab, go, origin));
            }
            catch (Exception ex)
            {
                sb.AppendLine($"{name} | dump failed: {ex.Message}");
            }
            finally
            {
                if (go != null)
                    scene.Destroy(go);
            }

            if (index % 15 == 0)
                yield return null;
        }

        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ReportPath, sb.ToString());
        TestLabPlugin.Log?.LogInfo($"Piece dump written: {ReportPath}");
    }

    private static string Describe(string name, GameObject prefab, GameObject go, Vector3 origin)
    {
        var sb = new StringBuilder();
        var piece = go.GetComponent<Piece>();
        sb.Append(name).Append(" | ").Append(piece != null ? piece.m_name : "?");
        if (piece != null)
            sb.Append(" | cat=").Append(piece.m_category).Append(" clipEverything=").Append(piece.m_clipEverything)
                .Append(" groundPiece=").Append(piece.m_groundPiece).Append(" dungeons=").Append(piece.m_allowedInDungeons);

        var wear = go.GetComponent<WearNTear>();
        if (wear != null)
            sb.Append(" | wear: mat=").Append(wear.m_materialType).Append(" supports=").Append(wear.m_supports)
                .Append(" noSupportWear=").Append(wear.m_noSupportWear).Append(" noRoofWear=").Append(wear.m_noRoofWear)
                .Append(" hp=").Append(wear.m_health);

        var cols = go.GetComponentsInChildren<Collider>(true);
        var bounds = new Bounds(origin, Vector3.zero);
        var first = true;
        foreach (var c in cols)
        {
            if (first)
            {
                bounds = c.bounds;
                first = false;
            }
            else
            {
                bounds.Encapsulate(c.bounds);
            }
        }

        sb.Append(" | size=").Append(bounds.size.ToString("0.00"));
        sb.Append(" centerOffset=").Append((bounds.center - origin).ToString("0.00"));
        foreach (var c in cols)
        {
            sb.Append(" | ").Append(c.GetType().Name).Append(" '").Append(c.gameObject.name).Append("' layer=")
                .Append(LayerMask.LayerToName(c.gameObject.layer)).Append(" tag=").Append(c.gameObject.tag)
                .Append(c.isTrigger ? " TRIGGER" : "");
        }

        sb.Append(" | slope:");
        var reach = Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.8f;
        foreach (var (label, dir) in new[] { ("+z", Vector3.forward), ("-z", Vector3.back), ("+x", Vector3.right), ("-x", Vector3.left) })
        {
            var from = new Vector3(bounds.center.x, origin.y + 60f, bounds.center.z) + dir * reach;
            sb.Append(' ').Append(label).Append('=');
            if (Physics.Raycast(from, Vector3.down, out var hit, 120f, ~0, QueryTriggerInteraction.Ignore))
                sb.Append((hit.point.y - bounds.min.y).ToString("0.00")).Append(hit.collider.gameObject.CompareTag("leaky") ? "(leaky)" : "");
            else
                sb.Append("miss");
        }

        return sb.ToString();
    }
}
