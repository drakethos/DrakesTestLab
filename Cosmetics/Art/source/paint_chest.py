# Paints the lab coat's body texture (_ChestTex) straight from the player body's own UV layout.
# Valheim draws _ChestTex over the body in the body mesh's UV space, so we rasterize the real body triangles:
# torso + arms down to the wrist get linen (with a cuff), hands / neck / head / legs stay transparent (skin shows).
#   exec(open(r"C:\Modding\LabCoat\paint_chest.py").read())
import bpy, os, numpy as np
from mathutils import Vector

ART = r'C:\Modding\Vahleim\Mod\DrakesWorkshop\DrakesTestLab\Cosmetics\Art'
TEX = r'C:\Modding\LabCoat\textures'
SIZE = 512
NECK_Z = 1.64      # above: neck/head (the collar covers the join)
WRIST_X = 0.545    # |x| beyond: hands (A-pose)
CUFF_X = 0.50      # cuff band from here to the wrist
HIP_Z = 0.98       # below: legs
ARM_X = 0.24

body = bpy.data.objects["Visual.body_0"]; me = body.data; me.calc_loop_triangles()
uvd = me.uv_layers[0].data; mw = body.matrix_world
P = np.array([tuple(mw @ v.co) for v in me.vertices])

H = W = SIZE
col = np.zeros((H, W, 4), np.float32)          # rgba
pos = np.full((H, W, 3), np.nan, np.float32)   # body position per covered texel


def wanted(p):
    ax = abs(p[0])
    if p[2] > NECK_Z or ax > WRIST_X: return False
    if p[2] < HIP_Z and ax < ARM_X: return False      # legs
    return True


for lt in me.loop_triangles:
    vs = [P[v] for v in lt.vertices]
    uvs = np.array([uvd[li].uv[:] for li in lt.loops], np.float64) * [W, H]
    x0, y0 = np.floor(uvs.min(0)).astype(int) - 1; x1, y1 = np.ceil(uvs.max(0)).astype(int) + 1
    x0, y0 = max(x0, 0), max(y0, 0); x1, y1 = min(x1, W - 1), min(y1, H - 1)
    if x1 < x0 or y1 < y0: continue
    yy, xx = np.mgrid[y0:y1 + 1, x0:x1 + 1]; px = xx + 0.5; py = yy + 0.5
    (ax_, ay_), (bx_, by_), (cx_, cy_) = uvs
    den = (by_ - cy_) * (ax_ - cx_) + (cx_ - bx_) * (ay_ - cy_)
    if abs(den) < 1e-12: continue
    l1 = ((by_ - cy_) * (px - cx_) + (cx_ - bx_) * (py - cy_)) / den
    l2 = ((cy_ - ay_) * (px - cx_) + (ax_ - cx_) * (py - cy_)) / den
    l3 = 1 - l1 - l2
    inside = (l1 >= -0.02) & (l2 >= -0.02) & (l3 >= -0.02)
    if not inside.any(): continue
    p3 = (l1[..., None] * vs[0] + l2[..., None] * vs[1] + l3[..., None] * vs[2])
    ys, xs = yy[inside], xx[inside]
    pos[ys, xs] = p3[inside]

covered = ~np.isnan(pos[..., 0])
keep = np.zeros((H, W), bool)
ax = np.abs(np.nan_to_num(pos[..., 0])); z = np.nan_to_num(pos[..., 2])
keep[covered] = True
keep &= ~(z > NECK_Z)
keep &= ~(ax > WRIST_X)
keep &= ~((z < HIP_Z) & (ax < ARM_X))

# grow the mask 2 texels into the gutters so mip-mapping doesn't bleed skin into the seams
grown = keep.copy()
for _ in range(2):
    grown = grown | np.roll(grown, 1, 0) | np.roll(grown, -1, 0) | np.roll(grown, 1, 1) | np.roll(grown, -1, 1)
grown &= ~(covered & ~keep)          # never spill onto hands/neck/legs texels


def smooth_noise(h, w, scale, seed):
    r = np.random.default_rng(seed); s = r.normal(0, 1, (h // scale + 2, w // scale + 2)).astype(np.float32)
    ys = np.linspace(0, s.shape[0] - 1.001, h); xs = np.linspace(0, s.shape[1] - 1.001, w)
    y0 = ys.astype(int); x0 = xs.astype(int); fy = (ys - y0)[:, None]; fx = (xs - x0)[None, :]
    a = s[y0][:, x0]; b = s[y0][:, x0 + 1]; c = s[y0 + 1][:, x0]; d = s[y0 + 1][:, x0 + 1]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def linen_height(h, w, period, seed):
    r = np.random.default_rng(seed); y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    warp = 0.5 + 0.5 * np.sin(2 * np.pi * x / period); weft = 0.5 + 0.5 * np.sin(2 * np.pi * y / period)
    over = ((np.floor(x / period) + np.floor(y / period)) % 2).astype(np.float32)
    hg = over * warp + (1 - over) * weft
    rows = r.random(h) < 0.10; cols = r.random(w) < 0.10
    hg += 0.35 * rows[:, None] * np.clip(smooth_noise(h, w, 16, seed + 1), 0, None)
    hg += 0.35 * cols[None, :] * np.clip(smooth_noise(h, w, 16, seed + 2), 0, None)
    return hg


BASE = np.array(bpy.context.scene.get("labcoat_body_base", (0.80, 0.80, 0.78)), np.float32)
hg = linen_height(H, W, 3, 11)
fold = 1 + 0.03 * smooth_noise(H, W, 24, 14) + 0.012 * smooth_noise(H, W, 8, 15)
shade = 0.94 + 0.06 * (hg / hg.max())
col[..., :3] = BASE[None, None, :] * (fold * shade)[..., None]
# cuff: slightly darker band with two stitch lines just above the wrist
cuff = keep & (ax > CUFF_X)
col[cuff, :3] *= 0.95
for lo, hi in ((CUFF_X, CUFF_X + 0.006), (WRIST_X - 0.008, WRIST_X - 0.002)):
    col[keep & (ax > lo) & (ax < hi), :3] = (0.60, 0.61, 0.62)
# shoulder seam (arm meets torso)
col[keep & (ax > ARM_X - 0.004) & (ax < ARM_X + 0.004) & (z > 1.35), :3] *= 0.86
# blue undershirt: the torso under the coat (it shows in the V and at the neck); a ribbed crew collar on top
SHIRT_TOP = 1.615
SHIRT_BLUE = np.array(bpy.context.scene.get("labcoat_shirt_blue", (0.16, 0.30, 0.55)), np.float32)
torso = covered & (ax < ARM_X) & (z <= NECK_Z)
knit = 0.93 + 0.07 * (0.5 + 0.5 * np.sin(2 * np.pi * np.arange(W) / 2.5))[None, :]
col[torso, :3] = (SHIRT_BLUE[None, None, :] * (fold * knit)[..., None])[torso]
rib = torso & (z > SHIRT_TOP - 0.018)
col[rib, :3] *= 0.82
neck_skin = torso & (z > SHIRT_TOP) & (ax < 0.10)
keep &= ~neck_skin
# shoulder tops beside the neck (above the coat's top ring) are coat linen, not shirt
shoulder_top = torso & (z > SHIRT_TOP - 0.03) & (ax >= 0.10)
col[shoulder_top, :3] = (BASE[None, None, :] * (fold * shade)[..., None])[shoulder_top]
keep |= covered & (ax < ARM_X) & (z < HIP_Z) & (z >= 0.91)    # shirt runs a little below the coat hem
grown = keep.copy()
for _ in range(2):
    grown = grown | np.roll(grown, 1, 0) | np.roll(grown, -1, 0) | np.roll(grown, 1, 1) | np.roll(grown, -1, 1)
grown &= ~(covered & ~keep)
# gutter texels take their neighbour's colour (else blue islands get white seams)
filled = keep.copy()
for _ in range(3):
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        src = np.roll(np.roll(filled, dy, 0), dx, 1); need = grown & ~filled & src
        col[need, :3] = np.roll(np.roll(col, dy, 0), dx, 1)[need, :3]; filled |= need
col[..., 3] = grown.astype(np.float32)
col[~grown, :3] = 0


def normal_map(h_, strength):
    gy, gx = np.gradient(h_.astype(np.float32)); n = np.dstack([-gx * strength, -gy * strength, np.ones_like(gx)])
    n /= np.linalg.norm(n, axis=2, keepdims=True); out = np.ones(h_.shape + (4,), np.float32); out[..., :3] = n * 0.5 + 0.5
    return out


def save(arr, base, artname):
    h, w = arr.shape[:2]; nm = "out_" + base
    im = bpy.data.images.get(nm) or bpy.data.images.new(nm, w, h, alpha=True)
    if tuple(im.size) != (w, h): im.scale(w, h)
    im.pixels = np.clip(arr, 0, 1).astype(np.float32).ravel().tolist(); im.file_format = 'PNG'
    for p in (os.path.join(TEX, base + '.png'), os.path.join(ART, artname)):
        im.filepath_raw = p; im.save()
    return im


chest_img = save(col, "LabCoat_ChestTex", "chest.png")
save(normal_map(hg, 0.15), "LabCoat_ChestBump", "chest_n.png")

# preview: the body in Blender with the chest texture over its skin, the way Valheim layers it
mat = bpy.data.materials.get("BodyPreview") or bpy.data.materials.new("BodyPreview"); mat.use_nodes = True
nt = mat.node_tree; nt.nodes.clear()
out = nt.nodes.new("ShaderNodeOutputMaterial"); bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
skin_src = body.data.materials[0]
skin_img = next((n.image for n in skin_src.node_tree.nodes if n.type == 'TEX_IMAGE'), None) if skin_src and skin_src.use_nodes else None
t_skin = nt.nodes.new("ShaderNodeTexImage"); t_skin.image = skin_img
t_chest = nt.nodes.new("ShaderNodeTexImage"); t_chest.image = chest_img
mix = nt.nodes.new("ShaderNodeMix"); mix.data_type = 'RGBA'
nt.links.new(t_chest.outputs["Alpha"], mix.inputs[0])
nt.links.new(t_skin.outputs["Color"], mix.inputs[6]); nt.links.new(t_chest.outputs["Color"], mix.inputs[7])
nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"]); nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
if len(body.data.materials) and body.data.materials[0].name != "BodyPreview":
    bpy.context.scene["labcoat_body_original_mat"] = body.data.materials[0].name
body.data.materials[0] = mat
print("chest painted", int(keep.sum()), "texels")
