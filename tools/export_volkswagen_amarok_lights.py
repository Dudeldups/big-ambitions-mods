import bpy
import colorsys
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
    "VehiclePaint_Blue",
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



def is_body_paint_candidate(material):
    if material is None:
        return False
    name = material.name.lower()
    return "phong5" in name or "dorr_r" in name


def find_base_color_image(material):
    if material is None or not material.use_nodes or material.node_tree is None:
        return None

    principled = next(
        (node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED"),
        None,
    )
    if principled is None:
        return None

    base = principled.inputs.get("Base Color")
    if base is None or not base.is_linked:
        return None

    pending = [link.from_node for link in base.links]
    seen = set()
    while pending:
        node = pending.pop()
        if node in seen:
            continue
        seen.add(node)
        if node.type == "TEX_IMAGE" and getattr(node, "image", None) is not None:
            return node.image
        for socket in getattr(node, "inputs", []):
            if socket.is_linked:
                pending.extend(link.from_node for link in socket.links)
    return None


def material_base_color(material):
    if material is None:
        return (0.0, 0.0, 0.0)
    if material.use_nodes and material.node_tree is not None:
        principled = next(
            (node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED"),
            None,
        )
        if principled is not None:
            base = principled.inputs.get("Base Color")
            if base is not None:
                value = base.default_value
                return (float(value[0]), float(value[1]), float(value[2]))
    value = material.diffuse_color
    return (float(value[0]), float(value[1]), float(value[2]))


def sample_image(image, uv):
    if image is None or image.size[0] <= 0 or image.size[1] <= 0:
        return None
    try:
        pixels = image.pixels
        width, height = int(image.size[0]), int(image.size[1])
        u = float(uv.x) % 1.0
        v = float(uv.y) % 1.0
        x = min(width - 1, max(0, int(round(u * (width - 1)))))
        y = min(height - 1, max(0, int(round(v * (height - 1)))))
        index = (y * width + x) * 4
        return (
            float(pixels[index]),
            float(pixels[index + 1]),
            float(pixels[index + 2]),
        )
    except Exception as exc:
        print(
            f"[Amarok paint] could not sample image='{getattr(image, 'name', '<unknown>')}' "
            f"reason={exc}"
        )
        return None


def is_source_blue(rgb):
    r, g, b = rgb
    maximum = max(r, g, b)
    minimum = min(r, g, b)
    if maximum < 0.055:
        return False

    hue, saturation, value = colorsys.rgb_to_hsv(
        max(0.0, min(1.0, r)),
        max(0.0, min(1.0, g)),
        max(0.0, min(1.0, b)),
    )

    # The supplied Amarok's authored paint is a saturated medium/deep blue.
    # Keep this deliberately hue-based rather than material-name-only so black
    # grille inserts and black plastic sharing phong5/dorr_R stay untouched.
    return (
        0.52 <= hue <= 0.72
        and saturation >= 0.24
        and value >= 0.08
        and b >= r * 1.10
        and b >= g * 1.035
    )


def collect_original_blue_polygons(source):
    mesh = source.data
    uv_layer = mesh.uv_layers.active
    if uv_layer is None:
        uv_data = None
    else:
        uv_data = uv_layer.data

    accepted = []
    candidate_faces = 0
    image_cache = {}

    for poly in mesh.polygons:
        if not poly.vertices:
            continue
        if poly.material_index < 0 or poly.material_index >= len(source.material_slots):
            continue

        material = source.material_slots[poly.material_index].material
        if not is_body_paint_candidate(material):
            continue
        candidate_faces += 1

        image = image_cache.get(material)
        if material not in image_cache:
            image = find_base_color_image(material)
            image_cache[material] = image

        samples = []
        if image is not None and uv_data is not None and poly.loop_indices:
            uv_points = [uv_data[index].uv.copy() for index in poly.loop_indices]
            for uv in uv_points:
                sampled = sample_image(image, uv)
                if sampled is not None:
                    samples.append(sampled)
            centroid = sum(uv_points[1:], uv_points[0].copy()) / len(uv_points)
            sampled = sample_image(image, centroid)
            if sampled is not None:
                samples.append(sampled)

        if not samples:
            samples = [material_base_color(material)]

        blue_votes = sum(1 for sample in samples if is_source_blue(sample))
        required_votes = max(1, math.ceil(len(samples) * 0.5))
        if blue_votes >= required_votes:
            accepted.append(poly)

    return candidate_faces, accepted


def build_original_blue_paint_overlay(source_objects):
    expected = "VehiclePaint_Blue"
    merged_vertices = []
    merged_faces = []
    reference_matrix = None
    reference_inverse = None
    contributing_sources = 0
    contributing_faces = 0

    for source in source_objects:
        candidate_faces, polys = collect_original_blue_polygons(source)
        if candidate_faces == 0:
            continue

        print(
            f"[Amarok paint] object='{source.name}' candidateBodyFaces={candidate_faces} "
            f"originalBlueFaces={len(polys)}"
        )
        if not polys:
            continue

        if reference_matrix is None:
            reference_matrix = source.matrix_world.copy()
            reference_inverse = reference_matrix.inverted_safe()

        contributing_sources += 1
        contributing_faces += len(polys)
        source_to_reference = reference_inverse @ source.matrix_world

        mesh = source.data
        coords = [vertex.co for vertex in mesh.vertices]
        min_x = min(v.x for v in coords)
        max_x = max(v.x for v in coords)
        min_y = min(v.y for v in coords)
        max_y = max(v.y for v in coords)
        min_z = min(v.z for v in coords)
        max_z = max(v.z for v in coords)
        local_span = max(max_x - min_x, max_y - min_y, max_z - min_z)
        normal_offset = max(local_span * 0.00028, 0.00012)

        used = sorted({index for poly in polys for index in poly.vertices})
        base = len(merged_vertices)
        remap = {old: base + new for new, old in enumerate(used)}

        for index in used:
            vertex = mesh.vertices[index]
            displaced = vertex.co + vertex.normal.normalized() * normal_offset
            merged_vertices.append(source_to_reference @ displaced)

        merged_faces.extend([
            [remap[index] for index in poly.vertices]
            for poly in polys
        ])

    if not merged_faces or reference_matrix is None:
        raise RuntimeError(
            "Could not derive VehiclePaint_Blue from the original Amarok materials/textures. "
            "The exporter found no sufficiently blue phong5/dorr_R faces."
        )

    out_mesh = bpy.data.meshes.new(expected + "_Mesh")
    out_mesh.from_pydata(merged_vertices, [], merged_faces)
    out_mesh.update()

    out_object = bpy.data.objects.new(expected, out_mesh)
    out_object.matrix_world = reference_matrix
    bpy.context.collection.objects.link(out_object)

    print(
        f"[Amarok paint] generated expected='{expected}' "
        f"sources={contributing_sources} faces={contributing_faces} "
        f"vertices={len(merged_vertices)}"
    )
    return out_object


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

paint_object = build_original_blue_paint_overlay(source_objects)
created.append(paint_object)
resolved.add("VehiclePaint_Blue")

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

        source_to_reference = reference_inverse @ source.matrix_world

        if expected.startswith("FactoryBlack_"):
            # These trim masks render directly over the still-present VehicleColor
            # source geometry. The previous per-polygon displacement fixed z-fighting
            # but duplicated triangle corners, opening tiny cracks between adjacent
            # faces where red paint could still show through. Keep one shared output
            # vertex per source vertex and move it along the Blender vertex normal.
            # Adjacent faces then remain welded while inner and outer surfaces are
            # both offset away from their original painted geometry.
            coords = [vertex.co for vertex in mesh.vertices]
            min_x = min(v.x for v in coords)
            max_x = max(v.x for v in coords)
            min_y = min(v.y for v in coords)
            max_y = max(v.y for v in coords)
            min_z = min(v.z for v in coords)
            max_z = max(v.z for v in coords)
            local_span = max(max_x - min_x, max_y - min_y, max_z - min_z)
            normal_offset = max(local_span * 0.00065, 0.00025)

            used = sorted({index for poly in polys for index in poly.vertices})
            base = len(merged_vertices)
            remap = {old: base + new for new, old in enumerate(used)}

            for index in used:
                vertex = mesh.vertices[index]
                normal = vertex.normal.normalized()
                displaced = vertex.co + normal * normal_offset
                merged_vertices.append(source_to_reference @ displaced)

            merged_faces.extend([
                [remap[index] for index in poly.vertices]
                for poly in polys
            ])

            print(
                f"[Amarok lights] factory-black welded vertex-normal offset "
                f"expected='{expected}' source='{source.name}' "
                f"offset={normal_offset:.6f}"
            )
        else:
            used = sorted({index for poly in polys for index in poly.vertices})
            base = len(merged_vertices)
            remap = {old: base + new for new, old in enumerate(used)}

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

print("Exported Amarok light/trim/paint groups:", ", ".join(obj.name for obj in created))
