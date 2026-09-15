"""Export topology-safe SF90 functional lamp overlays from Blender vertex groups.

Workflow:
1. Import/open the supplied SF90 model in Blender.
2. On the actual Light_Geo_lodA mesh object create the REQUIRED_GROUPS below.
3. Assign only complete lamp faces to each group (weight 1.0).
4. Save the .blend, then run for example:

   blender --background SF90_Lights.blend --python extract_sf90_light_overlays.py -- \
       --production 2021_ferrari_sf90_spider.glb --output FerrariSF90LightOverlays.glb

The output belongs in Assets/Mods/Ferrari_SF90_Spider/Models/.
"""
from __future__ import annotations
import argparse, json, sys
from pathlib import Path
import bpy
from mathutils import Vector

PREFIX="VehicleLightRef_"
REQUIRED_GROUPS=(
    "DRL_FL","DRL_FR",
    "FrontIndicatorSecondary_FL","FrontIndicatorSecondary_FR",
    "FrontIndicator_FL","FrontIndicator_FR",
    "Headlamps","TailLights","BrakeLights","ThirdBrakeLight","ReverseLights",
    "RearIndicator_RL","RearIndicator_RR",
)

def parse_args():
    args=sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    p=argparse.ArgumentParser(); p.add_argument("--production",required=True); p.add_argument("--output",required=True)
    return p.parse_args(args)

def vertices_for_group(obj,group):
    return {v.index for v in obj.data.vertices for m in v.groups if m.group==group.index and m.weight>.5}

def bounds(points):
    mn=Vector(tuple(min(p[i] for p in points) for i in range(3)))
    mx=Vector(tuple(max(p[i] for p in points) for i in range(3)))
    return {"min":[round(v,6) for v in mn],"max":[round(v,6) for v in mx],"center":[round(v,6) for v in (mn+mx)*.5]}

def clear_scene():
    bpy.ops.object.select_all(action="SELECT"); bpy.ops.object.delete(use_global=False)
    for collection in (bpy.data.meshes,bpy.data.materials,bpy.data.images):
        for item in list(collection): collection.remove(item)

def main():
    opt=parse_args(); production=Path(opt.production).resolve(); output=Path(opt.output).resolve(); output.parent.mkdir(parents=True,exist_ok=True)
    records=[]; source_meshes={}; found_groups=set()
    for obj in bpy.data.objects:
        if obj.type!="MESH" or "Light_Geo" not in obj.name: continue
        for group in obj.vertex_groups:
            if group.name not in REQUIRED_GROUPS: continue
            selected=vertices_for_group(obj,group)
            if not selected: raise RuntimeError(f"Required group {group.name!r} on {obj.name!r} is empty")
            polygons=[p for p in obj.data.polygons if all(i in selected for i in p.vertices)]
            if not polygons: raise RuntimeError(f"Group {group.name!r} has no complete faces")
            used=sorted({i for p in polygons for i in p.vertices}); remap={s:t for t,s in enumerate(used)}
            verts=[obj.data.vertices[i].co.copy() for i in used]
            faces=[tuple(remap[i] for i in p.vertices) for p in polygons]
            smooth=[p.use_smooth for p in polygons]
            records.append({"sourceObject":obj.name,"sourceVertexCount":len(obj.data.vertices),"group":group.name,
                            "overlay":PREFIX+group.name,"selectedVertexCount":len(selected),"usedVertexCount":len(used),
                            "faceCount":len(faces),"localBounds":bounds(verts)})
            source_meshes[(obj.name,group.name)]={"vertices":verts,"faces":faces,"smooth":smooth}
            found_groups.add(group.name)
    missing=[g for g in REQUIRED_GROUPS if g not in found_groups]
    if missing: raise RuntimeError("Missing required SF90 lamp groups: "+", ".join(missing))

    # Verify the production GLB still has matching source topology.
    clear_scene(); bpy.ops.import_scene.gltf(filepath=str(production))
    prod={o.name:o for o in bpy.data.objects if o.type=="MESH"}
    for r in records:
        obj=prod.get(r["sourceObject"])
        if obj is None: raise RuntimeError(f"Production GLB has no exact mesh object {r['sourceObject']!r}")
        if len(obj.data.vertices)!=r["sourceVertexCount"]:
            raise RuntimeError(f"Topology mismatch for {r['sourceObject']!r}: labeled={r['sourceVertexCount']} production={len(obj.data.vertices)}")

    clear_scene(); generated=[]
    for r in records:
        src=source_meshes[(r["sourceObject"],r["group"])]
        mesh=bpy.data.meshes.new(r["overlay"]); mesh.from_pydata(src["vertices"],[],src["faces"]); mesh.update()
        for p,smooth in zip(mesh.polygons,src["smooth"]): p.use_smooth=bool(smooth)
        obj=bpy.data.objects.new(r["overlay"],mesh); bpy.context.scene.collection.objects.link(obj)
        obj.location=(0,0,0); obj.rotation_euler=(0,0,0); obj.scale=(1,1,1); generated.append(obj)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in generated: obj.select_set(True)
    bpy.context.view_layer.objects.active=generated[0]
    bpy.ops.export_scene.gltf(filepath=str(output),export_format="GLB",use_selection=True,export_materials="NONE",export_yup=True)
    output.with_suffix('.json').write_text(json.dumps({"production":production.name,"overlays":records},indent=2),encoding='utf-8')
    print(f"EXPORTED {len(records)} SF90 lamp overlays to {output}")
if __name__=='__main__': main()
