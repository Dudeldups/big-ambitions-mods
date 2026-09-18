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
    # Optional authored factory-black masks. These are intentionally exported
    # from the same Blender source as the lights so mixed body meshes can be
    # marked precisely without guessing from runtime bounds.
    "FactoryBlack_SideSteps": ["FactoryBlack_SideSteps", "Black_SideSteps"],
    "FactoryBlack_Mudguards": ["FactoryBlack_Mudguards", "Black_Mudguards"],
}
REQUIRED_GROUPS = [
    "BHeadlights",
    "BDRL_Indicator_FL",
    "BDRL_Indicator_FR",
    "SideIndicatorFL",
    "SideIndicatorFR",
    "1RearDrivingLights",
    "1BrakeLights",
    "ThirdBrakeLight",
    "ReverseLights",
    "1IndicatorRL",
    "1IndicatorRR",
    "FactoryBlack_SideSteps",
    "FactoryBlack_Mudguards",
]
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
    return sum(
        1
        for vertex in source.data.vertices
        if vertex_weight(vertex, group.index) > 0.001
    )


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
source_objects = [
    obj for obj in list(bpy.context.scene.objects)
    if obj.type == "MESH"
]
for source in source_objects:
    entries = []
    for group in source.vertex_groups:
        count = group_member_count(source, group)
        if count:
            entries.append(f"{group.name}({count})")
    if entries:
        print(f"  {source.name}: " + ", ".join(entries))

created = []
resolved = set()

# Merge every source object carrying the same logical light group into ONE
# exported mesh. The previous exporter created e.g. 1BrakeLights and
# 1BrakeLights_2; the runtime controller only bound the first renderer, so newly
# added inner rear-lamp selections were exported but never illuminated.
for expected in GROUPS:
    merged_vertices = []
    merged_faces = []
    contributing_sources = 0
    contributing_faces = 0
    reference_matrix = None
    reference_inverse = None

    for source in source_objects:
        group, actual_name = find_group(source, expected)
        if group is None:
            continue

        mesh = source.data
        assigned, polys, mode = collect_polygons(mesh, group.index)
        print(
            f"[Amarok lights] object='{source.name}' expected='{expected}' "
            f"actual='{actual_name}' assignedVertices={len(assigned)} "
            f"faces={len(polys)} selection={mode}"
        )
        if not polys:
            continue

        # Preserve the exact transform basis used by the old, visible overlay
        # exporter. The first contributing source becomes the logical overlay's
        # transform. Geometry from any additional source object is converted into
        # that local space before merging. Baking matrix_world into identity space
        # caused Unity to apply the AmarokVisual/root transform a second time and
        # moved every authored light away from the vehicle.
        if reference_matrix is None:
            reference_matrix = source.matrix_world.copy()
            reference_inverse = reference_matrix.inverted_safe()

        contributing_sources += 1
        contributing_faces += len(polys)

        used = sorted({index for poly in polys for index in poly.vertices})
        base = len(merged_vertices)
        remap = {old: base + new for new, old in enumerate(used)}
        source_to_reference = reference_inverse @ source.matrix_world

        merged_vertices.extend([
            source_to_reference @ mesh.vertices[index].co
            for index in used
        ])
        merged_faces.extend([
            [remap[index] for index in poly.vertices]
            for poly in polys
        ])

    if not merged_faces or reference_matrix is None:
        continue

    out_mesh = bpy.data.meshes.new(expected + "_Mesh")
    out_mesh.from_pydata(merged_vertices, [], merged_faces)
    out_mesh.update()

    out_object = bpy.data.objects.new(expected, out_mesh)
    out_object.matrix_world = reference_matrix
    bpy.context.collection.objects.link(out_object)
    created.append(out_object)
    resolved.add(expected)

    print(
        f"[Amarok lights] merged expected='{expected}' "
        f"sources={contributing_sources} faces={contributing_faces} "
        f"vertices={len(merged_vertices)}"
    )

missing = [name for name in REQUIRED_GROUPS if name not in resolved]
if missing:
    raise RuntimeError(
        "Could not resolve required Amarok light/trim groups from the supplied Blender file: "
        + ", ".join(missing)
        + ". See the non-empty vertex-group list above for the actual saved names."
    )

# Background Blender does not always provide the UI context required by
# bpy.ops.object.select_all(). Remove the source scene objects instead and export
# the remaining generated overlay objects without relying on selection operators.
for obj in list(bpy.context.scene.objects):
    if obj in created:
        continue
    if obj.name in bpy.data.objects:
        bpy.data.objects.remove(obj, do_unlink=True)

bpy.ops.export_scene.gltf(
    filepath=output,
    export_format="GLB",
    use_selection=False,
    export_apply=True,
    export_materials="NONE",
)

print("Exported merged Amarok light/trim groups:", ", ".join(obj.name for obj in created))
