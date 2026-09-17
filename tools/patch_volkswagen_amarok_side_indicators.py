from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
LIGHTING = REPO / "Assets/Mods/Volkswagen_Amarok/Scripts/VolkswagenAmarokLightingController.cs"

if not LIGHTING.is_file():
    raise SystemExit("VolkswagenAmarokLightingController.cs is missing; generate the Amarok source first.")

s = LIGHTING.read_text(encoding="utf-8")

field_marker = "    private MeshRenderer? rearRightBlinkerOverlay;\n"
field_insert = field_marker + "    private MeshRenderer? sideLeftBlinkerOverlay;\n    private MeshRenderer? sideRightBlinkerOverlay;\n"
if "sideLeftBlinkerOverlay" not in s:
    if field_marker not in s:
        raise SystemExit("Could not locate Amarok blinker fields.")
    s = s.replace(field_marker, field_insert, 1)

find_marker = '        var rr=FindRendererByHierarchy(r,"1IndicatorRR");\n'
find_insert = find_marker + '        var sideL=FindRendererByHierarchy(r,"SideIndicatorFL");\n        var sideR=FindRendererByHierarchy(r,"SideIndicatorFR");\n'
if 'FindRendererByHierarchy(r,"SideIndicatorFL")' not in s:
    if find_marker not in s:
        raise SystemExit("Could not locate Amarok rear-indicator renderer lookup.")
    s = s.replace(find_marker, find_insert, 1)

create_marker = '        leftBlinkerOverlay=CreateOverlay(fl,"IndicatorLeft",amber,5.2f,1.009f); rightBlinkerOverlay=CreateOverlay(fr,"IndicatorRight",amber,5.2f,1.009f);\n'
create_insert = create_marker + '        sideLeftBlinkerOverlay=CreateOverlay(sideL,"SideIndicatorLeft",amber,5.0f,1.008f); sideRightBlinkerOverlay=CreateOverlay(sideR,"SideIndicatorRight",amber,5.0f,1.008f);\n'
if 'SideIndicatorLeft' not in s:
    if create_marker not in s:
        raise SystemExit("Could not locate Amarok front-indicator overlay creation.")
    s = s.replace(create_marker, create_insert, 1)

apply_marker = '        SetEnabled(rearRightBlinkerOverlay, rightBlinker && flash);\n'
apply_insert = apply_marker + '        SetEnabled(sideLeftBlinkerOverlay, leftBlinker && flash);\n        SetEnabled(sideRightBlinkerOverlay, rightBlinker && flash);\n'
if 'SetEnabled(sideLeftBlinkerOverlay' not in s:
    if apply_marker not in s:
        raise SystemExit("Could not locate Amarok blinker state application.")
    s = s.replace(apply_marker, apply_insert, 1)

count_old = '''    private int CountBlinkerOverlays() =>
        (leftBlinkerOverlay != null ? 1 : 0) + (rightBlinkerOverlay != null ? 1 : 0) +
        (rearLeftBlinkerOverlay != null ? 1 : 0) + (rearRightBlinkerOverlay != null ? 1 : 0);'''
count_new = '''    private int CountBlinkerOverlays() =>
        (leftBlinkerOverlay != null ? 1 : 0) + (rightBlinkerOverlay != null ? 1 : 0) +
        (sideLeftBlinkerOverlay != null ? 1 : 0) + (sideRightBlinkerOverlay != null ? 1 : 0) +
        (rearLeftBlinkerOverlay != null ? 1 : 0) + (rearRightBlinkerOverlay != null ? 1 : 0);'''
if count_old in s:
    s = s.replace(count_old, count_new, 1)

LIGHTING.write_text(s, encoding="utf-8", newline="\n")
print("Volkswagen Amarok side indicators wired into the lighting controller.")
