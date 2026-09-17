from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
PRIVATE_DRIVER = REPO / "Assets/Mods/Volkswagen_Amarok/Scripts/VolkswagenAmarokPrivateDriverSupport.cs"

if not PRIVATE_DRIVER.is_file():
    raise SystemExit(f"Generated Amarok private-driver source is missing: {PRIVATE_DRIVER}")

text = PRIVATE_DRIVER.read_text(encoding="utf-8")

# The third-feedback patch historically authors its intermediate NPC centre
# offset at 0.025 m and validates that value before the final stance correction
# turns it into 0.040 m. Normalize an already-patched worktree back to that
# intermediate state so repeated pull/build/test cycles stay idempotent.
if "private static void ApplyNpcBodyFitment" in text:
    text = text.replace(
        "body.localPosition += new Vector3(0f, 0.040f, 0f);",
        "body.localPosition += new Vector3(0f, 0.025f, 0f);",
    )
    PRIVATE_DRIVER.write_text(text, encoding="utf-8", newline="\n")
    print("Normalized existing Amarok NPC stance to the third-feedback intermediate state.")
else:
    print("Amarok NPC stance helper is not present yet; no pre-normalization needed.")
