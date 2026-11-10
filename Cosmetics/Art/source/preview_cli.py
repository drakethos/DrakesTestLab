# Headless preview renders of body + coat: blender -b LabCoat.blend -P preview_cli.py
import bpy, math
from mathutils import Vector
sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE'
if not bpy.data.objects.get("PrevSun"):
    sun = bpy.data.objects.new("PrevSun", bpy.data.lights.new("PrevSun", 'SUN')); sc.collection.objects.link(sun)
    sun.data.energy = 3.0; sun.rotation_euler = (math.radians(50), 0, math.radians(30))
w = sc.world or bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.25, 0.25, 0.27, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 1.0
sc.render.resolution_x = 700; sc.render.resolution_y = 900
sc.render.film_transparent = False
cam = bpy.data.objects.get("PrevCam")
if not cam:
    cam = bpy.data.objects.new("PrevCam", bpy.data.cameras.new("PrevCam")); sc.collection.objects.link(cam)
sc.camera = cam
cam.data.lens = 50
target = Vector((0, 0, 1.25))
for name, yaw in (("front", 0), ("three", 35), ("side", 90), ("back", 180)):
    a = math.radians(yaw); d = 2.6
    cam.location = target + Vector((d * math.sin(a), d * math.cos(a), 0.05))
    direction = target - cam.location
    cam.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = rf"C:\Modding\LabCoat\preview_{name}.png"
    bpy.ops.render.render(write_still=True)
print("PREVIEW done")
