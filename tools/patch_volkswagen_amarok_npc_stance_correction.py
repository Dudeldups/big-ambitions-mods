from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
PRIVATE_DRIVER = REPO / "Assets/Mods/Volkswagen_Amarok/Scripts/VolkswagenAmarokPrivateDriverSupport.cs"

if not PRIVATE_DRIVER.is_file():
    raise SystemExit(f"Generated Amarok private-driver source is missing: {PRIVATE_DRIVER}")

text = PRIVATE_DRIVER.read_text(encoding="utf-8")

# The player-body feedback pass lowers AmarokVisual by another 1.5 cm. To make
# the NPC/private-driver body end up +4 cm at the front axle and +1 cm at the
# rear axle relative to the user's previous in-game screenshot, compensate that
# global body change here: +4.0 cm at the body centre combined with -0.56 deg X
# gives approximately +5.5/+2.5 cm within the new source, i.e. net +4/+1 cm
# versus the previous build after the player's -1.5 cm body correction.
text = text.replace(
    "body.localPosition += new Vector3(0f, 0.025f, 0f);",
    "body.localPosition += new Vector3(0f, 0.040f, 0f);",
)

PRIVATE_DRIVER.write_text(text, encoding="utf-8", newline="\n")

check = PRIVATE_DRIVER.read_text(encoding="utf-8")
if "body.localPosition += new Vector3(0f, 0.040f, 0f);" not in check:
    raise SystemExit("Amarok NPC stance correction failed.")
if "body.localPosition += new Vector3(0f, 0.025f, 0f);" in check:
    raise SystemExit("Amarok NPC stance correction left the old centre offset active.")

print("Adjusted Amarok NPC/private-driver stance to net +4 cm front / +1 cm rear versus the previous build.")
