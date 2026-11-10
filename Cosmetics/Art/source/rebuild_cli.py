# Headless rebuild: blender -b LabCoat.blend -P rebuild_cli.py  (coat mesh + chest texture, saves the .blend)
import bpy
exec(open(r"C:\Modding\LabCoat\build_coat.py").read())
exec(open(r"C:\Modding\LabCoat\paint_chest.py").read())
ob = bpy.data.objects["LabCoat"]
zs = [(ob.matrix_world @ v.co).z for v in ob.data.vertices]
print("REBUILD verts", len(ob.data.vertices), "faces", len(ob.data.polygons), "z", round(min(zs), 3), round(max(zs), 3))
bpy.ops.wm.save_as_mainfile(filepath=r'C:\Modding\LabCoat\LabCoat.blend')
