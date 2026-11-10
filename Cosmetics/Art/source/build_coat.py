# Builds the lab coat mesh around the Valheim player body (object "Visual.body_0", imported from Forge's
# export-glb Player --worn) and writes the mesh JSON TestLab embeds. Run inside Blender:
#   exec(open(r"C:\Modding\LabCoat\build_coat.py").read())
# Space: Blender X = Unity X, Blender Y = Unity Z (front of the character = +Y), Blender Z = Unity Y (up).
import bpy, bmesh, math, json, os
import numpy as np
from mathutils import Vector

ART = r'C:\Modding\Vahleim\Mod\DrakesWorkshop\DrakesTestLab\Cosmetics\Art'

# ---------------- tuning ----------------
NSEG = 32
# Shirt length: the hem sits at the top of the hips, so the legs never push through it.
ZS = [1.625, 1.605, 1.585, 1.56, 1.53, 1.50, 1.47, 1.44, 1.41, 1.38, 1.34, 1.30, 1.26, 1.21, 1.16, 1.11, 1.06, 1.02, 0.99, 0.96, 0.93]
SLIT = 0.0           # no open skirt: buttoned to the hem
HIPS_Z = 0.97
TOP_BUTTON = 1.27    # above it the coat opens in a V and the blue undershirt (painted on the body) shows
BUTTONS = (1.27, 1.17, 1.07, 0.97)
FLARE = 1.0
V_TOP = 0.68        # half-width (radians) of the V opening at the collar


def v_half(z):
    """Half-width of the front V opening at height z (0 at the top button)."""
    return V_TOP * min(1.0, max(0.0, (z - TOP_BUTTON) / (ZS[0] - TOP_BUTTON)))


def v_warp(phi, z, side):
    """Ring angle with the front V cut out: angles squeeze away from the centre line by v_half(z)."""
    w = v_half(z)
    if w <= 0: return phi
    s = side if phi == 0 else (1 if phi > 0 else -1)
    return s * (w + abs(phi) * (math.pi - w) / math.pi)


def clearance(z):
    """Design gap between body and coat (m). The game also skins the coat with the body's own weights and pushes
    any vertex that ends up inside the skin back out, so this only shapes the drape: snug like a shirt."""
    if z >= 1.60: return 0.012   # neck
    if z >= 1.40: return 0.018   # chest/shoulders
    if z >= 1.12: return 0.022   # belly
    return 0.024                 # hips


def xlim(z):
    """Body vertices further out than this are arms (excluded from the torso fit)."""
    if z >= 1.60: return 0.10
    if z >= 1.50: return 0.25
    if z >= 0.92: return 0.235
    return 0.32


# ---------------- fit ----------------
body = bpy.data.objects["Visual.body_0"]
BV = np.array([tuple(body.matrix_world @ v.co) for v in body.data.vertices], dtype=np.float64)
ZMAX, ZMIN = ZS[0], ZS[-1]
from mathutils.bvhtree import BVHTree
_dg = bpy.context.evaluated_depsgraph_get()
_bm = bmesh.new(); _bm.from_mesh(body.data); _bm.transform(body.matrix_world)
TREE = BVHTree.FromBMesh(_bm); _bm.free()
CY = []; R = []
for z in ZS:
    def outer_hit(origin_xy, d, zz, lim):
        """First torso skin met by a ray coming from outside towards the axis (internal faces and arms skipped)."""
        o = Vector((origin_xy[0] + d.x * 0.8, origin_xy[1] + d.y * 0.8, zz)); back = -d
        for _ in range(12):
            loc, nrm, idx_, dist = TREE.ray_cast(o, back, 1.6)
            if loc is None: return None
            if abs(loc.x) <= lim: return loc
            o = loc + back * 1e-4                           # an arm: keep going inwards
        return None
    # centre the axis between the front and back skin
    f = outer_hit((0.0, 0.0), Vector((0, 1, 0)), z, 1.0); bk = outer_hit((0.0, 0.0), Vector((0, -1, 0)), z, 1.0)
    cy = (f.y + bk.y) / 2 if f is not None and bk is not None else 0.0
    # Skin distance per angle, from outside in: the shirt follows the outer chest/back/waist envelope.
    row = []
    for j in range(NSEG):
        phi = 2 * math.pi * j / NSEG - math.pi
        d = Vector((math.sin(phi), math.cos(phi), 0.0))
        hits = []
        for dz in (-0.015, 0.0, 0.015):                     # a little vertical spread smooths small dents
            loc = outer_hit((0.0, cy), d, z + dz, xlim(z) + 0.02)
            if loc is not None: hits.append(math.hypot(loc.x, loc.y - cy))
        row.append(max(hits) if hits else np.nan)
    row = np.array(row); idx = np.arange(NSEG); ok = ~np.isnan(row)
    row = np.interp(idx, idx[ok], row[ok], period=NSEG) if ok.any() else np.full(NSEG, 0.2)
    sm = (np.roll(row, 1) + 2 * row + np.roll(row, -1)) / 4
    row = np.maximum(row, sm)                              # cloth bridges dents, never dips into the skin
    R.append(row + clearance(z)); CY.append(cy)
R = np.array(R); CY = np.array(CY)
for i in range(1, len(ZS)):
    if ZS[i] < HIPS_Z:
        R[i] = np.maximum(R[i], R[i - 1] * FLARE); CY[i] = CY[i - 1]
R2 = R.copy()
for i in range(1, len(ZS) - 1): R2[i] = np.maximum(R[i], (R[i - 1] + 2 * R[i] + R[i + 1]) / 4)
R = R2


def rad_at(z, phi, off=0.0):
    zi = np.interp(z, ZS[::-1], np.arange(len(ZS))[::-1])
    i0 = int(math.floor(zi)); i1 = min(i0 + 1, len(ZS) - 1); tz = zi - i0
    fj = ((phi + math.pi) / (2 * math.pi) * NSEG) % NSEG
    j0 = int(math.floor(fj)) % NSEG; j1 = (j0 + 1) % NSEG; tj = fj - math.floor(fj)
    r = (R[i0, j0] * (1 - tj) + R[i0, j1] * tj) * (1 - tz) + (R[i1, j0] * (1 - tj) + R[i1, j1] * tj) * tz
    return r + off, CY[i0] * (1 - tz) + CY[i1] * tz


def surf(z, phi, off=0.0):
    r, cy = rad_at(z, phi, off)
    return Vector((r * math.sin(phi), cy + r * math.cos(phi), z))


def uv_body(j, z): return (0.75 * j / NSEG, 0.30 + 0.70 * (z - ZMIN) / (ZMAX - ZMIN))


# ---------------- mesh ----------------
old = bpy.data.objects.get("LabCoat")
if old: bpy.data.objects.remove(old)
bm = bmesh.new(); uvl = bm.loops.layers.uv.new("UVMap")


def face(verts, uvs):
    f = bm.faces.new(verts)
    for l, uv in zip(f.loops, uvs): l[uvl].uv = uv
    return f


FRONT = NSEG // 2
rings = []; right = []
for z in ZS:
    ring = [bm.verts.new(surf(z, v_warp(2 * math.pi * j / NSEG - math.pi, z, -1))) for j in range(NSEG)]
    ring.append(bm.verts.new(ring[0].co))           # back seam duplicate (UV wrap)
    rings.append(ring)
    # the centre-front vertex splits into a left and right edge where the V is open
    right.append(bm.verts.new(surf(z, v_warp(0.0, z, 1))) if v_half(z) > 0 else ring[FRONT])
for i in range(len(ZS) - 1):
    for j in range(NSEG):
        a, b, c, d = rings[i][j], rings[i][j + 1], rings[i + 1][j + 1], rings[i + 1][j]
        if j == FRONT:
            a, d = right[i], right[i + 1]
        face((a, b, c, d), (uv_body(j, ZS[i]), uv_body(j + 1, ZS[i]), uv_body(j + 1, ZS[i + 1]), uv_body(j, ZS[i + 1])))

# stand collar round the back and sides, folding into the lapels at the V
z0 = ZS[0]; bot = []; top = []; botR = topR = None
for j in range(NSEG + 1):
    phi0 = 2 * math.pi * j / NSEG - math.pi
    for side in ((-1,) if j != FRONT else (-1, 1)):
        phi = v_warp(phi0, z0, side)
        notch = max(0.0, 1 - (abs(phi) - V_TOP) / 0.5)
        h = 0.040 * (1 - 0.8 * notch) + 0.004
        r, cy = rad_at(z0, phi, 0.002); k = 0.008 * (1 - 0.5 * notch)
        vb = bm.verts.new((r * math.sin(phi), cy + r * math.cos(phi), z0))
        vt = bm.verts.new(((r + k) * math.sin(phi), cy + (r + k) * math.cos(phi), z0 + h))
        if side == 1: botR, topR = vb, vt
        else: bot.append(vb); top.append(vt)
for j in range(NSEG):
    b0, t0 = (botR, topR) if j == FRONT else (bot[j], top[j])
    face((b0, bot[j + 1], top[j + 1], t0), ((0.5 * j / NSEG, 0.04), (0.5 * (j + 1) / NSEG, 0.04), (0.5 * (j + 1) / NSEG, 0.14), (0.5 * j / NSEG, 0.14)))


def grid(fn, rect, nu, nv, off, mirror):
    u0, v0, u1, v1 = rect; s = -1 if mirror else 1; g = []
    for b in range(nv + 1):
        g.append([bm.verts.new(surf(fn(a / nu, b / nv)[0], s * fn(a / nu, b / nv)[1], off)) for a in range(nu + 1)])
    for b in range(nv):
        for a in range(nu):
            face((g[b][a], g[b][a + 1], g[b + 1][a + 1], g[b + 1][a]),
                 ((u0 + (u1 - u0) * a / nu, v0 + (v1 - v0) * b / nv), (u0 + (u1 - u0) * (a + 1) / nu, v0 + (v1 - v0) * b / nv),
                  (u0 + (u1 - u0) * (a + 1) / nu, v0 + (v1 - v0) * (b + 1) / nv), (u0 + (u1 - u0) * a / nu, v0 + (v1 - v0) * (b + 1) / nv)))


def lapel(a, b):
    # b=0 at the collar, b=1 at the top button; the inner edge follows the V, the outer edge tapers to a point
    z = (ZS[0] - 0.01) - (ZS[0] - 0.01 - TOP_BUTTON) * b
    inner = v_half(z)
    return z, inner + (0.26 * (1 - b) + 0.04) * a


for m in (False, True): grid(lapel, (0.54, 0.06, 0.96, 0.24), 6, 8, 0.005, m)
grid(lambda a, b: (1.30 + 0.10 * b, 0.62 + 0.40 * a), (0.78, 0.56, 0.98, 0.72), 4, 3, 0.005, True)   # breast pocket (wearer's left)
for m in (False, True): grid(lambda a, b: (0.95 + 0.10 * b, 0.62 + 0.55 * a), (0.78, 0.78, 0.98, 0.98), 5, 3, 0.005, m)

BU = (0.84, 0.35); BR = 0.045
for z in BUTTONS:
    c = surf(z, 0.0, 0.004); rr = 0.012
    rb = [bm.verts.new(c + Vector((rr * math.cos(2 * math.pi * k / 10), 0.002, rr * math.sin(2 * math.pi * k / 10)))) for k in range(10)]
    ctr = bm.verts.new(c + Vector((0, 0.004, 0)))
    for k in range(10):
        k2 = (k + 1) % 10
        face((ctr, rb[k2], rb[k]), (BU, (BU[0] + BR * math.cos(2 * math.pi * k2 / 10), BU[1] + BR * math.sin(2 * math.pi * k2 / 10)),
                                     (BU[0] + BR * math.cos(2 * math.pi * k / 10), BU[1] + BR * math.sin(2 * math.pi * k / 10))))

for f in bm.faces:                                   # every face points away from the body axis
    c = f.calc_center_median(); cyc = rad_at(c.z, 0.0)[1]
    if f.normal.dot(Vector((c.x, c.y - cyc, 0))) < 0: f.normal_flip()
bm.normal_update()
me = bpy.data.meshes.new("LabCoat"); bm.to_mesh(me); bm.free()
for p in me.polygons: p.use_smooth = True
ob = bpy.data.objects.new("LabCoat", me); bpy.data.collections["LabCoat"].objects.link(ob)
ob.data.materials.append(bpy.data.materials["LabCoat_mat"])
bpy.context.scene["labcoat_zmin"] = ZMIN; bpy.context.scene["labcoat_zmax"] = ZMAX

# ---------------- export (Blender x,y,z -> Unity x,z,y; winding reversed) ----------------
dg = bpy.context.evaluated_depsgraph_get(); e = ob.evaluated_get(dg); m2 = e.to_mesh(); m2.calc_loop_triangles()
uvd = m2.uv_layers["UVMap"].data; cn = m2.corner_normals; mw = ob.matrix_world
pos = []; nor = []; uvs = []; tris = []; n = 0
for lt in m2.loop_triangles:
    ids = []
    for li in lt.loops:
        p = mw @ m2.vertices[m2.loops[li].vertex_index].co; nv = cn[li].vector
        pos += [p.x, p.z, p.y]; nor += [nv.x, nv.z, nv.y]; uvs += list(uvd[li].uv); ids.append(n); n += 1
    tris += [ids[0], ids[2], ids[1]]
e.to_mesh_clear()
txt = json.dumps({"positions": [round(v, 5) for v in pos], "normals": [round(v, 4) for v in nor],
                  "uvs": [round(v, 5) for v in uvs], "triangles": tris}, separators=(',', ':'))
for path in (r'C:\Modding\LabCoat\LabCoat.mesh.json', os.path.join(ART, 'mesh.json')):
    open(path, 'w').write(txt)
bpy.ops.wm.save_as_mainfile(filepath=r'C:\Modding\LabCoat\LabCoat.blend')
print("coat built", n, "verts")
