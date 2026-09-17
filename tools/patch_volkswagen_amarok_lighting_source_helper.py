from pathlib import Path
import runpy

REPO = Path(__file__).resolve().parents[1]
TOOLS = REPO / "tools"
LIGHTING = REPO / "Assets/Mods/Volkswagen_Amarok/Scripts/VolkswagenAmarokLightingController.cs"

if not LIGHTING.is_file():
    raise SystemExit(f"Generated Amarok lighting source is missing: {LIGHTING}")

text = LIGHTING.read_text(encoding="utf-8")
method_signature = "    private MeshRenderer? PrepareSourceOverlay(\n"

if method_signature not in text:
    marker = "    private void Update()\n"
    if marker not in text:
        raise SystemExit("Could not locate VolkswagenAmarokLightingController.Update().")

    helper = '''    private MeshRenderer? PrepareSourceOverlay(
        MeshRenderer? source,
        string suffix,
        Color color,
        float intensity)
    {
        if (source == null || source.GetComponent<MeshFilter>()?.sharedMesh == null)
        {
            LogWarning($"Amarok authored light source '{suffix}' is missing.");
            return null;
        }

        source.sharedMaterial = CreateUnlitMaterial(
            "VolkswagenAmarok_" + suffix + " Material", color, intensity);
        source.shadowCastingMode = ShadowCastingMode.Off;
        source.receiveShadows = false;
        source.enabled = false;
        return source;
    }

'''
    text = text.replace(marker, helper + marker, 1)
    LIGHTING.write_text(text, encoding="utf-8", newline="\n")
    print("Inserted Amarok authored-light source renderer helper.")
else:
    print("Amarok authored-light source renderer helper is already present.")

check = LIGHTING.read_text(encoding="utf-8")
required = [
    "private MeshRenderer? PrepareSourceOverlay(",
    "source.sharedMaterial = CreateUnlitMaterial(",
    "source.shadowCastingMode = ShadowCastingMode.Off;",
    "source.enabled = false;",
]
missing = [item for item in required if item not in check]
if missing:
    raise SystemExit("Amarok lighting helper patch failed; missing: " + ", ".join(missing))

print("Volkswagen Amarok authored-light helper preflight passed.")

# The PowerShell builder already invokes this helper immediately before launching
# Unity. Chain the feedback-prefab build patch here so the existing public
# executeMethod is redirected away from Generate(), which unnecessarily depended
# on a loadable Audi donor VehicleType asset.
runpy.run_path(
    str(TOOLS / "patch_volkswagen_amarok_existing_prefab_feedback_build.py"),
    run_name="__main__",
)
