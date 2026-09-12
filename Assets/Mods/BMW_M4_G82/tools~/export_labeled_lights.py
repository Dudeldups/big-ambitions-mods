"""Bake labeled BMW lamp vertex groups into a minimal GLB for Unity.

Usage:
  blender --background --factory-startup --python export_labeled_lights.py -- \
    BMW_M4_Lights_Work.blend bmw_m4_lights.glb
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


def create_group_object(source, group_name, output_name):
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
    target.matrix_world = source.matrix_world.copy()
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
    bpy.ops.wm.open_mainfile(filepath=source_path)

    generated = []
    for object_name, group_mapping in GROUPS.items():
        source = bpy.data.objects.get(object_name)
        if source is None or source.type != "MESH":
            raise RuntimeError(f"Missing mesh object {object_name}")
        for group_name, output_name in group_mapping.items():
            generated.append(create_group_object(source, group_name, output_name))

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
