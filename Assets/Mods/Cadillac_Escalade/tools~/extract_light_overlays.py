"""Validate labeled lamp groups and export topology-safe overlay meshes.

Run with Blender after opening the labeled working .blend::

    blender --background Cadillac_Escalade_Lights_Work.blend \
        --python extract_light_overlays.py -- \
        --production cadillac_escalade.glb --output CadillacLightOverlays.glb
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import bpy
from mathutils import Vector


PREFIX = "VehicleLightRef_"
IGNORED_GROUPS = {
    (
        "E:common_headlight_metals_headlight_metals_common_001_common_headlight_metals_"
        "headlight_metals_common_001_headlight_metal_001_metal_002_0",
        "Indicator_FL",
    ),
}


def parse_args() -> argparse.Namespace:
    args = sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--production", required=True)
    parser.add_argument("--output", required=True)
    return parser.parse_args(args)


def safe_name(value: str) -> str:
    return "".join(character if character.isalnum() else "_" for character in value)


def group_vertices(obj: bpy.types.Object, group: bpy.types.VertexGroup) -> set[int]:
    return {
        vertex.index
        for vertex in obj.data.vertices
        if any(
            membership.group == group.index and membership.weight > 0.5
            for membership in vertex.groups
        )
    }


def bounds(points: list[Vector]) -> dict[str, list[float]]:
    minimum = Vector((min(point[index] for point in points) for index in range(3)))
    maximum = Vector((max(point[index] for point in points) for index in range(3)))
    return {
        "min": [round(value, 6) for value in minimum],
        "max": [round(value, 6) for value in maximum],
        "center": [round(value, 6) for value in (minimum + maximum) * 0.5],
    }


def clear_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for collection in (bpy.data.meshes, bpy.data.materials, bpy.data.images):
        for item in list(collection):
            collection.remove(item)


def main() -> None:
    options = parse_args()
    production_path = Path(options.production).resolve()
    output_path = Path(options.output).resolve()
    output_path.parent.mkdir(parents=True, exist_ok=True)

    source_records: list[dict[str, object]] = []
    source_meshes: dict[tuple[str, str], dict[str, object]] = {}
    for obj in bpy.data.objects:
        if obj.type != "MESH" or not obj.vertex_groups:
            continue
        for group in obj.vertex_groups:
            if (obj.name, group.name) in IGNORED_GROUPS:
                print(f"SKIP_INCOMPLETE {obj.name} | {group.name}")
                continue
            selected = group_vertices(obj, group)
            if not selected:
                print(f"SKIP_EMPTY {obj.name} | {group.name}")
                continue
            polygons = [
                polygon
                for polygon in obj.data.polygons
                if all(vertex_index in selected for vertex_index in polygon.vertices)
            ]
            if not polygons:
                raise RuntimeError(
                    f"Vertex group '{group.name}' on '{obj.name}' contains no complete faces."
                )
            used = sorted(
                {vertex_index for polygon in polygons for vertex_index in polygon.vertices}
            )
            remap = {source_index: target_index for target_index, source_index in enumerate(used)}
            vertices = [obj.data.vertices[index].co.copy() for index in used]
            faces = [tuple(remap[index] for index in polygon.vertices) for polygon in polygons]
            smooth = [polygon.use_smooth for polygon in polygons]
            world_points = [obj.matrix_world @ point for point in vertices]
            record = {
                "sourceObject": obj.name,
                "sourceVertexCount": len(obj.data.vertices),
                "group": group.name,
                "overlay": f"{PREFIX}{safe_name(group.name)}",
                "selectedVertexCount": len(selected),
                "usedVertexCount": len(used),
                "faceCount": len(faces),
                "localBounds": bounds(vertices),
                "worldBounds": bounds(world_points),
            }
            source_records.append(record)
            source_meshes[(obj.name, group.name)] = {
                "vertices": vertices,
                "faces": faces,
                "smooth": smooth,
            }

    if not source_records:
        raise RuntimeError("No non-empty labeled mesh vertex groups were found.")

    clear_scene()
    bpy.ops.import_scene.gltf(filepath=str(production_path))
    production = {
        obj.name: obj for obj in bpy.data.objects if obj.type == "MESH"
    }
    for record in source_records:
        source_name = str(record["sourceObject"])
        production_object = production.get(source_name)
        if production_object is None:
            raise RuntimeError(
                f"Production GLB has no exact mesh object named '{source_name}'."
            )
        production_count = len(production_object.data.vertices)
        if production_count != record["sourceVertexCount"]:
            raise RuntimeError(
                f"Topology mismatch for '{source_name}': labeled source has "
                f"{record['sourceVertexCount']} vertices, production has {production_count}."
            )
        record["productionVertexCount"] = production_count

    clear_scene()
    generated: list[bpy.types.Object] = []
    duplicate_counts: dict[str, int] = {}
    for record in source_records:
        semantic_name = str(record["overlay"])
        duplicate_index = duplicate_counts.get(semantic_name, 0)
        duplicate_counts[semantic_name] = duplicate_index + 1
        overlay_name = semantic_name if duplicate_index == 0 else f"{semantic_name}_{duplicate_index + 1}"
        record["overlay"] = overlay_name
        source = source_meshes[(str(record["sourceObject"]), str(record["group"]))]
        mesh = bpy.data.meshes.new(overlay_name)
        mesh.from_pydata(source["vertices"], [], source["faces"])
        mesh.update()
        for polygon, smooth in zip(mesh.polygons, source["smooth"]):
            polygon.use_smooth = bool(smooth)
        obj = bpy.data.objects.new(overlay_name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        obj.location = (0.0, 0.0, 0.0)
        obj.rotation_euler = (0.0, 0.0, 0.0)
        obj.scale = (1.0, 1.0, 1.0)
        generated.append(obj)

    bpy.ops.object.select_all(action="DESELECT")
    for obj in generated:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = generated[0]
    bpy.ops.export_scene.gltf(
        filepath=str(output_path),
        export_format="GLB",
        use_selection=True,
        export_materials="NONE",
        export_yup=True,
    )
    manifest_path = output_path.with_suffix(".json")
    manifest_path.write_text(
        json.dumps({"production": production_path.name, "overlays": source_records}, indent=2),
        encoding="utf-8",
    )
    print("===CADILLAC_LIGHT_OVERLAYS===")
    for record in source_records:
        print(json.dumps(record, sort_keys=True))
    print(f"EXPORTED {len(source_records)} overlays to {output_path}")
    print(f"MANIFEST {manifest_path}")


if __name__ == "__main__":
    main()
