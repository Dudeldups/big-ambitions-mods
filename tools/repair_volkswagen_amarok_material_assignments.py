from pathlib import Path
import json
import re
import struct

REPO = Path(__file__).resolve().parents[1]
MOD = REPO / "Assets/Mods/Volkswagen_Amarok"
MODEL = MOD / "Models/2017_volkswagen_amarok_v6.glb"
TEXTURE_DIR = MOD / "Models/ExtractedTextures"
MANIFEST = MOD / "Config/AmarokMaterialManifest.json"
SETUP = MOD / "Editor/VolkswagenAmarokSetup.cs"
CREATE = REPO / "tools/create_volkswagen_amarok.py"

for path in (MODEL, SETUP):
    if not path.is_file():
        raise SystemExit(f"Required Amarok file is missing: {path}")


def read_glb(path: Path):
    with path.open("rb") as handle:
        magic, version, total_length = struct.unpack("<4sII", handle.read(12))
        if magic != b"glTF" or version != 2:
            raise SystemExit("Expected a glTF 2.0 binary (.glb) Amarok source model.")
        json_data = None
        bin_data = None
        while handle.tell() < total_length:
            chunk_length, chunk_type = struct.unpack("<II", handle.read(8))
            chunk = handle.read(chunk_length)
            if chunk_type == 0x4E4F534A:
                json_data = json.loads(chunk.decode("utf-8").rstrip("\x00 \t\r\n"))
            elif chunk_type == 0x004E4942:
                bin_data = chunk
        if json_data is None or bin_data is None:
            raise SystemExit("The Amarok GLB is missing its JSON or BIN chunk.")
        return json_data, bin_data


gltf, binary = read_glb(MODEL)
materials = gltf.get("materials", [])
meshes = gltf.get("meshes", [])
images = gltf.get("images", [])
textures = gltf.get("textures", [])
buffer_views = gltf.get("bufferViews", [])

if len(materials) != 22:
    raise SystemExit(f"Expected 22 Amarok source materials; found {len(materials)}.")
if len(meshes) != 83:
    raise SystemExit(f"Expected 83 Amarok source meshes; found {len(meshes)}.")

TEXTURE_DIR.mkdir(parents=True, exist_ok=True)
image_paths = {}
for index, image in enumerate(images):
    view_index = image.get("bufferView")
    mime = image.get("mimeType")
    if view_index is None or mime not in ("image/png", "image/jpeg"):
        raise SystemExit(f"Unsupported embedded Amarok image {index}: {image}")
    view = buffer_views[view_index]
    offset = view.get("byteOffset", 0)
    length = view["byteLength"]
    data = binary[offset:offset + length]
    extension = ".png" if mime == "image/png" else ".jpg"
    destination = TEXTURE_DIR / f"AmarokTexture_{index:02d}{extension}"
    destination.write_bytes(data)
    image_paths[index] = destination.relative_to(REPO).as_posix()

material_specs = []
for index, material in enumerate(materials):
    pbr = material.get("pbrMetallicRoughness", {})
    texture_index = pbr.get("baseColorTexture", {}).get("index")
    texture_path = ""
    if texture_index is not None:
        source_image = textures[texture_index].get("source")
        if source_image is None or source_image not in image_paths:
            raise SystemExit(f"Material {material.get('name')} references an unresolved base-color texture.")
        texture_path = image_paths[source_image]
    material_specs.append({
        "index": index,
        "name": material.get("name") or f"Material_{index}",
        "alphaMode": material.get("alphaMode", "OPAQUE"),
        "doubleSided": bool(material.get("doubleSided", False)),
        "baseColorFactor": pbr.get("baseColorFactor", [1.0, 1.0, 1.0, 1.0]),
        "metallicFactor": float(pbr.get("metallicFactor", 1.0)),
        "roughnessFactor": float(pbr.get("roughnessFactor", 1.0)),
        "baseColorTexturePath": texture_path,
    })

bindings = []
for mesh in meshes:
    primitives = mesh.get("primitives", [])
    if len(primitives) != 1:
        raise SystemExit(f"Amarok mesh '{mesh.get('name')}' does not have exactly one primitive.")
    material_index = primitives[0].get("material")
    if material_index is None:
        raise SystemExit(f"Amarok mesh '{mesh.get('name')}' has no source material.")
    bindings.append({
        "rendererName": mesh.get("name") or "",
        "materialName": materials[material_index].get("name") or f"Material_{material_index}",
    })

if len({entry["rendererName"] for entry in bindings}) != len(bindings):
    raise SystemExit("Amarok mesh names are not unique; deterministic material binding is unsafe.")

MANIFEST.parent.mkdir(parents=True, exist_ok=True)
MANIFEST.write_text(json.dumps({"materials": material_specs, "meshes": bindings}, indent=2) + "\n", encoding="utf-8")

setup = SETUP.read_text(encoding="utf-8")
const_marker = '    private const string LightOverlayModelPath = ModRoot + "/Models/AmarokLightOverlays.glb";\n'
const_insert = const_marker + '    private const string MaterialManifestPath = ModRoot + "/Config/AmarokMaterialManifest.json";\n'
if "MaterialManifestPath" not in setup:
    if const_marker not in setup:
        raise SystemExit("Could not locate LightOverlayModelPath in VolkswagenAmarokSetup.cs.")
    setup = setup.replace(const_marker, const_insert, 1)

old_calls = '''            AssignPersistentMaterials(modelInstance);\n            ConvertAmarokOpaqueMaterialsToHdrp(modelInstance);'''
new_call = '''            AssignAmarokMaterialsFromManifest(modelInstance);'''
if old_calls in setup:
    setup = setup.replace(old_calls, new_call, 1)
elif "AssignAmarokMaterialsFromManifest(modelInstance);" not in setup:
    setup = setup.replace("            AssignPersistentMaterials(modelInstance);", new_call, 1)

helper_marker = "    private static void AssignPersistentMaterials(GameObject model)\n"
helper = r'''    [Serializable]
    private sealed class AmarokMaterialManifest
    {
        public AmarokMaterialSpec[] materials = Array.Empty<AmarokMaterialSpec>();
        public AmarokMeshMaterialBinding[] meshes = Array.Empty<AmarokMeshMaterialBinding>();
    }

    [Serializable]
    private sealed class AmarokMaterialSpec
    {
        public int index;
        public string name = string.Empty;
        public string alphaMode = "OPAQUE";
        public bool doubleSided;
        public float[] baseColorFactor = Array.Empty<float>();
        public float metallicFactor = 1f;
        public float roughnessFactor = 1f;
        public string baseColorTexturePath = string.Empty;
    }

    [Serializable]
    private sealed class AmarokMeshMaterialBinding
    {
        public string rendererName = string.Empty;
        public string materialName = string.Empty;
    }

    private static void AssignAmarokMaterialsFromManifest(GameObject model)
    {
        var manifestAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(MaterialManifestPath) ??
                            throw new InvalidOperationException(
                                "Amarok material manifest is missing. Run tools/repair_volkswagen_amarok_material_assignments.py first.");
        var manifest = JsonUtility.FromJson<AmarokMaterialManifest>(manifestAsset.text) ??
                       throw new InvalidOperationException("Amarok material manifest could not be parsed.");
        if (manifest.materials.Length != 22 || manifest.meshes.Length != 83)
            throw new InvalidOperationException(
                $"Amarok material manifest is incomplete materials={manifest.materials.Length}/22 meshes={manifest.meshes.Length}/83.");

        EnsureAssetFolder(MaterialFolder);
        var generated = new Dictionary<string, Material>(StringComparer.Ordinal);
        foreach (var spec in manifest.materials)
        {
            if (string.IsNullOrEmpty(spec.name))
                throw new InvalidOperationException("Amarok material manifest contains an unnamed material.");

            var assetName = $"AmarokSource_{spec.index:D2}_{SanitizeAssetName(spec.name)}";
            var assetPath = $"{MaterialFolder}/{assetName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("High Definition Render Pipeline/Lit") ??
                             throw new InvalidOperationException("HDRP/Lit shader is unavailable in the SDK project.");
                material = new Material(shader) { name = assetName };
                AssetDatabase.CreateAsset(material, assetPath);
            }
            else
            {
                material.name = assetName;
            }

            // The runtime helper performs the SDK's known-good HDRP state setup.
            // Restore the actual glTF material data afterwards so the conversion
            // does not replace the source color/texture with generic defaults.
            VolkswagenAmarokMaterials.NormalizeImportedMaterial(material);

            var factor = spec.baseColorFactor != null && spec.baseColorFactor.Length >= 4
                ? new Color(spec.baseColorFactor[0], spec.baseColorFactor[1], spec.baseColorFactor[2], spec.baseColorFactor[3])
                : Color.white;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", factor);
            if (material.HasProperty("_Color")) material.SetColor("_Color", factor);
            if (material.HasProperty("baseColorFactor")) material.SetColor("baseColorFactor", factor);

            Texture2D? baseTexture = null;
            if (!string.IsNullOrEmpty(spec.baseColorTexturePath))
            {
                baseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(spec.baseColorTexturePath) ??
                              throw new InvalidOperationException(
                                  $"Amarok texture '{spec.baseColorTexturePath}' for material '{spec.name}' is missing.");
            }
            if (material.HasProperty("_BaseColorMap")) material.SetTexture("_BaseColorMap", baseTexture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", baseTexture);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", spec.metallicFactor);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 1f - Mathf.Clamp01(spec.roughnessFactor));

            if (spec.doubleSided)
            {
                if (material.HasProperty("_DoubleSidedEnable")) material.SetFloat("_DoubleSidedEnable", 1f);
                if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Off);
                if (material.HasProperty("_CullMode")) material.SetFloat("_CullMode", (float)CullMode.Off);
                if (material.HasProperty("_CullModeForward")) material.SetFloat("_CullModeForward", (float)CullMode.Off);
                if (material.HasProperty("_TransparentCullMode")) material.SetFloat("_TransparentCullMode", (float)CullMode.Off);
                material.EnableKeyword("_DOUBLESIDED_ON");
            }

            EditorUtility.SetDirty(material);
            generated[spec.name] = material;
        }

        var bindingByRenderer = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var binding in manifest.meshes)
        {
            if (string.IsNullOrEmpty(binding.rendererName) || string.IsNullOrEmpty(binding.materialName))
                throw new InvalidOperationException("Amarok material manifest contains an incomplete renderer binding.");
            bindingByRenderer[binding.rendererName] = binding.materialName;
        }

        var assigned = 0;
        var unmapped = new List<string>();
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter?.sharedMesh == null)
                continue;
            if (!bindingByRenderer.TryGetValue(renderer.name, out var materialName) ||
                !generated.TryGetValue(materialName, out var material))
            {
                unmapped.Add(renderer.name);
                continue;
            }
            renderer.sharedMaterials = new[] { material };
            assigned++;
        }

        if (unmapped.Count != 0 || assigned != manifest.meshes.Length)
            throw new InvalidOperationException(
                $"Amarok material assignment incomplete assigned={assigned}/{manifest.meshes.Length}; " +
                $"unmapped={string.Join(", ", unmapped.GetRange(0, Math.Min(8, unmapped.Count)))}");

        var nullSlots = 0;
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            foreach (var material in renderer.sharedMaterials)
                if (material == null) nullSlots++;
        if (nullSlots != 0)
            throw new InvalidOperationException(
                $"Amarok model still contains {nullSlots} null material slot(s) immediately after manifest assignment.");

        Debug.Log(
            $"VolkswagenAmarok: assigned source materials from GLB manifest " +
            $"renderers={assigned}/83 materials={generated.Count}/22 nullSlots={nullSlots}.");
    }

'''
if "private static void AssignAmarokMaterialsFromManifest(GameObject model)" not in setup:
    if helper_marker not in setup:
        raise SystemExit("Could not locate AssignPersistentMaterials insertion point in VolkswagenAmarokSetup.cs.")
    setup = setup.replace(helper_marker, helper + helper_marker, 1)

required = [
    "MaterialManifestPath",
    "AssignAmarokMaterialsFromManifest(modelInstance);",
    "private static void AssignAmarokMaterialsFromManifest(GameObject model)",
    "renderers={assigned}/83 materials={generated.Count}/22 nullSlots={nullSlots}",
]
missing = [needle for needle in required if needle not in setup]
if missing:
    raise SystemExit("Amarok material-assignment source patch failed: " + ", ".join(missing))
SETUP.write_text(setup, encoding="utf-8", newline="\n")

if CREATE.is_file():
    create = CREATE.read_text(encoding="utf-8")
    call = '    runpy.run_path(str(TOOLS / "repair_volkswagen_amarok_material_assignments.py"), run_name="__main__")\n'
    anchor = '    runpy.run_path(str(TOOLS / "repair_volkswagen_amarok_duplicate_normalizer.py"), run_name="__main__")\n'
    if call not in create:
        if anchor in create:
            create = create.replace(anchor, anchor + call, 1)
        else:
            anchor = '    runpy.run_path(str(TOOLS / "patch_volkswagen_amarok_nav_and_materials.py"), run_name="__main__")\n'
            if anchor not in create:
                raise SystemExit("Could not locate Amarok generator patch chain.")
            create = create.replace(anchor, anchor + call, 1)
        CREATE.write_text(create, encoding="utf-8", newline="\n")

print(f"Extracted {len(images)} embedded Amarok texture image(s) into {TEXTURE_DIR.relative_to(REPO)}.")
print(f"Wrote deterministic material manifest with {len(material_specs)} materials and {len(bindings)} mesh bindings.")
print("Patched VolkswagenAmarokSetup.cs to ignore the GLB importer's null material slots and rebuild/assign HDRP materials from the source GLB manifest.")
print("Volkswagen Amarok source-material assignment preflight passed.")
