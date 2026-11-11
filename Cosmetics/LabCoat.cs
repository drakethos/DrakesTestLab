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
        if (coatMaterial.HasProperty("_Color"))
            coatMaterial.SetColor("_Color", new Color(0.80f, 0.80f, 0.80f, 1f)); // pure white blows out in daylight; keeps seams and buttons readable
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

    /// <summary>Gives the coat to the local player's inventory (not equipped, same as the lab gear's give-on-spawn).</summary>
    public static int Give() => Util.ItemGiver.Give(PrefabName, 1);

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

        // The standard way to fit clothes to a skinned character: put the garment in the body's own bind space, copy the
        // body's bone weights from the nearest skin (so cloth and skin bend together in every pose), and push any cloth
        // vertex that sits inside the skin back out along the skin normal. VisEquipment binds attach_skin to the body's
        // bones array, so the body's bind poses are exactly the ones the coat must use.
        var fit = FitToBody(tunic, vertices, normals, rootToMesh);
        if (fit != null)
        {
            skinned.vertices = fit.Value.vertices;
            skinned.normals = fit.Value.normals;
            var tris = (int[])json.triangles.Clone();
            if (fit.Value.mirrored)
                for (var t = 0; t + 2 < tris.Length; t += 3)
                    (tris[t + 1], tris[t + 2]) = (tris[t + 2], tris[t + 1]);
            skinned.triangles = tris;
            skinned.bindposes = fit.Value.bindposes;
            skinned.boneWeights = fit.Value.weights;
        }
        else
        {
            // No readable body: fall back to the height-based spine weights in the tunic's own space.
            skinned.vertices = meshVertices;
            skinned.normals = meshNormals;
            skinned.triangles = triangles;
            var bind = tunic.sharedMesh.bindposes;
            skinned.bindposes = bind;
            skinned.boneWeights = Weigh(vertices, tunic.bones, bind, rootToMesh.inverse);
        }

        // set after vertices on both branches: Unity validates a mesh's per-vertex arrays against its current vertex count,
        // and uvs is index-aligned with vertices/meshVertices/fit.Value.vertices either way (the fit only moves positions).
        skinned.uv = uvs;

        _fitted = fit != null;
        skinned.RecalculateBounds();
        TestLabPlugin.Log?.LogInfo($"Lab coat: {count} verts, fit={(fit != null ? fit.Value.note : "height weights (no body)")}; mesh-space bounds {skinned.bounds.min:F2}..{skinned.bounds.max:F2}.");
        return (skinned, flat);
    }

    /// <summary>Minimum gap between skin and cloth in the bind pose (metres).</summary>
    private const float SkinGap = 0.009f;

    /// <summary>Largest push-out the fit applies (metres).</summary>
    private const float MaxPush = 0.05f;

    /// <summary>Body vertices sampled for each coat vertex (weights and push-out are blended over them).</summary>
    private const int Neighbours = 6;

    private static SkinnedMeshRenderer? _bodySource;
    private static bool _fitted;

    /// <summary>The player body every coat is fitted to: the Player prefab's VisEquipment body model.</summary>
    private static SkinnedMeshRenderer? BodySource()
    {
        if (_bodySource != null)
            return _bodySource;
        var player = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Player") : null;
        _bodySource = player != null ? player.GetComponent<VisEquipment>()?.m_bodyModel : null;
        return _bodySource;
    }

    internal static void SetBodySource(GameObject? playerPrefab)
    {
        if (_bodySource == null && playerPrefab != null)
            _bodySource = playerPrefab.GetComponent<VisEquipment>()?.m_bodyModel;
    }

    private struct Fit
    {
        public Vector3[] vertices;
        public Vector3[] normals;
        public Matrix4x4[] bindposes;
        public BoneWeight[] weights;
        public string note;
        public bool mirrored;
    }

    private static Fit? FitToBody(SkinnedMeshRenderer tunic, Vector3[] rootVerts, Vector3[] rootNormals, Matrix4x4 rootToMesh)
    {
        var body = BodySource();
        var bodyMesh = body != null ? body.sharedMesh : null;
        if (body == null || bodyMesh == null)
            return null;
        if (!bodyMesh.isReadable)
        {
            TestLabPlugin.Log?.LogWarning("Lab coat: the player body mesh is not readable; using height weights.");
            return null;
        }

        var bodyVerts = bodyMesh.vertices;
        var bodyNormals = bodyMesh.normals;
        var bodyWeights = bodyMesh.boneWeights;
        var bodyBind = bodyMesh.bindposes;
        var bodyBones = body.bones;
        if (bodyVerts.Length == 0 || bodyWeights.Length != bodyVerts.Length || bodyBones.Length != bodyBind.Length)
            return null;

        // Tunic mesh space -> body mesh space through a bone both rigs share (bind poses map mesh space to bone space).
        var tunicBones = tunic.bones;
        var tunicBind = tunic.sharedMesh.bindposes;
        int IndexOf(Transform[] list, string name)
        {
            for (var i = 0; i < list.Length; i++)
                if (list[i] != null && list[i].name == name)
                    return i;
            return -1;
        }
        var th = IndexOf(tunicBones, "Hips");
        var bh = IndexOf(bodyBones, "Hips");
        if (th < 0 || bh < 0 || th >= tunicBind.Length)
            return null;

        // Art (prefab root space, metres) -> body mesh space. Two ways to get there; keep the one where the coat lies on
        // the skin (the art was fitted to the body exported from the Player prefab, so the right one is ~2 cm away).
        var viaBind = bodyBind[bh].inverse * tunicBind[th] * rootToMesh;
        var viaPlayer = body.transform.worldToLocalMatrix * body.transform.root.localToWorldMatrix;
        float Median(Matrix4x4 m)
        {
            var scale = m.MultiplyVector(Vector3.up).magnitude;
            var list = new List<float>();
            for (var i = 0; i < rootVerts.Length; i += Mathf.Max(1, rootVerts.Length / 200))
            {
                var p = m.MultiplyPoint3x4(rootVerts[i]);
                var best = float.MaxValue;
                foreach (var b in bodyVerts)
                    best = Mathf.Min(best, (b - p).sqrMagnitude);
                list.Add(Mathf.Sqrt(best) / scale);
            }
            list.Sort();
            return list[list.Count / 2];
        }
        var dBind = Median(viaBind);
        var dPlayer = Median(viaPlayer);
        var toBody = dPlayer <= dBind ? viaPlayer : viaBind;
        var mapping = $"{(dPlayer <= dBind ? "player" : "bind")} space (median skin distance: player {dPlayer * 100f:F1} cm, bind {dBind * 100f:F1} cm)";

        // One root-space metre measured in body mesh units.
        var unitsPerMetre = toBody.MultiplyVector(Vector3.up).magnitude;
        var gap = SkinGap * unitsPerMetre;
        var normalMatrix = toBody.inverse.transpose;
        var tunicVerts = rootVerts;
        var tunicNormals = rootNormals;

        var count = tunicVerts.Length;
        var outVerts = new Vector3[count];
        var outNormals = new Vector3[count];
        var weights = new BoneWeight[count];
        var cache = new Dictionary<Vector3, (Vector3 pos, BoneWeight w)>();
        var nearIdx = new int[Neighbours];
        var nearD = new float[Neighbours];
        var acc = new Dictionary<int, float>();
        float pushed = 0f, worst = 0f;
        var pushedCount = 0;

        for (var v = 0; v < count; v++)
        {
            var p = toBody.MultiplyPoint3x4(tunicVerts[v]);
            var n = normalMatrix.MultiplyVector(tunicNormals[v]).normalized;
            outNormals[v] = n;
            var key = new Vector3(Mathf.Round(p.x * 1e4f), Mathf.Round(p.y * 1e4f), Mathf.Round(p.z * 1e4f));
            if (cache.TryGetValue(key, out var hit))
            {
                outVerts[v] = hit.pos;
                weights[v] = hit.w;
                continue;
            }

            // k nearest skin vertices that face the same way as the cloth (keeps the torso from grabbing arm skin).
            for (var k = 0; k < Neighbours; k++) { nearIdx[k] = -1; nearD[k] = float.MaxValue; }
            for (var b = 0; b < bodyVerts.Length; b++)
            {
                var d = (bodyVerts[b] - p).sqrMagnitude;
                if (d >= nearD[Neighbours - 1] || Vector3.Dot(bodyNormals[b], n) < 0.2f)
                    continue;
                var k = Neighbours - 1;
                while (k > 0 && nearD[k - 1] > d) { nearD[k] = nearD[k - 1]; nearIdx[k] = nearIdx[k - 1]; k--; }
                nearD[k] = d; nearIdx[k] = b;
            }

            if (nearIdx[0] < 0)
            {
                outVerts[v] = p;
                weights[v] = new BoneWeight { boneIndex0 = bh, weight0 = 1f };
                cache[key] = (p, weights[v]);
                continue;
            }

            // Inverse-distance blend of the neighbours' weights, normal and position.
            acc.Clear();
            var skinPos = Vector3.zero;
            var skinNormal = Vector3.zero;
            var total = 0f;
            for (var k = 0; k < Neighbours && nearIdx[k] >= 0; k++)
            {
                var w = 1f / (Mathf.Sqrt(nearD[k]) + 0.002f * unitsPerMetre);
                total += w;
                skinPos += bodyVerts[nearIdx[k]] * w;
                skinNormal += bodyNormals[nearIdx[k]] * w;
                var bw = bodyWeights[nearIdx[k]];
                Add(acc, bw.boneIndex0, bw.weight0 * w);
                Add(acc, bw.boneIndex1, bw.weight1 * w);
                Add(acc, bw.boneIndex2, bw.weight2 * w);
                Add(acc, bw.boneIndex3, bw.weight3 * w);
            }
            skinPos /= total;
            skinNormal = skinNormal.normalized;

            // Push out of the skin: the cloth must sit at least `gap` above the nearest skin vertex's tangent plane (on a
            // low-poly body the flat triangles lie under that plane, so this is the safe side). Moves beyond MaxPush mean
            // the neighbour is the wrong piece of skin (a hand, the other leg): leave those alone.
            var nearest = bodyVerts[nearIdx[0]];
            var nearestNormal = bodyNormals[nearIdx[0]];
            var height = Vector3.Dot(p - nearest, nearestNormal);
            if (height < gap && gap - height < MaxPush * unitsPerMetre)
            {
                var move = gap - height;
                skinNormal = nearestNormal;
                p += skinNormal * move;
                pushed += move; pushedCount++;
                worst = Mathf.Max(worst, move);
            }

            weights[v] = TopFour(acc);
            outVerts[v] = p;
            cache[key] = (p, weights[v]);
        }

        // Push-outs move single vertices; neighbours that share a position already moved together through the cache.
        var note = $"body weights, {mapping} ({bodyVerts.Length} skin verts, {cache.Count} cloth points, pushed {pushedCount} out of the skin, worst {worst / unitsPerMetre * 100f:F1} cm, avg {(pushedCount > 0 ? pushed / pushedCount / unitsPerMetre * 100f : 0f):F1} cm)";
        return new Fit { vertices = outVerts, normals = outNormals, bindposes = bodyBind, weights = weights, note = note, mirrored = toBody.determinant < 0f };
    }

    private static void Add(Dictionary<int, float> acc, int bone, float weight)
    {
        if (weight <= 0f)
            return;
        acc.TryGetValue(bone, out var have);
        acc[bone] = have + weight;
    }

    private static BoneWeight TopFour(Dictionary<int, float> acc)
    {
        var list = new List<KeyValuePair<int, float>>(acc);
        list.Sort((a, b) => b.Value.CompareTo(a.Value));
        var sum = 0f;
        for (var i = 0; i < list.Count && i < 4; i++)
            sum += list[i].Value;
        var bw = new BoneWeight();
        float W(int i) => i < list.Count ? list[i].Value / sum : 0f;
        int I(int i) => i < list.Count ? list[i].Key : 0;
        bw.boneIndex0 = I(0); bw.weight0 = W(0);
        bw.boneIndex1 = I(1); bw.weight1 = W(1);
        bw.boneIndex2 = I(2); bw.weight2 = W(2);
        bw.boneIndex3 = I(3); bw.weight3 = W(3);
        return bw;
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

    /// <summary>On by default, part of the standard kit: [Experimental] LabCoat in the TestLab config.</summary>
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
        SetBodySource(scene.m_prefabs.Find(p => p != null && p.name == "Player"));
        var prefab = EnsurePrefab(scene.m_prefabs.Find(p => p != null && p.name == DonorName));
        if (prefab == null)
            return;
        if (!_fitted && _bodySource != null)
            Dress(prefab); // built earlier (ObjectDB first) without the body: fit it now
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
