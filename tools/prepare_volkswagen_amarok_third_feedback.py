from pathlib import Path
import re

REPO = Path(__file__).resolve().parents[1]
PRIVATE_DRIVER = REPO / "Assets/Mods/Volkswagen_Amarok/Scripts/VolkswagenAmarokPrivateDriverSupport.cs"

if not PRIVATE_DRIVER.is_file():
    raise SystemExit(f"Generated Amarok private-driver source is missing: {PRIVATE_DRIVER}")

text = PRIVATE_DRIVER.read_text(encoding="utf-8")

# The third-feedback patch historically authors its intermediate NPC centre
# offset at 0.025 m and validates that value before the later stance/fourth-pass
# corrections. Normalize any already-tested later helper back to that exact
# intermediate method so repeated pull/build/test cycles remain idempotent.
if "private static void ApplyNpcBodyFitment" in text:
    pattern = re.compile(
        r"    private static void ApplyNpcBodyFitment\(GameObject clone\)\s*\{.*?\n    \}\n\n",
        re.S,
    )
    intermediate = '''    private static void ApplyNpcBodyFitment(GameObject clone)
    {
        foreach (var name in new[] { "AmarokVisual", "AmarokDamageBody" })
        {
            var body = FindTransform(clone.transform, name);
            if (body == null)
                continue;

            body.localPosition += new Vector3(0f, 0.025f, 0f);
            body.localRotation = Quaternion.Euler(-0.56f, 0f, 0f) * body.localRotation;
        }
    }

'''
    text, count = pattern.subn(intermediate, text, count=1)
    if count != 1:
        raise SystemExit("Could not normalize Amarok NPC stance helper to third-feedback state.")
    PRIVATE_DRIVER.write_text(text, encoding="utf-8", newline="\n")
    print("Normalized existing Amarok NPC stance to the third-feedback intermediate state.")
else:
    print("Amarok NPC stance helper is not present yet; no pre-normalization needed.")
