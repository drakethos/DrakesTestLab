using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DrakesTestLab.Bootstrap;
using DrakesTestLab.Util;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Gear;

/// <summary>Where a model sits on its joint, on top of the computed default: offsets in the model's own space (metres, degrees).</summary>
internal sealed class Fit
{
    public Vector3 Pos;
    public Vector3 Rot;
    public float Scale = 1f;

    public override string ToString() => $"pos {Pos.x:0.###},{Pos.y:0.###},{Pos.z:0.###} rot {Rot.x:0.#},{Rot.y:0.#},{Rot.z:0.#} scale {Scale:0.##}";
}

/// <summary>
/// The lab gear: a clipboard (a tool held in the right hand) and black lab glasses (utility slot), built at runtime from vanilla
/// donors with no Jotunn, pack or bundle. The clipboard notes down whatever you look at and opens a menu of notes and actions
/// (Clipboard, ClipboardMenu); the glasses show a readout of anything in sight (Readout, GearHost).
/// </summary>
internal static class LabGear
{
    public const string ClipboardPrefab = "Drake_Clipboard";
    public const string GlassesPrefab = "Drake_LabGlasses";
    public const string ClipboardName = "Lab Clipboard";
    public const string GlassesName = "Lab Glasses";
    private const string ClipboardDonor = "Hammer";
    private static readonly string[] GlassesDonors = { "BeltStrength", "Wishbone", "Demister" };
    private const string HeadJoint = "Head";

    public static bool Enabled => TestLabConfig.Instance == null || TestLabConfig.Instance.LabGear.Value;

    private static GameObject? _root;
    private static GameObject? _clipboard;
    private static GameObject? _glasses;
    private static bool _failed;
    private static Material? _material;
    private static Material? _creatureMaterial;
    private static Material? _donorPiece;
    private static Material? _donorCreature;
    private static readonly List<GameObject> _probes = new();
    private static Sprite? _clipboardIcon;
    private static Sprite? _glassesIcon;

    // the player's joints, read from the Player prefab once ZNetScene has it
    private static Transform? _playerRoot;
    private static Transform? _rightHand;
    private static Transform? _head;
    private static Transform? _eye;
    private static Bounds? _hammerBounds;
    private static bool _fittedToPlayer;

    /// <summary>Tuning on top of the computed placement (dev/Gear/gear.json overrides these).</summary>
    public static readonly Fit ClipboardFit = new();
    public static readonly Fit GlassesFit = new();

    public static GameObject? ClipboardObject => _clipboard;
    public static GameObject? GlassesObject => _glasses;

    // ---- registration (no Jotunn): ObjectDB for the items and recipes, ZNetScene so they can be spawned, dropped and synced ----

    internal static void RegisterIn(ObjectDB db)
    {
        if (!Enabled || db == null || db.m_items == null || db.m_items.Count == 0)
            return;
        if (!EnsureBuilt(name => db.m_items.Find(p => p != null && p.name == name)))
            return;
        foreach (var prefab in new[] { _clipboard!, _glasses! })
        {
            if (!db.m_items.Contains(prefab))
                db.m_items.Add(prefab);
            // Private in the live game (the build-time reference assemblies expose it): reach it with AccessTools.
            if (AccessTools.Field(typeof(ObjectDB), "m_itemByHash")?.GetValue(db) is Dictionary<int, GameObject> byHash)
                byHash[prefab.name.GetStableHashCode()] = prefab;
            AddRecipe(db, prefab);
        }
    }

    internal static void RegisterIn(ZNetScene scene)
    {
        if (!Enabled || scene == null || scene.m_prefabs == null || scene.m_prefabs.Count == 0)
            return;
        ReadPlayerJoints(scene.m_prefabs.Find(p => p != null && p.name == "Player"));
        if (!EnsureBuilt(name => scene.m_prefabs.Find(p => p != null && p.name == name)))
            return;
        if (!_fittedToPlayer && _playerRoot != null)
            DressAll(); // built earlier (ObjectDB first) without the player's joints: place the models now
        foreach (var prefab in new[] { _clipboard!, _glasses! })
        {
            if (!scene.m_prefabs.Contains(prefab))
                scene.m_prefabs.Add(prefab);
            if (AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs")?.GetValue(scene) is Dictionary<int, GameObject> named)
                named[prefab.name.GetStableHashCode()] = prefab;
        }
    }

    private static void AddRecipe(ObjectDB db, GameObject prefab)
    {
        var drop = prefab.GetComponent<ItemDrop>();
        if (drop == null || db.m_recipes == null || db.m_recipes.Exists(r => r != null && r.m_item == drop))
            return;
        // free and at hand: the gear is lab equipment, not loot
        var recipe = ScriptableObject.CreateInstance<Recipe>();
        recipe.name = "Recipe_" + prefab.name;
        recipe.m_item = drop;
        recipe.m_amount = 1;
        recipe.m_enabled = true;
        recipe.m_resources = Array.Empty<Piece.Requirement>();
        recipe.m_craftingStation = null;
        recipe.m_minStationLevel = 1;
        db.m_recipes.Add(recipe);
    }

    private static bool EnsureBuilt(Func<string, GameObject?> find)
    {
        if (_clipboard != null && _glasses != null)
            return true;
        if (_failed)
            return false;
        try
        {
            var hammer = find(ClipboardDonor) ?? throw new InvalidOperationException($"no '{ClipboardDonor}' prefab");
            GameObject? belt = null;
            foreach (var name in GlassesDonors)
                if ((belt = find(name)) != null)
                    break;
            if (belt == null)
                throw new InvalidOperationException("no utility item to clone (" + string.Join(", ", GlassesDonors) + ")");

            _root ??= MakeRoot();
            LoadTuning();
            _clipboard ??= BuildClipboard(hammer);
            _glasses ??= BuildGlasses(belt);
            TestLabPlugin.Log?.LogInfo($"Lab gear: built {ClipboardPrefab} (from {ClipboardDonor}) and {GlassesPrefab} (from {belt.name}).");
            return true;
        }
        catch (Exception ex)
        {
            _failed = true;
            TestLabPlugin.Log?.LogWarning($"Lab gear: could not build the items ({ex.GetType().Name}: {ex.Message}). They will be missing this session.");
            return false;
        }
    }

    private static GameObject MakeRoot()
    {
        // Prefabs live under an inactive root, like Jotunn's: clones are real prefabs, never scene objects.
        var root = new GameObject("DrakesTestLab_LabGear");
        root.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(root);
        return root;
    }

    // ---- building ----

    private static GameObject BuildClipboard(GameObject donor)
    {
        var clone = UnityEngine.Object.Instantiate(donor, _root!.transform, false);
        clone.name = ClipboardPrefab;
        var drop = clone.GetComponent<ItemDrop>() ?? throw new InvalidOperationException("donor has no ItemDrop");

        // remember the hammer's shape in hand space before the model goes: the default grip is derived from it
        var attach = clone.transform.Find("attach");
        if (attach != null)
            _hammerBounds = LocalBounds(attach);
        _donorPiece ??= PieceMaterial(donor);
        _donorCreature ??= FirstMaterial(clone);
        _material ??= MakeMaterial(_donorPiece ?? _donorCreature);
        _creatureMaterial ??= MakeMaterial(_donorCreature);
        StripModel(clone, keepAttach: "attach");
        var shared = drop.m_itemData.m_shared;
        shared.m_name = ClipboardName;
        shared.m_description = "Lab clipboard. Hold it: left click notes down what you look at (its prefab, id and state), right click opens the clipboard: notes, spawn, go to, copy, repair, destroy.";
        shared.m_buildPieces = null;
        shared.m_useDurability = false;
        shared.m_toolTier = 0;
        shared.m_equipStatusEffect = null;
        shared.m_weight = 0.5f;
        shared.m_maxStackSize = 1;
        shared.m_icons = new[] { _clipboardIcon ??= GearArt.LoadIcon("clipboard_icon.png") };
        drop.m_itemData.m_dropPrefab = clone;
        DressClipboard(clone);
        return clone;
    }

    private static GameObject BuildGlasses(GameObject donor)
    {
        var clone = UnityEngine.Object.Instantiate(donor, _root!.transform, false);
        clone.name = GlassesPrefab;
        var drop = clone.GetComponent<ItemDrop>() ?? throw new InvalidOperationException("donor has no ItemDrop");
        _material ??= MakeMaterial(FirstMaterial(clone));
        _creatureMaterial ??= _material;
        StripModel(clone, keepAttach: null);
        var shared = drop.m_itemData.m_shared;
        shared.m_name = GlassesName;
        shared.m_description = "Black lab glasses. Worn in the utility slot they show a readout of whatever you look at: prefab, id, owner, health, contents, data.";
        shared.m_itemType = ItemDrop.ItemData.ItemType.Utility;
        shared.m_useDurability = false;
        shared.m_equipStatusEffect = null;
        shared.m_movementModifier = 0f;
        shared.m_weight = 0.2f;
        shared.m_maxStackSize = 1;
        shared.m_icons = new[] { _glassesIcon ??= GearArt.LoadIcon("glasses_icon.png") };
        drop.m_itemData.m_dropPrefab = clone;
        DressGlasses(clone);
        return clone;
    }

    /// <summary>Removes the donor's visuals: everything under "attach" (kept, emptied), every other child that renders, and equipoffset.</summary>
    private static void StripModel(GameObject clone, string? keepAttach)
    {
        var doomed = new List<GameObject>();
        foreach (Transform child in clone.transform)
        {
            if (keepAttach != null && child.name == keepAttach)
            {
                foreach (Transform grand in child)
                    doomed.Add(grand.gameObject);
                continue;
            }
            if (child.name == "equipoffset" || child.name.StartsWith("attach", StringComparison.Ordinal) || child.GetComponentInChildren<Renderer>(true) != null)
                doomed.Add(child.gameObject);
        }
        foreach (var go in doomed)
            UnityEngine.Object.DestroyImmediate(go);
        // some item prefabs draw their dropped model from the root object itself
        foreach (var c in clone.GetComponents<Component>())
            if (c is Renderer or MeshFilter or LODGroup)
                UnityEngine.Object.DestroyImmediate(c);
        if (keepAttach != null && clone.transform.Find(keepAttach) == null)
        {
            var attach = new GameObject(keepAttach);
            attach.transform.SetParent(clone.transform, false);
        }
    }

    /// <summary>
    /// A building material (Custom/Piece) from the hammer's own piece table: it lights like a wall and shows the atlas as it is,
    /// where the creature shader of weapons tints by its own colour maps.
    /// </summary>
    private static Material? PieceMaterial(GameObject hammer)
    {
        var table = hammer.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
        if (table == null || table.m_pieces == null)
            return null;
        foreach (var piece in table.m_pieces)
        {
            if (piece == null)
                continue;
            foreach (var r in piece.GetComponentsInChildren<MeshRenderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.shader != null && m.shader.name == "Custom/Piece")
                        return m;
        }
        return null;
    }

    /// <summary>A lit mesh material from the donor (its equip effect is a particle material: not that one).</summary>
    private static Material? FirstMaterial(GameObject go)
    {
        Material? any = null;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r is not MeshRenderer and not SkinnedMeshRenderer)
                continue;
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || m.shader == null)
                    continue;
                if (m.shader.name.StartsWith("Custom/", StringComparison.Ordinal) && !m.shader.name.Contains("Particle"))
                    return m;
                any ??= m;
            }
        }
        return any;
    }

    private static Material MakeMaterial(Material? donor)
    {
        // The piece shader lights like a building; a creature material would tint the atlas (the aprons learned that).
        // The game's shaders live in its bundles, so Shader.Find rarely works: the donor's own lit material is the safe base.
        var m = donor != null ? new Material(donor) : new Material(Shader.Find("Custom/Piece") ?? Shader.Find("Standard"));
        m.name = "LabGear_mat (" + m.shader.name + ")";
        TestLabPlugin.Log?.LogInfo($"Lab gear: material from {(donor != null ? donor.name : "none")} shader {m.shader.name}.");
        m.mainTexture = GearArt.LoadTexture("gear_atlas.png");
        // our UVs are plain 0..1 atlas coordinates; the donor's own tiling/offset (its wood or weapon texture's _MainTex_ST)
        // would otherwise repeat a slice of our atlas across the whole mesh instead of showing it as laid out
        m.mainTextureScale = Vector2.one;
        m.mainTextureOffset = Vector2.zero;
        foreach (var tex in new[] { "_BumpMap", "_MetallicGlossMap", "_EmissionMap", "_EmissiveTex", "_MetalTex" })
            if (m.HasProperty(tex))
                m.SetTexture(tex, null);
        foreach (var (name, value) in new[] { ("_Glossiness", 0.2f), ("_Metallic", 0f), ("_MetalGloss", 0.2f), ("_UseGlossmap", 0f), ("_EmissionColor", 0f) })
            if (m.HasProperty(name))
                m.SetFloat(name, value);
        if (m.HasProperty("_Color"))
            m.SetColor("_Color", Color.white);
        if (m.HasProperty("_EmissionColor"))
            m.SetColor("_EmissionColor", Color.black);
        // the creature shader multiplies its metal mask (white when the map is missing) by the weapon's steel-blue _MetalColor
        if (m.HasProperty("_MetalColor"))
            m.SetColor("_MetalColor", Color.white);
        foreach (var keyword in new[] { "_NORMALMAP", "_PARALLAXMAP", "_EMISSION", "_METALLICGLOSSMAP", "_DETAIL_MULX2" })
            m.DisableKeyword(keyword);
        foreach (var flag in new[] { "_UseStyles", "_TwoSidedNormals", "_AddSnow", "_AddRain" })
            if (m.HasProperty(flag))
                m.SetFloat(flag, 0f);
        return m;
    }

    private static Bounds? LocalBounds(Transform space)
    {
        Bounds? total = null;
        foreach (var filter in space.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
                continue;
            var toSpace = space.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var b = filter.sharedMesh.bounds;
            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                var p = toSpace.MultiplyPoint3x4(corner);
                if (total == null) total = new Bounds(p, Vector3.zero);
                else { var t = total.Value; t.Encapsulate(p); total = t; }
            }
        }
        return total;
    }

    // ---- dressing: the models on their joints (re-run by Reload) ----

    private static void DressAll()
    {
        if (_clipboard != null) DressClipboard(_clipboard);
        if (_glasses != null) DressGlasses(_glasses);
    }

    private static void DressClipboard(GameObject prefab)
    {
        var attach = prefab.transform.Find("attach") ?? throw new InvalidOperationException("clipboard has no attach");
        ClearChildren(attach);
        var mesh = GearMeshes.Clipboard();
        var (pos, rot) = ClipboardGrip();
        var model = AddModel(attach, "clipboard", mesh, pos, rot, ClipboardFit);
        model.SetActive(true);
        // the dropped item: the board lying face up
        AddDropModel(prefab, mesh, new Vector3(0f, 0.02f, 0f), Quaternion.Euler(-90f, 0f, 0f));
        FitCollider(prefab, new Vector3(0.24f, 0.05f, 0.33f));
        _fittedToPlayer = _playerRoot != null;
    }

    private static void DressGlasses(GameObject prefab)
    {
        foreach (Transform child in prefab.transform)
            if (child.name == "attach_" + HeadJoint)
                UnityEngine.Object.DestroyImmediate(child.gameObject);
        var attach = new GameObject("attach_" + HeadJoint);
        attach.transform.SetParent(prefab.transform, false);
        var mesh = GearMeshes.Glasses();
        var (pos, rot) = GlassesSeat();
        var model = AddModel(attach.transform, "glasses", mesh, pos, rot, GlassesFit, _creatureMaterial);
        model.SetActive(true);
        AddDropModel(prefab, mesh, new Vector3(0f, 0.02f, 0f), Quaternion.Euler(-90f, 0f, 0f));
        FitCollider(prefab, new Vector3(0.15f, 0.05f, 0.15f));
        _fittedToPlayer = _playerRoot != null;
    }

    private static GameObject AddModel(Transform parent, string name, Mesh mesh, Vector3 pos, Quaternion rot, Fit fit, Material? material = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        // computed placement, then the tuning offsets in the model's own space
        var local = Matrix4x4.TRS(pos, rot, Vector3.one) * Matrix4x4.TRS(fit.Pos, Quaternion.Euler(fit.Rot), Vector3.one * Mathf.Max(0.05f, fit.Scale));
        go.transform.localPosition = local.GetColumn(3);
        go.transform.localRotation = Quaternion.LookRotation(local.GetColumn(2), local.GetColumn(1));
        go.transform.localScale = Vector3.one * Mathf.Max(0.05f, fit.Scale);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material ?? _material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return go;
    }

    private static void AddDropModel(GameObject prefab, Mesh mesh, Vector3 pos, Quaternion rot)
    {
        var old = prefab.transform.Find("model");
        if (old != null)
            UnityEngine.Object.DestroyImmediate(old.gameObject);
        var go = new GameObject("model");
        go.transform.SetParent(prefab.transform, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = _material;
    }

    private static void FitCollider(GameObject prefab, Vector3 size)
    {
        var box = prefab.GetComponent<BoxCollider>();
        if (box == null)
            return;
        box.size = size;
        box.center = new Vector3(0f, size.y / 2f, 0f);
    }

    private static void ClearChildren(Transform t)
    {
        var doomed = new List<GameObject>();
        foreach (Transform child in t)
            doomed.Add(child.gameObject);
        foreach (var go in doomed)
            UnityEngine.Object.DestroyImmediate(go);
    }

    // ---- placement from the player's skeleton ----

    private static void ReadPlayerJoints(GameObject? player)
    {
        if (_playerRoot != null || player == null)
            return;
        _playerRoot = player.transform;
        var vis = player.GetComponent<VisEquipment>();
        _rightHand = vis != null ? vis.m_rightHand : null;
        _head = FindChild(player.transform, HeadJoint);
        _eye = AccessTools.Field(typeof(Player), "m_eye")?.GetValue(player.GetComponent<Player>()) as Transform;
        if (_rightHand == null) _rightHand = FindChild(player.transform, "RightHand");
        if (_head == null)
            TestLabPlugin.Log?.LogWarning($"Lab gear: the Player prefab has no '{HeadJoint}' joint; the glasses will hang at the joint origin.");
    }

    private static Transform? FindChild(Transform t, string name)
    {
        if (t.name == name)
            return t;
        foreach (Transform child in t)
        {
            var hit = FindChild(child, name);
            if (hit != null)
                return hit;
        }
        return null;
    }

    /// <summary>Default seat of the glasses in Head-joint space: a little above and in front of the joint, facing the way the player faces.</summary>
    private static (Vector3 pos, Quaternion rot) GlassesSeat()
    {
        if (_playerRoot == null || _head == null)
            return (new Vector3(0f, 0.06f, 0.09f), Quaternion.identity);
        // The game parents the attach object to the joint keeping world scale 1 (the joints themselves are scaled), so the
        // offset is only turned into the joint's frame, never scaled: metres in, metres out.
        var headInRoot = Quaternion.Inverse(_playerRoot.rotation) * _head.rotation;
        var toHead = Quaternion.Inverse(headInRoot);
        // the player's own eye point when the prefab has one (Player.m_eye), else a guess above and in front of the joint
        var offset = new Vector3(0f, 0.05f, 0.14f);
        if (_eye != null)
            offset = _playerRoot.InverseTransformPoint(_eye.position) - _playerRoot.InverseTransformPoint(_head.position) + new Vector3(0f, 0.005f, 0.075f);
        return (toHead * offset, toHead);
    }

    /// <summary>
    /// Default grip of the clipboard in right-hand space. Verified in game (orbit screenshots + a rotation sweep, 2026-11-11):
    /// the hand joint's own basis already holds the board upright with the paper facing outward, so the mesh needs no extra
    /// rotation; only a small offset so the grip sits near the clip (the top of the board, where a hand would naturally hold
    /// it) rather than the board's centre.
    /// </summary>
    private static (Vector3 pos, Quaternion rot) ClipboardGrip() => (new Vector3(0f, -0.1f, 0.05f), Quaternion.identity);

    // ---- tuning, reload, diagnostics ----

    private const string TuningFile = "gear.json";

    private static void LoadTuning()
    {
        var text = GearArt.ReadDevText(TuningFile);
        if (text == null)
            return;
        try
        {
            if (Cases.MiniJson.Parse(text) is not Dictionary<string, object?> json)
                return;
            ReadFit(json, "clipboard", ClipboardFit);
            ReadFit(json, "glasses", GlassesFit);
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Lab gear: {TuningFile} not read ({ex.Message}).");
        }
    }

    private static void ReadFit(Dictionary<string, object?> json, string key, Fit fit)
    {
        if (!json.TryGetValue(key, out var raw) || raw is not Dictionary<string, object?> d)
            return;
        fit.Pos = Vec(d, "pos", fit.Pos);
        fit.Rot = Vec(d, "rot", fit.Rot);
        if (d.TryGetValue("scale", out var s) && s != null)
            fit.Scale = Convert.ToSingle(s, CultureInfo.InvariantCulture);
    }

    private static Vector3 Vec(Dictionary<string, object?> d, string key, Vector3 fallback)
    {
        if (!d.TryGetValue(key, out var raw) || raw is not List<object?> list || list.Count < 3)
            return fallback;
        return new Vector3(Convert.ToSingle(list[0], CultureInfo.InvariantCulture), Convert.ToSingle(list[1], CultureInfo.InvariantCulture), Convert.ToSingle(list[2], CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Hot reload for iterating the look: re-reads the art and gear.json from the dev folder, rebuilds the models on both prefabs
    /// and refreshes what the local player holds and wears, so the new look shows without a restart. Returns what it did.
    /// Suites call it with "invoke": DrakesTestLab.Gear.LabGear.Reload.
    /// </summary>
    public static string Reload()
    {
        if (_clipboard == null || _glasses == null)
            return "lab gear not built";
        LoadTuning();
        var atlas = GearArt.LoadTexture("gear_atlas.png");
        if (_material != null)
            _material.mainTexture = atlas;
        if (_creatureMaterial != null)
            _creatureMaterial.mainTexture = atlas;
        DressAll();
        RefreshWorn();
        var note = $"Lab gear: reloaded (clipboard {ClipboardFit}; glasses {GlassesFit}; dev folder {GearArt.DevFolder}).";
        TestLabPlugin.Log?.LogInfo(note);
        return note;
    }

    /// <summary>Makes the player's VisEquipment re-attach the right-hand and utility models (it only re-attaches on a hash change).</summary>
    private static void RefreshWorn()
    {
        var player = Player.m_localPlayer;
        var vis = player != null ? player.GetComponent<VisEquipment>() : null;
        if (vis == null)
            return;
        foreach (var field in new[] { "m_currentRightItemHash", "m_currentUtilityItemHash" })
            AccessTools.Field(typeof(VisEquipment), field)?.SetValue(vis, 0);
    }

    /// <summary>Diagnostics for suites and the log: what was built, the joints found, the hammer's shape and the placements.</summary>
    public static string Describe()
    {
        var sb = new StringBuilder();
        sb.Append(_clipboard != null ? "clipboard built" : "clipboard missing").Append(" | ");
        sb.Append(_glasses != null ? "glasses built" : "glasses missing").Append(" | ");
        sb.Append($"player joints: hand={(_rightHand != null ? _rightHand.name : "none")} head={(_head != null ? _head.name : "none")}");
        if (_playerRoot != null && _head != null)
            sb.Append($" head in root {_playerRoot.InverseTransformPoint(_head.position):F3} scale {_head.lossyScale:F3} rot {(Quaternion.Inverse(_playerRoot.rotation) * _head.rotation).eulerAngles:F0}");
        if (_playerRoot != null && _eye != null)
            sb.Append($" eye in root {_playerRoot.InverseTransformPoint(_eye.position):F3}");
        if (_playerRoot != null && _rightHand != null)
            sb.Append($" hand in root {_playerRoot.InverseTransformPoint(_rightHand.position):F3} scale {_rightHand.lossyScale:F3}");
        sb.Append(" | ");
        if (_hammerBounds is { } hb)
            sb.Append($"hammer bounds center {hb.center:F3} size {hb.size:F3} | ");
        var (gp, gr) = GlassesSeat();
        sb.Append($"glasses seat {gp:F3} {gr.eulerAngles:F0} + {GlassesFit} | ");
        var (cp, cr) = ClipboardGrip();
        sb.Append($"clipboard grip {cp:F3} {cr.eulerAngles:F0} + {ClipboardFit}");
        var player = Player.m_localPlayer;
        if (player != null)
        {
            var worn = new List<string>();
            foreach (var r in player.GetComponentsInChildren<MeshRenderer>())
                if (r.sharedMaterial == _material || r.sharedMaterial == _creatureMaterial)
                {
                    var m = r.sharedMaterial;
                    var tex = m != null ? m.mainTexture : null;
                    var ours = _material != null ? _material.mainTexture : null;
                    worn.Add($"{r.name} at {player.transform.InverseTransformPoint(r.transform.position):F2} size {r.bounds.size:F2} lossy {r.transform.lossyScale:F2} enabled={r.enabled} active={r.gameObject.activeInHierarchy} visible={r.isVisible} layer={r.gameObject.layer} shader={(m != null ? m.shader.name : "none")} sameMat={m == _material} tex={(tex != null ? $"{tex.name}#{tex.GetInstanceID()} {tex.width}x{tex.height} mips={(tex as Texture2D)?.mipmapCount}" : "null")} sameTex={tex == ours} ourTex={(ours != null ? $"#{ours.GetInstanceID()}" : "null")} probes={r.lightProbeUsage} keywords=[{string.Join(" ", m != null ? m.shaderKeywords : Array.Empty<string>())}] (parent {r.transform.parent.name} scale {r.transform.parent.localScale:F2} / {r.transform.parent.parent?.name})");
                }
            sb.Append(" | worn: ").Append(worn.Count > 0 ? string.Join("; ", worn) : "none");
        }
        if (_clipboard != null)
        {
            var r = _clipboard.GetComponentInChildren<MeshRenderer>(true);
            var m = r != null ? r.sharedMaterial : null;
            sb.Append($" | prefab model: {(r != null ? r.name : "none")} shader={(m != null ? m.shader.name : "none")} tex={(m != null && m.mainTexture != null ? $"{m.mainTexture.name}#{m.mainTexture.GetInstanceID()} {m.mainTexture.width}x{m.mainTexture.height}" : "null")} keywords=[{string.Join(" ", m != null ? m.shaderKeywords : Array.Empty<string>())}]");
        }
        return sb.ToString();
    }

    /// <summary>Every property of a material and its value, for comparing a working game material with ours.</summary>
    public static string DumpMaterial(Material? m)
    {
        if (m == null)
            return "none";
        var sb = new StringBuilder();
        sb.Append(m.name).Append(" [").Append(m.shader.name).Append("] kw=[").Append(string.Join(" ", m.shaderKeywords)).Append("] q=").Append(m.renderQueue).Append(' ');
        var count = m.shader.GetPropertyCount();
        for (var i = 0; i < count; i++)
        {
            var name = m.shader.GetPropertyName(i);
            var type = m.shader.GetPropertyType(i);
            string value = type switch
            {
                UnityEngine.Rendering.ShaderPropertyType.Color => m.GetColor(name).ToString("F2"),
                UnityEngine.Rendering.ShaderPropertyType.Vector => m.GetVector(name).ToString("F2"),
                UnityEngine.Rendering.ShaderPropertyType.Float or UnityEngine.Rendering.ShaderPropertyType.Range => m.GetFloat(name).ToString("0.###", CultureInfo.InvariantCulture),
                UnityEngine.Rendering.ShaderPropertyType.Texture => m.GetTexture(name) is { } t ? $"{t.name}({t.width}x{t.height})" : "null",
                _ => "?"
            };
            sb.Append(name).Append('=').Append(value).Append(' ');
        }
        return sb.ToString();
    }

    public static string DumpMaterials() =>
        "OURS PIECE: " + DumpMaterial(_material) + "\nOURS CREATURE: " + DumpMaterial(_creatureMaterial) +
        "\nDONOR PIECE: " + DumpMaterial(_donorPiece) + "\nDONOR CREATURE: " + DumpMaterial(_donorCreature);

    /// <summary>
    /// Shader probe: five clipboards standing in a row 2 m in front of the player, left to right: our piece material, our
    /// creature material, the untouched donor piece material, the untouched donor creature material, and our piece material
    /// with the donor's own texture. clear = remove them again.
    /// </summary>
    public static string Probe(bool clear)
    {
        foreach (var go in _probes)
            if (go != null) UnityEngine.Object.Destroy(go);
        _probes.Clear();
        if (clear)
            return "cleared";
        var player = Player.m_localPlayer;
        if (player == null || _material == null)
            return "no player or material";
        var mesh = GearMeshes.Clipboard();
        var variants = new List<(string name, Material? mat)>
        {
            ("piece-ours", _material), ("creature-ours", _creatureMaterial), ("piece-donor", _donorPiece), ("creature-donor", _donorCreature)
        };
        if (_donorPiece != null)
        {
            var mixed = new Material(_material) { name = "piece-ours-donortex" };
            mixed.mainTexture = _donorPiece.mainTexture;
            variants.Add(("piece-ours-donortex", mixed));
        }
        var origin = player.transform.position + player.transform.forward * 2.5f + Vector3.up * 1.4f;
        var right = player.transform.right;
        var n = 0;
        foreach (var (name, mat) in variants)
        {
            if (mat == null) continue;
            var go = new GameObject("GearProbe_" + name);
            go.transform.position = origin + right * (n - (variants.Count - 1) / 2f) * 0.3f;
            go.transform.rotation = Quaternion.LookRotation(player.transform.forward, Vector3.up);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            _probes.Add(go);
            n++;
        }
        return $"{_probes.Count} probes: " + string.Join(", ", variants.ConvertAll(v => v.name));
    }

    // ---- giving and equipping ----

    public static void GiveAll()
    {
        ItemGiver.Give(ClipboardPrefab, 1);
        ItemGiver.Give(GlassesPrefab, 1);
    }

    /// <summary>Equips the clipboard and the glasses from the inventory (gives them first when missing). Returns what is now equipped.</summary>
    public static string EquipAll()
    {
        var player = Player.m_localPlayer;
        if (player == null)
            return "no player";
        foreach (var name in new[] { ClipboardPrefab, GlassesPrefab })
        {
            var item = FindInInventory(player, name);
            if (item == null)
            {
                ItemGiver.Give(name, 1);
                item = FindInInventory(player, name);
            }
            if (item != null && !item.m_equipped)
                player.EquipItem(item, true);
        }
        Clipboard.Refresh();
        return $"clipboard held={Clipboard.Held} glasses worn={Clipboard.Worn}";
    }

    public static ItemDrop.ItemData? FindInInventory(Player player, string prefab)
    {
        foreach (var item in player.GetInventory().GetAllItems())
            if (item.m_dropPrefab != null && item.m_dropPrefab.name == prefab)
                return item;
        return null;
    }
}
