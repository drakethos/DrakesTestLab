using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Cosmetics;

/// <summary>
/// The Drake's Lab coat: a chest-slot cosmetic built at runtime from the vanilla ArmorTunic1.
/// No Jotunn, no pack, no extra mod: the item is cloned into ObjectDB and ZNetScene by the patches below, and its
/// mesh (skinned to the tunic's own bones), textures and icon are embedded in this DLL (Cosmetics/Art).
/// The sleeves are painted onto the player's body (the armor material's _ChestTex); the coat itself is the mesh.
/// </summary>
internal static class LabCoat
{
    public const string PrefabName = "Drake_LabCoat";
    private const string DonorName = "ArmorTunic1";
    private const string DisplayName = "Lab Coat";

    private static GameObject? _prefab;
    private static GameObject? _root;
    private static bool _failed;

    /// <summary>Builds the item prefab once, from whichever donor list is at hand (ObjectDB or ZNetScene).</summary>
    internal static GameObject? EnsurePrefab(GameObject? donor)
    {
        if (_prefab != null)
            return _prefab;
        if (_failed || donor == null)
            return null;

        try
        {
            _prefab = Build(donor);
            TestLabPlugin.Log?.LogInfo($"Lab coat: built '{PrefabName}' from {DonorName}.");
        }
        catch (Exception ex)
        {
            _failed = true;
            TestLabPlugin.Log?.LogWarning($"Lab coat: could not build the item ({ex.GetType().Name}: {ex.Message}). It will be missing this session.");
        }

        return _prefab;
    }

    private static GameObject Build(GameObject donor)
    {
        // Prefabs live under an inactive root, like Jotunn's: clones are real prefabs, never scene objects.
        _root = new GameObject("DrakesTestLab_LabCoat");
        _root.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(_root);

        var clone = UnityEngine.Object.Instantiate(donor, _root.transform, false);
        clone.name = PrefabName;

        var donorDrop = donor.GetComponent<ItemDrop>() ?? throw new InvalidOperationException("donor has no ItemDrop");
        var skin = clone.transform.Find("attach_skin")?.GetComponentInChildren<SkinnedMeshRenderer>(true)
                   ?? throw new InvalidOperationException("donor has no attach_skin skinned mesh");
        _donorCoatMaterial = skin.sharedMaterial;
        _donorArmorMaterial = donorDrop.m_itemData.m_shared.m_armorMaterial
                              ?? throw new InvalidOperationException("donor has no armor material");
        Dress(clone);
        return clone;
    }

    private static Material? _donorCoatMaterial;
    private static Material? _donorArmorMaterial;

    /// <summary>Dev art folder: files here override the embedded art and are re-read by <see cref="Reload"/>.</summary>
    public static string DevFolder => Path.Combine(BepInEx.Paths.ConfigPath, "DrakesTestLab", "dev", "LabCoat");

    /// <summary>
    /// Hot reload for art iteration: rebuilds the coat's mesh, textures and materials on the live prefab from the dev
    /// folder (or the embedded art), so the next equip shows the new look without restarting the game.
    /// Suites call it first ("invoke": DrakesTestLab.Cosmetics.LabCoat.Reload). Returns what it loaded.
    /// </summary>
    public static string Reload()
    {
        if (_prefab == null)
            return "not built";
        Dress(_prefab);
        var dev = Directory.Exists(DevFolder) ? Directory.GetFiles(DevFolder).Length : 0;
        var note = $"Lab coat: reloaded ({(dev > 0 ? $"{dev} dev file(s) from {DevFolder}" : "embedded art")}).";
        TestLabPlugin.Log?.LogInfo(note);
        return note;
    }

    /// <summary>(Re)applies mesh, materials, textures and item data to the coat prefab.</summary>
    private static void Dress(GameObject clone)
    {
        var drop = clone.GetComponent<ItemDrop>()!;
        var skin = clone.transform.Find("attach_skin")!.GetComponentInChildren<SkinnedMeshRenderer>(true);

        var atlas = LoadTexture("atlas.png", linear: false);
        var atlasBump = LoadTexture("atlas_n.png", linear: true);
        var chest = LoadTexture("chest.png", linear: false);
        var chestBump = LoadTexture("chest_n.png", linear: true);
        var icon = LoadTexture("icon.png", linear: false, mips: false);

        // Coat mesh material: the tunic's own (Custom/Creature) with our atlas; no shine from the tunic's gloss map.
        var coatMaterial = new Material(_donorCoatMaterial) { name = "LabCoat_mat" };
        coatMaterial.mainTexture = atlas;
        SetTex(coatMaterial, "_BumpMap", atlasBump);
        SetTex(coatMaterial, "_MetallicGlossMap", null);
        SetFloat(coatMaterial, "_Metallic", 0f);
        SetFloat(coatMaterial, "_MetalGloss", 0.06f);   // Custom/Creature's smoothness (the tunic's 0.55 made the coat shine)
        SetFloat(coatMaterial, "_Glossiness", 0.06f);
        SetFloat(coatMaterial, "_UseGlossmap", 0f);
        SetFloat(coatMaterial, "_UseStyles", 0f);
        SetFloat(coatMaterial, "_TwoSidedNormals", 1f); // cloth is one surface: light its inside too (no dark patches)
        SetFloat(coatMaterial, "_Cull", 0f);

        // Sleeves: the body material Valheim paints onto the player while the item is worn (Custom/Player).
        var armorMaterial = new Material(_donorArmorMaterial) { name = "LabCoat_body_mat" };
        SetTex(armorMaterial, "_ChestTex", chest);
        SetTex(armorMaterial, "_ChestBumpMap", chestBump);
        // Valheim copies _ChestTex/_ChestBumpMap/_ChestMetal onto the body. The tunic has no metal map; give an explicit
        // "cloth" one (not metal, low smoothness) so the sleeves don't pick up whatever the shader defaults to.
        SetTex(armorMaterial, "_ChestMetal", SolidTexture("LabCoat_chest_metal", new Color(0f, 0f, 0f, 0.08f)));

        // Art is in prefab-root space; the skinned mesh needs the renderer's own space.
        var rootToMesh = skin.transform.worldToLocalMatrix * clone.transform.localToWorldMatrix;
        var (mesh, flatMesh) = BuildMeshes(skin, rootToMesh);
        skin.sharedMesh = mesh;
        skin.sharedMaterials = new[] { coatMaterial };
        skin.localBounds = mesh.bounds;

        // The dropped item (vanilla: a folded tunic "log") shows the coat lying face up.
        var log = clone.transform.Find("log");
        if (log != null)
        {
            var filter = log.GetComponent<MeshFilter>();
            var renderer = log.GetComponent<MeshRenderer>();
            if (filter != null && renderer != null)
            {
                filter.sharedMesh = flatMesh;
                renderer.sharedMaterials = new[] { coatMaterial };
                log.localScale = Vector3.one * 0.45f;
                log.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                log.localPosition = new Vector3(0f, 0.04f, 0.445f);
            }
        }

        var shared = drop.m_itemData.m_shared;
        shared.m_name = DisplayName;
        shared.m_description = "A crisp white lab coat, long sleeves and buttoned front. Purely cosmetic.";
        shared.m_armorMaterial = armorMaterial;
        shared.m_icons = new[] { Sprite.Create(icon, new Rect(0, 0, icon.width, icon.height), new Vector2(0.5f, 0.5f), 100f) };
        drop.m_itemData.m_dropPrefab = clone;
    }

    /// <summary>
    /// Test diagnostic: what the local player's body material really carries right now (shader, the chest textures
    /// VisEquipment copied from the worn armor, and the coat renderer's material). Suites log it with "invoke".
    /// </summary>
    public static string Describe()
    {
        var player = Player.m_localPlayer;
        var vis = player != null ? player.GetComponent<VisEquipment>() : null;
        var body = vis != null ? vis.m_bodyModel : null;
        if (body == null)
            return "no player body";
        var m = body.sharedMaterial;
        string Tex(Material mat, string p) => !mat.HasProperty(p) ? $"{p}=n/a" :
            mat.GetTexture(p) is { } t ? $"{p}={t.name}({t.width}x{t.height},{(t is Texture2D t2 ? t2.format.ToString() : "?")})" : $"{p}=null";
        var parts = new List<string>
        {
            $"body shader={m.shader.name} mat={m.name}",
            Tex(m, "_MainTex"), Tex(m, "_ChestTex"), Tex(m, "_ChestBumpMap"), Tex(m, "_ChestMetal"), Tex(m, "_LegsTex")
        };
        foreach (var r in player!.GetComponentsInChildren<SkinnedMeshRenderer>())
            if (r.sharedMesh != null && r.sharedMesh.name == "LabCoat")
                parts.Add($"coat shader={r.sharedMaterial.shader.name} {Tex(r.sharedMaterial, "_MainTex")} {Tex(r.sharedMaterial, "_BumpMap")} visible={r.isVisible}");
        return string.Join(" | ", parts);
    }

    // ---- mesh ----

    [Serializable]
    private sealed class MeshJson
    {
        public float[] positions = Array.Empty<float>();
        public float[] normals = Array.Empty<float>();
        public float[] uvs = Array.Empty<float>();
        public int[] triangles = Array.Empty<int>();
    }

    /// <summary>
    /// The art is authored in the prefab root's space (metres, Y up). A skinned mesh stores its vertices in the renderer's
    /// own local space (often centimetres or rotated, from the FBX import), so the skinned copy is converted with the tunic
    /// renderer's transform; the static copy (the dropped item) stays in root space.
    /// </summary>
    private static (Mesh skinned, Mesh flat) BuildMeshes(SkinnedMeshRenderer tunic, Matrix4x4 rootToMesh)
    {
        var json = JsonUtility.FromJson<MeshJson>(ReadText("mesh.json"));
        var count = json.positions.Length / 3;
        var vertices = new Vector3[count];
        var normals = new Vector3[count];
        var uvs = new Vector2[count];
        for (var i = 0; i < count; i++)
        {
            vertices[i] = new Vector3(json.positions[i * 3], json.positions[i * 3 + 1], json.positions[i * 3 + 2]);
            normals[i] = new Vector3(json.normals[i * 3], json.normals[i * 3 + 1], json.normals[i * 3 + 2]);
            uvs[i] = new Vector2(json.uvs[i * 2], json.uvs[i * 2 + 1]);
        }

        var flat = new Mesh { name = "LabCoat_static" };
        flat.vertices = vertices;
        flat.normals = normals;
        flat.uv = uvs;
        flat.triangles = json.triangles;
        flat.RecalculateBounds();

        var normalMatrix = rootToMesh.inverse.transpose;
        var meshVertices = new Vector3[count];
        var meshNormals = new Vector3[count];
        for (var i = 0; i < count; i++)
        {
            meshVertices[i] = rootToMesh.MultiplyPoint3x4(vertices[i]);
            meshNormals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
        }

        var triangles = (int[])json.triangles.Clone();
        if (rootToMesh.determinant < 0f)
            for (var t = 0; t + 2 < triangles.Length; t += 3)
                (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);

        var skinned = new Mesh { name = "LabCoat" };
        skinned.vertices = meshVertices;
        skinned.normals = meshNormals;
        skinned.uv = uvs;
        skinned.triangles = triangles;

        // Same bone list and bind poses as the tunic, so VisEquipment re-binds it to the player's skeleton unchanged.
        var bones = tunic.bones;
        var bind = tunic.sharedMesh.bindposes;
        skinned.bindposes = bind;
        skinned.boneWeights = Weigh(vertices, bones, bind, rootToMesh.inverse);
        skinned.RecalculateBounds();
        TestLabPlugin.Log?.LogInfo($"Lab coat: {count} verts, {bones.Length} bones; mesh-space bounds {skinned.bounds.min:F2}..{skinned.bounds.max:F2}; tunic mesh bounds {tunic.sharedMesh.bounds.min:F2}..{tunic.sharedMesh.bounds.max:F2}.");
        return (skinned, flat);
    }

    /// <summary>Height of the coat's hem in the art (prefab root space, metres): below the knee.</summary>
    private const float HemHeight = 0.43f;

    private static readonly string[] SpineChain ={ "Hips", "Spine", "Spine1", "Spine2", "Neck" };

    /// <summary>
    /// Skins the coat by height: each vertex blends the two spine bones around it (collar to the neck, skirt to the hips);
    /// below the hips the skirt also follows the nearer upper leg a little, so it doesn't stay rigid while walking.
    /// </summary>
    private static BoneWeight[] Weigh(Vector3[] vertices, Transform[] bones, Matrix4x4[] bind, Matrix4x4 meshToRoot)
    {
        // Bone origins in the same (prefab root) space as the vertices.
        var origin = new Vector3[bones.Length];
        for (var i = 0; i < bones.Length; i++)
            origin[i] = meshToRoot.MultiplyPoint3x4(bind[i].inverse.MultiplyPoint3x4(Vector3.zero));

        int Find(string name)
        {
            for (var i = 0; i < bones.Length; i++)
                if (bones[i] != null && bones[i].name == name)
                    return i;
            return -1;
        }

        var chain = new List<int>();
        foreach (var name in SpineChain)
            if (Find(name) is var idx and >= 0)
                chain.Add(idx);
        chain.Sort((a, b) => origin[a].y.CompareTo(origin[b].y));
        if (chain.Count == 0)
        {
            TestLabPlugin.Log?.LogWarning("Lab coat: no spine bones found in the tunic skeleton (" + string.Join(", ", Array.ConvertAll(bones, b => b != null ? b.name : "null")) + "); the coat will not follow the body.");
            chain.Add(0);
        }

        var hips = Find("Hips");
        var legs = new List<int>();
        foreach (var name in new[] { "LeftUpLeg", "RightUpLeg" })
            if (Find(name) is var idx and >= 0)
                legs.Add(idx);

        var weights = new BoneWeight[vertices.Length];
        for (var v = 0; v < vertices.Length; v++)
        {
            var p = vertices[v];
            var weight = new BoneWeight();
            int a, b;
            float t;
            if (p.y <= origin[chain[0]].y) { a = chain[0]; b = a; t = 0f; }
            else if (p.y >= origin[chain[chain.Count - 1]].y) { a = chain[chain.Count - 1]; b = a; t = 0f; }
            else
            {
                a = b = chain[0]; t = 0f;
                for (var k = 0; k < chain.Count - 1; k++)
                {
                    var lo = origin[chain[k]].y;
                    var hi = origin[chain[k + 1]].y;
                    if (p.y >= lo && p.y <= hi)
                    {
                        a = chain[k]; b = chain[k + 1];
                        t = Mathf.SmoothStep(0f, 1f, (p.y - lo) / Mathf.Max(1e-4f, hi - lo));
                        break;
                    }
                }
            }

            var legWeight = 0f;
            var leg = -1;
            if (hips >= 0 && legs.Count > 0 && a == hips && b == hips && p.y < origin[hips].y)
            {
                // The front of the coat is two panels (open below the last button): each follows its own thigh closely,
                // so a leg stepping forward pushes its panel instead of poking through it. The back follows less.
                var depth = Mathf.Pow(Mathf.Clamp01((origin[hips].y - p.y) / Mathf.Max(0.05f, origin[hips].y - HemHeight)), 0.4f);
                var side = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Abs(p.x) / 0.1f));
                var front = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((p.z - origin[hips].z) / 0.08f));
                legWeight = depth * Mathf.Lerp(0.3f * side, 0.95f, front);
                leg = legs[0];
                for (var k = 1; k < legs.Count; k++)
                    if (Mathf.Abs(origin[legs[k]].x - p.x) < Mathf.Abs(origin[leg].x - p.x))
                        leg = legs[k];
            }

            var rest = 1f - legWeight;
            weight.boneIndex0 = a; weight.weight0 = rest * (1f - t);
            weight.boneIndex1 = b; weight.weight1 = rest * t;
            if (a == b) { weight.weight0 = rest; weight.weight1 = 0f; }
            if (leg >= 0) { weight.boneIndex2 = leg; weight.weight2 = legWeight; }
            weights[v] = weight;
        }

        return weights;
    }

    // ---- embedded art ----

    private static byte[] ReadBytes(string name)
    {
        var dev = Path.Combine(DevFolder, name);
        if (File.Exists(dev))
            return File.ReadAllBytes(dev);
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("LabCoat/" + name)
                           ?? throw new FileNotFoundException("embedded resource LabCoat/" + name);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string ReadText(string name) => System.Text.Encoding.UTF8.GetString(ReadBytes(name));

    private static readonly MethodInfo LoadImageMethod =
        Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) })
        ?? throw new MissingMethodException("UnityEngine.ImageConversion.LoadImage");

    private static Texture2D LoadTexture(string name, bool linear, bool mips = true)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mips, linear) { name = "LabCoat_" + name };
        // ImageConversion.LoadImage lives in a module this project doesn't reference (it needs netstandard 2.1): call it by reflection.
        var loaded = LoadImageMethod.Invoke(null, new object[] { texture, ReadBytes(name), false });
        if (loaded is not true)
            throw new InvalidDataException("could not decode LabCoat/" + name);
        texture.wrapMode = TextureWrapMode.Clamp;
        return texture;
    }

    private static Texture2D SolidTexture(string name, Color color)
    {
        var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = name };
        var pixels = new Color[16];
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = color;
        texture.SetPixels(pixels);
        texture.Apply(false, false);
        return texture;
    }

    private static void SetTex(Material m, string property, Texture? texture)
    {
        if (m.HasProperty(property))
            m.SetTexture(property, texture);
    }

    private static void SetFloat(Material m, string property, float value)
    {
        if (m.HasProperty(property))
            m.SetFloat(property, value);
    }

    // ---- registration (no Jotunn): ObjectDB for the item, ZNetScene so it can be spawned and synced ----

    /// <summary>[Experimental] LabCoat (default off): the coat is a work in progress.</summary>
    private static bool Enabled => Bootstrap.TestLabConfig.Instance?.LabCoat?.Value == true;

    internal static void RegisterIn(ObjectDB db)
    {
        if (!Enabled || db == null || db.m_items == null || db.m_items.Count == 0)
            return;
        var prefab = EnsurePrefab(db.m_items.Find(p => p != null && p.name == DonorName));
        if (prefab == null)
            return;
        if (!db.m_items.Contains(prefab))
            db.m_items.Add(prefab);
        // Private in the live game (the build-time reference assemblies expose it): reach it with AccessTools.
        if (AccessTools.Field(typeof(ObjectDB), "m_itemByHash")?.GetValue(db) is Dictionary<int, GameObject> byHash)
            byHash[PrefabName.GetStableHashCode()] = prefab;
    }

    internal static void RegisterIn(ZNetScene scene)
    {
        if (!Enabled || scene == null || scene.m_prefabs == null || scene.m_prefabs.Count == 0)
            return;
        var prefab = EnsurePrefab(scene.m_prefabs.Find(p => p != null && p.name == DonorName));
        if (prefab == null)
            return;
        if (!scene.m_prefabs.Contains(prefab))
            scene.m_prefabs.Add(prefab);
        if (AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs")?.GetValue(scene) is Dictionary<int, GameObject> named)
            named[PrefabName.GetStableHashCode()] = prefab;
    }
}

[HarmonyPatch(typeof(ObjectDB), "Awake")]
internal static class LabCoat_ObjectDB_Awake_Patch
{
    private static void Postfix(ObjectDB __instance) => LabCoat.RegisterIn(__instance);
}

[HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
internal static class LabCoat_ObjectDB_CopyOtherDB_Patch
{
    private static void Postfix(ObjectDB __instance) => LabCoat.RegisterIn(__instance);
}

[HarmonyPatch(typeof(ZNetScene), "Awake")]
internal static class LabCoat_ZNetScene_Awake_Patch
{
    private static void Postfix(ZNetScene __instance) => LabCoat.RegisterIn(__instance);
}
