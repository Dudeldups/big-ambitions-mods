import bpy
import math
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


def vertex_weight(vertex, group_index):
    for item in vertex.groups:
        if item.group == group_index:
            return item.weight
    return 0.0


def collect_polygons(mesh, group_index):
    assigned = {
        vertex.index
        for vertex in mesh.vertices
        if vertex_weight(vertex, group_index) > 0.001
    }
    if not assigned:
        return assigned, [], "none"

    def matches(poly, mode):
        hits = sum(1 for index in poly.vertices if index in assigned)
        if mode == "all":
            return hits == len(poly.vertices)
        if mode == "half":
            return hits >= math.ceil(len(poly.vertices) * 0.5)
        return hits >= 1

    for mode in ("all", "half", "any"):
        polys = [poly for poly in mesh.polygons if poly.vertices and matches(poly, mode)]
        if polys:
            return assigned, polys, mode
    return assigned, [], "none"


created = []
created_names = set()
for source in list(bpy.context.scene.objects):
    if source.type != "MESH":
        continue

    for name in GROUPS:
        group = source.vertex_groups.get(name)
        if group is None:
            continue

        mesh = source.data
        assigned, polys, mode = collect_polygons(mesh, group.index)
        print(
            f"[Amarok lights] object='{source.name}' group='{name}' "
            f"assignedVertices={len(assigned)} faces={len(polys)} selection={mode}"
        )
        if not polys:
            continue

        used = sorted({index for poly in polys for index in poly.vertices})
        remap = {old: new for new, old in enumerate(used)}
        verts = [mesh.vertices[index].co.copy() for index in used]
        faces = [[remap[index] for index in poly.vertices] for poly in polys]

        out_mesh = bpy.data.meshes.new(name + "_Mesh")
        out_mesh.from_pydata(verts, [], faces)
        out_mesh.update()

        object_name = name
        if object_name in created_names:
            suffix = 2
            while f"{name}_{suffix}" in created_names:
                suffix += 1
            object_name = f"{name}_{suffix}"

        out_object = bpy.data.objects.new(object_name, out_mesh)
        out_object.matrix_world = source.matrix_world.copy()
        bpy.context.collection.objects.link(out_object)
        created.append(out_object)
        created_names.add(object_name)

missing = [
    name for name in GROUPS
    if not any(obj.name == name or obj.name.startswith(name + "_") for obj in created)
]
if missing:
    raise RuntimeError(
        "Missing or empty Amarok light groups after partial-face fallback: "
        + ", ".join(missing)
    )

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
