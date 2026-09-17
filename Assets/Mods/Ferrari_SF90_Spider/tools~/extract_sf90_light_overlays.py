"""Export Ferrari SF90 Spider functional light overlays from Blender vertex groups.

This V14 extractor matches the groups used in FerrariSF90LightOverlays.blend:

    Headlamps
    DRL_FrontIndicator_FL
    DRL_FrontIndicator_FR
    BrakeLights
    ThirdBrakeLight
    RearIndicator_RL
    RearIndicator_RR
    ReverseLights
    MirrorIndicatorLeft
    MirrorIndicatorRight

The SF90 reuses physical surfaces in two places:

* DRL_FrontIndicator_FL/FR are each exported twice: once as white DRL and once
  as amber front indicator. Runtime logic disables the white DRL while the
  corresponding indicator is selected.
* BrakeLights is exported twice: once as the dim TailLights overlay and once as
  the brighter BrakeLights overlay. The runtime never enables both copies at the
  same time, so there is no z-fighting.

GUI workflow:
1. Open FerrariSF90LightOverlays.blend.
2. Scripting -> Open this file -> Run Script.
3. The exporter writes FerrariSF90LightOverlays.glb beside the .blend file.

Command-line workflow:
    blender --background FerrariSF90LightOverlays.blend \
      --python extract_sf90_light_overlays.py -- \
      --output /path/to/FerrariSF90LightOverlays.glb
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import bpy

PREFIX = "VehicleLightRef_"

# One labeled Blender group may intentionally generate more than one runtime overlay.
GROUP_TO_OVERLAYS = {
    "Headlamps": ("Headlamps",),
    "DRL_FrontIndicator_FL": ("DRL_FL", "FrontIndicator_FL"),
    "DRL_FrontIndicator_FR": ("DRL_FR", "FrontIndicator_FR"),
    "BrakeLights": ("TailLights", "BrakeLights"),
    "ThirdBrakeLight": ("ThirdBrakeLight",),
    "RearIndicator_RL": ("RearIndicator_RL",),
    "RearIndicator_RR": ("RearIndicator_RR",),
    "ReverseLights": ("ReverseLights",),
    "MirrorIndicatorLeft": ("MirrorIndicatorLeft",),
    "MirrorIndicatorRight": ("MirrorIndicatorRight",),
}

REQUIRED_GROUPS = tuple(GROUP_TO_OVERLAYS.keys())
EXPECTED_OVERLAY_COUNT = sum(len(v) for v in GROUP_TO_OVERLAYS.values())


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--output")
    return parser.parse_args(argv)


def vertices_for_group(obj, group):
    return {
        vertex.index
        for vertex in obj.data.vertices
        for membership in vertex.groups
        if membership.group == group.index and membership.weight > 0.5
    }


def build_overlay_mesh(source_obj, group_name, overlay_name):
    group = source_obj.vertex_groups.get(group_name)
    if group is None:
        raise RuntimeError(f"Group {group_name!r} disappeared from {source_obj.name!r}")

    selected = vertices_for_group(source_obj, group)
    if not selected:
        raise RuntimeError(f"Vertex group {group_name!r} on {source_obj.name!r} is empty")

    polygons = [
        poly
        for poly in source_obj.data.polygons
        if all(vertex_index in selected for vertex_index in poly.vertices)
    ]
    if not polygons:
        raise RuntimeError(
            f"Vertex group {group_name!r} on {source_obj.name!r} contains no complete faces. "
            "Select complete lamp faces and press Assign in Blender."
        )

    used = sorted({vertex_index for poly in polygons for vertex_index in poly.vertices})
    remap = {source_index: target_index for target_index, source_index in enumerate(used)}
    vertices = [source_obj.data.vertices[index].co.copy() for index in used]
    faces = [tuple(remap[index] for index in poly.vertices) for poly in polygons]

    mesh = bpy.data.meshes.new(PREFIX + overlay_name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    for target_poly, source_poly in zip(mesh.polygons, polygons):
        target_poly.use_smooth = source_poly.use_smooth

    obj = bpy.data.objects.new(PREFIX + overlay_name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    # Preserve the exact transform of the source mesh; mirror indicators may live
    # on a different object from the main Light_Geo mesh.
    obj.matrix_world = source_obj.matrix_world.copy()
    return obj, {
        "sourceObject": source_obj.name,
        "sourceGroup": group_name,
        "overlay": PREFIX + overlay_name,
        "selectedVertexCount": len(selected),
        "usedVertexCount": len(used),
        "faceCount": len(faces),
    }


def main():
    options = parse_args()
    blend_path = Path(bpy.data.filepath).resolve() if bpy.data.filepath else None
    if not blend_path:
        raise RuntimeError("Save the .blend file before running the SF90 light exporter.")

    output = (
        Path(options.output).expanduser().resolve()
        if options.output
        else blend_path.with_name("FerrariSF90LightOverlays.glb")
    )
    output.parent.mkdir(parents=True, exist_ok=True)

    group_sources = {}
    duplicates = {}
    for obj in bpy.data.objects:
        if obj.type != "MESH":
            continue
        for group in obj.vertex_groups:
            if group.name not in GROUP_TO_OVERLAYS:
                continue
            if group.name in group_sources:
                duplicates.setdefault(group.name, [group_sources[group.name].name]).append(obj.name)
            else:
                group_sources[group.name] = obj

    if duplicates:
        details = "; ".join(f"{name}: {objects}" for name, objects in duplicates.items())
        raise RuntimeError("A required SF90 light group exists on multiple mesh objects: " + details)

    missing = [name for name in REQUIRED_GROUPS if name not in group_sources]
    if missing:
        raise RuntimeError("Missing required SF90 light vertex groups: " + ", ".join(missing))

    generated = []
    records = []
    for group_name, overlay_names in GROUP_TO_OVERLAYS.items():
        source_obj = group_sources[group_name]
        for overlay_name in overlay_names:
            obj, record = build_overlay_mesh(source_obj, group_name, overlay_name)
            generated.append(obj)
            records.append(record)

    if len(generated) != EXPECTED_OVERLAY_COUNT:
        raise RuntimeError(
            f"Internal exporter error: generated {len(generated)} overlays, "
            f"expected {EXPECTED_OVERLAY_COUNT}."
        )

    bpy.ops.object.select_all(action="DESELECT")
    for obj in generated:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = generated[0]

    bpy.ops.export_scene.gltf(
        filepath=str(output),
        export_format="GLB",
        use_selection=True,
        export_materials="NONE",
        export_yup=True,
    )

    manifest = output.with_suffix(".json")
    manifest.write_text(
        json.dumps(
            {
                "sourceBlend": blend_path.name,
                "overlayCount": len(generated),
                "overlays": records,
            },
            indent=2,
        ),
        encoding="utf-8",
    )

    # Remove generated temporary objects from the live .blend after export so the
    # user's authored scene remains clean if they save again.
    for obj in generated:
        mesh = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        if mesh.users == 0:
            bpy.data.meshes.remove(mesh)

    print(f"EXPORTED {len(records)} SF90 light overlays to {output}")
    print(f"Manifest: {manifest}")


if __name__ == "__main__":
    main()
