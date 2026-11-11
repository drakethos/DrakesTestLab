using System.Collections.Generic;
using UnityEngine;

namespace DrakesTestLab.Gear;

/// <summary>
/// The lab gear models, built from boxes at runtime (no asset bundle): a clipboard (board, clip, lined paper) and a pair of
/// black glasses. All UVs point into gear_atlas.png (see Gear/Art/make_art.py for the layout).
/// </summary>
internal static class GearMeshes
{
    // atlas regions (pixel rects in a 512 px, y-down image -> UV rects, y-up)
    private static Rect Px(int x0, int y0, int x1, int y1) => new(x0 / 512f, 1f - y1 / 512f, (x1 - x0) / 512f, (y1 - y0) / 512f);
    private static readonly Rect Paper = Px(2, 2, 298, 418);
    private static readonly Rect PaperBlank = Px(250, 380, 294, 414);
    private static readonly Rect Board = Px(324, 4, 508, 196);
    private static readonly Rect Clip = Px(324, 224, 508, 296);
    private static readonly Rect Black = Px(324, 324, 412, 412);
    private static readonly Rect Lens = Px(420, 324, 508, 412);

    /// <summary>A clipboard in its own space: x right, y up (clip at the top), z = the front of the board (paper side).</summary>
    public static Mesh Clipboard()
    {
        var b = new Builder("Drake_Clipboard");
        b.Box(new Vector3(0f, 0f, 0f), new Vector3(0.23f, 0.32f, 0.006f), Board);
        b.Box(new Vector3(0f, -0.008f, 0.0036f), new Vector3(0.21f, 0.29f, 0.0012f), PaperBlank, Paper);
        b.Box(new Vector3(0f, 0.147f, 0.004f), new Vector3(0.076f, 0.03f, 0.013f), Clip);
        b.Box(new Vector3(0f, 0.158f, 0.0125f), new Vector3(0.044f, 0.012f, 0.005f), Clip);
        b.Box(new Vector3(0f, 0.138f, 0.0125f), new Vector3(0.06f, 0.006f, 0.004f), Clip);
        return b.Build();
    }

    /// <summary>Glasses in their own space: origin between the eyes, x right, y up, z forward (away from the face).</summary>
    public static Mesh Glasses()
    {
        var b = new Builder("Drake_LabGlasses");
        const float frameW = 0.048f, frameH = 0.032f, bar = 0.005f, depth = 0.006f, eye = 0.0325f;
        foreach (var side in new[] { -1f, 1f })
        {
            var cx = side * eye;
            b.Box(new Vector3(cx, frameH / 2f, 0f), new Vector3(frameW, bar, depth), Black);
            b.Box(new Vector3(cx, -frameH / 2f, 0f), new Vector3(frameW, bar, depth), Black);
            b.Box(new Vector3(cx - frameW / 2f, 0f, 0f), new Vector3(bar, frameH, depth), Black);
            b.Box(new Vector3(cx + frameW / 2f, 0f, 0f), new Vector3(bar, frameH, depth), Black);
            b.Box(new Vector3(cx, 0f, -0.0005f), new Vector3(frameW - bar, frameH - bar, 0.002f), Lens);
            // temple: from the outer hinge straight back along the head, with a hook down behind the ear
            var hinge = side * (eye + frameW / 2f + 0.0015f);
            b.Box(new Vector3(hinge, 0.006f, -0.064f), new Vector3(0.004f, 0.006f, 0.128f), Black);
            b.Box(new Vector3(hinge, -0.004f, -0.127f), new Vector3(0.003f, 0.022f, 0.003f), Black);
        }
        b.Box(new Vector3(0f, 0.006f, 0f), new Vector3(eye * 2f - frameW + 0.002f, bar, bar), Black);
        return b.Build();
    }

    private sealed class Builder
    {
        private readonly string _name;
        private readonly List<Vector3> _v = new();
        private readonly List<Vector3> _n = new();
        private readonly List<Vector2> _uv = new();
        private readonly List<int> _t = new();

        public Builder(string name) => _name = name;

        /// <summary>An axis-aligned box. Every face gets <paramref name="uv"/>; the +z face gets <paramref name="front"/> when given.</summary>
        public void Box(Vector3 center, Vector3 size, Rect uv, Rect? front = null)
        {
            var h = size / 2f;
            Face(center, new Vector3(0, 0, 1), new Vector3(1, 0, 0), new Vector3(0, 1, 0), h.z, h.x, h.y, front ?? uv);
            Face(center, new Vector3(0, 0, -1), new Vector3(-1, 0, 0), new Vector3(0, 1, 0), h.z, h.x, h.y, uv);
            Face(center, new Vector3(1, 0, 0), new Vector3(0, 0, -1), new Vector3(0, 1, 0), h.x, h.z, h.y, uv);
            Face(center, new Vector3(-1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 1, 0), h.x, h.z, h.y, uv);
            Face(center, new Vector3(0, 1, 0), new Vector3(1, 0, 0), new Vector3(0, 0, -1), h.y, h.x, h.z, uv);
            Face(center, new Vector3(0, -1, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 1), h.y, h.x, h.z, uv);
        }

        private void Face(Vector3 center, Vector3 normal, Vector3 right, Vector3 up, float offset, float halfW, float halfH, Rect uv)
        {
            var c = center + normal * offset;
            var i = _v.Count;
            _v.Add(c - right * halfW - up * halfH);
            _v.Add(c + right * halfW - up * halfH);
            _v.Add(c + right * halfW + up * halfH);
            _v.Add(c - right * halfW + up * halfH);
            for (var k = 0; k < 4; k++)
                _n.Add(normal);
            _uv.Add(new Vector2(uv.xMin, uv.yMin));
            _uv.Add(new Vector2(uv.xMax, uv.yMin));
            _uv.Add(new Vector2(uv.xMax, uv.yMax));
            _uv.Add(new Vector2(uv.xMin, uv.yMax));
            // counter-clockwise seen from the outside (Unity is left-handed: clockwise in front view is front-facing)
            _t.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
        }

        public Mesh Build()
        {
            var mesh = new Mesh { name = _name };
            mesh.SetVertices(_v);
            mesh.SetNormals(_n);
            mesh.SetUVs(0, _uv);
            mesh.SetTriangles(_t, 0);
            // the game's shaders multiply by vertex colour: a mesh without one must not come out black
            var colours = new List<Color>(_v.Count);
            for (var i = 0; i < _v.Count; i++)
                colours.Add(Color.white);
            mesh.SetColors(colours);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
