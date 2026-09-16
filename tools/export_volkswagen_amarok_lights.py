import bpy
import sys

GROUPS = [
    "BHeadlights",
    "BDRL_Indicator_FL",
    "BDRL_Indicator_FR",
    "1RearDrivingLights",
    "1BrakeLights",
    "ThirdBrakeLight",
    "ReverseLights",
    "1IndicatorRL",
    "1IndicatorRR",
]

if "--" not in sys.argv:
    raise RuntimeError("Pass the output GLB after --")
output = sys.argv[sys.argv.index("--") + 1]

def in_group(vertex, group_index):
    return any(item.group == group_index and item.weight > 0.001 for item in vertex.groups)

created = []
for source in list(bpy.context.scene.objects):
    if source.type != "MESH":
        continue
    for name in GROUPS:
        group = source.vertex_groups.get(name)
        if group is None:
            continue
        mesh = source.data
        polys = [
            poly for poly in mesh.polygons
            if poly.vertices and all(in_group(mesh.vertices[index], group.index) for index in poly.vertices)
        ]
        if not polys:
            continue
        used = sorted({index for poly in polys for index in poly.vertices})
        remap = {old: new for new, old in enumerate(used)}
        verts = [mesh.vertices[index].co.copy() for index in used]
        faces = [[remap[index] for index in poly.vertices] for poly in polys]
        out_mesh = bpy.data.meshes.new(name + "_Mesh")
        out_mesh.from_pydata(verts, [], faces)
        out_mesh.update()
        out_object = bpy.data.objects.new(name, out_mesh)
        out_object.matrix_world = source.matrix_world.copy()
        bpy.context.collection.objects.link(out_object)
        created.append(out_object)

missing = [name for name in GROUPS if not any(obj.name == name for obj in created)]
if missing:
    raise RuntimeError("Missing or empty Amarok light groups: " + ", ".join(missing))

bpy.ops.object.select_all(action="DESELECT")
for obj in created:
    obj.select_set(True)
bpy.context.view_layer.objects.active = created[0]
bpy.ops.export_scene.gltf(
    filepath=output,
    export_format="GLB",
    use_selection=True,
    export_apply=True,
    export_materials="NONE",
)
print("Exported Amarok light groups:", ", ".join(obj.name for obj in created))
