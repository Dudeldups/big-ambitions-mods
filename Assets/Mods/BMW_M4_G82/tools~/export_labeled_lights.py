"""Bake labeled BMW lamp vertex groups into a minimal GLB for Unity.

Usage:
  blender --background --factory-startup --python export_labeled_lights.py -- \
    BMW_M4_Lights_Work.blend bmw_m4_lights.glb bmw_m4.glb
"""

import bpy
import os
import sys


GROUPS = {
    "Object_67": {
        "Front_Headlights": "BMWLightRef_FrontHeadlights",
        "Mirror_TurnSignal_Left": "BMWLightRef_MirrorTurnSignalLeft",
        "Mirror_TurnSignal_Right": "BMWLightRef_MirrorTurnSignalRight",
        "Rear_Brake_Lights": "BMWLightRef_RearBrakeLights",
        "Rear_Indicator_Left": "BMWLightRef_RearIndicatorLeft",
        "Rear_Indicator_Right": "BMWLightRef_RearIndicatorRight",
        "Rear_Reverse_Lights": "BMWLightRef_RearReverseLights",
        "Rear_Running_Lights": "BMWLightRef_RearRunningLights",
    },
    "Object_71": {
        "Front_DRL_TurnSignal_Outer_Left": "BMWLightRef_FrontOuterDrlIndicatorLeft",
        "Front_DRL_TurnSignal_Outer_Right": "BMWLightRef_FrontOuterDrlIndicatorRight",
        "Front_DRL_inner": "BMWLightRef_FrontInnerDrl",
    },
}


def group_vertices(source, group):
    result = set()
    for vertex in source.data.vertices:
        if any(
            assignment.group == group.index and assignment.weight > 0.5
            for assignment in vertex.groups
        ):
            result.add(vertex.index)
    return result


def create_group_object(source, placement_source, group_name, output_name):
    group = source.vertex_groups.get(group_name)
    if group is None:
        raise RuntimeError(f"Missing vertex group {source.name}/{group_name}")

    members = group_vertices(source, group)
    polygons = [
        polygon
        for polygon in source.data.polygons
        if all(vertex_index in members for vertex_index in polygon.vertices)
    ]
    if not polygons:
        raise RuntimeError(
            f"Vertex group {source.name}/{group_name} selects no complete faces"
        )

    used_indices = sorted(
        {
            vertex_index
            for polygon in polygons
            for vertex_index in polygon.vertices
        }
    )
    remap = {
        source_index: target_index
        for target_index, source_index in enumerate(used_indices)
    }
    vertices = [source.data.vertices[index].co.copy() for index in used_indices]
    faces = [[remap[index] for index in polygon.vertices] for polygon in polygons]

    mesh = bpy.data.meshes.new(output_name + "Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update(calc_edges=True)
    for polygon, source_polygon in zip(mesh.polygons, polygons):
        polygon.use_smooth = source_polygon.use_smooth

    target = bpy.data.objects.new(output_name, mesh)
    # Keep only Object_67/Object_71-local mesh coordinates. Unity binds each
    # extracted mesh to its matching production renderer transform, avoiding
    # any dependence on the subset GLB's generated root hierarchy.
    bpy.context.scene.collection.objects.link(target)
    print(
        f"BMW_LIGHT_EXPORT object={output_name} "
        f"vertices={len(vertices)} polygons={len(faces)}"
    )
    return target


def main():
    separator = sys.argv.index("--")
    source_path = os.path.abspath(sys.argv[separator + 1])
    output_path = os.path.abspath(sys.argv[separator + 2])
    reference_path = os.path.abspath(sys.argv[separator + 3])
    bpy.ops.wm.open_mainfile(filepath=source_path)

    labeled_sources = {}
    for object_name in GROUPS:
        source = bpy.data.objects.get(object_name)
        if source is None or source.type != "MESH":
            raise RuntimeError(f"Missing mesh object {object_name}")
        labeled_sources[object_name] = source

    for obj in bpy.context.scene.objects:
        obj.select_set(False)
    bpy.ops.import_scene.gltf(filepath=reference_path)
    imported_objects = list(bpy.context.selected_objects)

    placement_sources = {}
    for object_name in GROUPS:
        candidates = [
            obj
            for obj in imported_objects
            if obj.type == "MESH"
            and (obj.name == object_name or obj.name.startswith(object_name + "."))
        ]
        if len(candidates) != 1:
            raise RuntimeError(
                f"Production GLB requires one imported {object_name}, found "
                f"{[obj.name for obj in candidates]}"
            )
        placement_sources[object_name] = candidates[0]
        print(
            f"BMW_LIGHT_REFERENCE object={object_name} "
            f"imported={candidates[0].name} vertices={len(candidates[0].data.vertices)}"
        )

    generated = []
    for object_name, group_mapping in GROUPS.items():
        source = labeled_sources[object_name]
        placement_source = placement_sources[object_name]
        if len(source.data.vertices) != len(placement_source.data.vertices):
            raise RuntimeError(
                f"Vertex count mismatch for {object_name}: labeled="
                f"{len(source.data.vertices)} production={len(placement_source.data.vertices)}"
            )
        for group_name, output_name in group_mapping.items():
            generated.append(
                create_group_object(source, placement_source, group_name, output_name)
            )

    for obj in bpy.context.scene.objects:
        obj.select_set(obj in generated)

    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=output_path,
        export_format="GLB",
        use_selection=True,
        export_materials="NONE",
        export_cameras=False,
        export_lights=False,
        export_animations=False,
    )
    print(
        f"BMW_LIGHT_EXPORT_COMPLETE path={output_path} objects={len(generated)}"
    )


if __name__ == "__main__":
    main()
