from pathlib import Path
import re

REPO = Path(__file__).resolve().parents[1]
LIGHTING = REPO / "Assets/Mods/Volkswagen_Amarok/Scripts/VolkswagenAmarokLightingController.cs"
SETUP = REPO / "Assets/Mods/Volkswagen_Amarok/Editor/VolkswagenAmarokSetup.cs"

if not LIGHTING.is_file():
    raise SystemExit(f"Generated Amarok lighting source is missing: {LIGHTING}")
if not SETUP.is_file():
    raise SystemExit(f"Generated Amarok setup source is missing: {SETUP}")

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

# Older feedback-patch runs used a plain string replacement for TargetWidth.
# Because VisualTargetWidth itself contains that substring, repeated builds could
# expand it to VisualVisualTargetWidth (and beyond). Normalize both legacy damage
# and a plain TargetWidth use to exactly one VisualTargetWidth before Unity compiles.
setup_text = SETUP.read_text(encoding="utf-8")
setup_text, normalized_count = re.subn(
    r"(?<![A-Za-z0-9_])(?:Visual)*TargetWidth(?=\s*/\s*bounds\.size\.x)",
    "VisualTargetWidth",
    setup_text,
)
SETUP.write_text(setup_text, encoding="utf-8", newline="\n")
if normalized_count > 0:
    print(f"Normalized {normalized_count} Amarok visual-width reference(s) to VisualTargetWidth.")

if re.search(r"VisualVisual+TargetWidth", setup_text):
    raise SystemExit("Amarok visual-width symbol normalization failed.")

print("Volkswagen Amarok authored-light helper preflight passed.")
