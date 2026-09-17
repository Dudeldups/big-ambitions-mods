import bpy
import math
import re
import sys

GROUP_ALIASES = {
    "BHeadlights": ["BHeadlights", "Headlights"],
    "BDRL_Indicator_FL": ["BDRL_Indicator_FL", "DRL_Indicator_FL"],
    "BDRL_Indicator_FR": ["BDRL_Indicator_FR", "DRL_Indicator_FR"],
    "SideIndicatorFL": ["SideIndicatorFL", "Side_Indicator_FL"],
    "SideIndicatorFR": ["SideIndicatorFR", "Side_Indicator_FR"],
    "1RearDrivingLights": ["1RearDrivingLights", "RearDrivingLights"],
    "1BrakeLights": ["1BrakeLights", "BrakeLights"],
    "ThirdBrakeLight": ["ThirdBrakeLight"],
    "ReverseLights": ["ReverseLights"],
    "1IndicatorRL": ["1IndicatorRL", "IndicatorRL", "RearIndicatorLeft"],
    "1IndicatorRR": ["1IndicatorRR", "IndicatorRR", "RearIndicatorRight"],
}
GROUPS = list(GROUP_ALIASES)

if "--" not in sys.argv:
    raise RuntimeError("Pass the output GLB after --")
output = sys.argv[sys.argv.index("--") + 1]


def normalized_name(value):
    return re.sub(r"[^a-z0-9]", "", value.lower())


def vertex_weight(vertex, group_index):
    for item in vertex.groups:
        if item.group == group_index:
            return item.weight
    return 0.0


def group_member_count(source, group):
    return sum(1 for vertex in source.data.vertices if vertex_weight(vertex, group.index) > 0.001)


def find_group(source, expected):
    for alias in GROUP_ALIASES[expected]:
        group = source.vertex_groups.get(alias)
        if group is not None and group_member_count(source, group) > 0:
            return group, alias

    aliases = {normalized_name(alias) for alias in GROUP_ALIASES[expected]}
    for group in source.vertex_groups:
        if normalized_name(group.name) in aliases and group_member_count(source, group) > 0:
            return group, group.name
    return None, None


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


print("[Amarok lights] non-empty Blender vertex groups:")
for source in list(bpy.context.scene.objects):
    if source.type != "MESH":
        continue
    entries = []
    for group in source.vertex_groups:
        count = group_member_count(source, group)
        if count:
            entries.append(f"{group.name}({count})")
    if entries:
        print(f"  {source.name}: " + ", ".join(entries))

created = []
created_names = set()
resolved = set()
for expected in GROUPS:
    for source in list(bpy.context.scene.objects):
        if source.type != "MESH":
            continue

        group, actual_name = find_group(source, expected)
        if group is None:
            continue

        mesh = source.data
        assigned, polys, mode = collect_polygons(mesh, group.index)
        print(
            f"[Amarok lights] object='{source.name}' expected='{expected}' actual='{actual_name}' "
            f"assignedVertices={len(assigned)} faces={len(polys)} selection={mode}"
        )
        if not polys:
            continue

        used = sorted({index for poly in polys for index in poly.vertices})
        remap = {old: new for new, old in enumerate(used)}
        verts = [mesh.vertices[index].co.copy() for index in used]
        faces = [[remap[index] for index in poly.vertices] for poly in polys]

        out_mesh = bpy.data.meshes.new(expected + "_Mesh")
        out_mesh.from_pydata(verts, [], faces)
        out_mesh.update()

        object_name = expected
        if object_name in created_names:
            suffix = 2
            while f"{expected}_{suffix}" in created_names:
                suffix += 1
            object_name = f"{expected}_{suffix}"

        out_object = bpy.data.objects.new(object_name, out_mesh)
        out_object.matrix_world = source.matrix_world.copy()
        bpy.context.collection.objects.link(out_object)
        created.append(out_object)
        created_names.add(object_name)
        resolved.add(expected)

missing = [name for name in GROUPS if name not in resolved]
if missing:
    raise RuntimeError(
        "Could not resolve Amarok light groups from the supplied Blender file: "
        + ", ".join(missing)
        + ". See the non-empty vertex-group list above for the actual saved names."
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
