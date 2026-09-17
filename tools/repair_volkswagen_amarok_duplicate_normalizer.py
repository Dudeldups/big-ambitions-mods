from pathlib import Path
import re

REPO = Path(__file__).resolve().parents[1]
MATERIALS = REPO / "Assets/Mods/Volkswagen_Amarok/Scripts/VolkswagenAmarokMaterials.cs"

if not MATERIALS.is_file():
    raise SystemExit(f"VolkswagenAmarokMaterials.cs is missing: {MATERIALS}")

text = MATERIALS.read_text(encoding="utf-8")

signature = "public static bool NormalizeImportedMaterial(Material material)"
count_before = text.count(signature)
if count_before == 0:
    raise SystemExit("NormalizeImportedMaterial(Material) is missing; run the Amarok nav/material patch first.")

# Remove every current copy. Method-level closing braces are indented by four
# spaces, while nested if-block braces are indented further, so this pattern
# safely consumes one complete method at a time.
method_pattern = re.compile(
    r"\n    public static bool NormalizeImportedMaterial\(Material material\)\s*\{.*?\n    \}",
    re.S,
)
text, removed = method_pattern.subn("", text)
if removed != count_before:
    raise SystemExit(
        f"Normalizer cleanup was ambiguous: signatures={count_before}, removed={removed}."
    )

is_transparent_pattern = re.compile(
    r"(    public static bool IsTransparentMaterial\(Material material\)\s*\{.*?\n    \})",
    re.S,
)
match = is_transparent_pattern.search(text)
if match is None:
    raise SystemExit("Could not find IsTransparentMaterial(Material) insertion point.")

normalizer = '''

    public static bool NormalizeImportedMaterial(Material material)
    {
        if (IsTransparentMaterial(material))
        {
            PrepareTransparentMaterial(material, VolkswagenAmarokTransparentRole.OtherClear);
            return true;
        }

        RebindToHdrpLit(material);
        return FixSolidHdrpMaterial(material);
    }'''

text = text[:match.end()] + normalizer + text[match.end():]
MATERIALS.write_text(text, encoding="utf-8", newline="\n")

count_after = MATERIALS.read_text(encoding="utf-8").count(signature)
if count_after != 1:
    raise SystemExit(f"Expected exactly one NormalizeImportedMaterial method, found {count_after}.")

print(f"Removed {max(0, count_before - 1)} duplicate NormalizeImportedMaterial definition(s).")
print("VolkswagenAmarokMaterials.cs now contains exactly one NormalizeImportedMaterial(Material) method.")
