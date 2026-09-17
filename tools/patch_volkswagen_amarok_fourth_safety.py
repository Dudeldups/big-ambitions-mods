from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
RUNTIME = REPO / "Assets/Mods/Volkswagen_Amarok/Scripts/VolkswagenAmarokRuntime.cs"

if not RUNTIME.is_file():
    raise SystemExit(f"Generated Amarok runtime source is missing: {RUNTIME}")

text = RUNTIME.read_text(encoding="utf-8")

# Keep the expanded deformation list explicit. A broad material fallback such as
# every plastic/rubber/metal mesh can accidentally pull dashboard/cabin trim into
# crash deformation. Plates and requested exterior trim are already covered by
# hierarchy/name markers in the fourth feedback pass.
text = text.replace(
    '        return HasMaterial(filter, "phong5", "dorr_R", "chrome", "plastic", "rubber", "metal");',
    '        return false;',
)

RUNTIME.write_text(text, encoding="utf-8", newline="\n")

check = RUNTIME.read_text(encoding="utf-8")
if 'return HasMaterial(filter, "phong5", "dorr_R", "chrome", "plastic", "rubber", "metal");' in check:
    raise SystemExit("Amarok deformation safety patch failed to remove broad material fallback.")
if '"numberplate"' not in check or '"kennzeichen"' not in check:
    raise SystemExit("Amarok deformation safety patch lost explicit plate markers.")

print("Kept Amarok deformation expansion exterior-only; cabin plastic/rubber/metal remains excluded.")
