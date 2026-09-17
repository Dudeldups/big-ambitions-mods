from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
SETUP = REPO / "Assets/Mods/Volkswagen_Amarok/Editor/VolkswagenAmarokSetup.cs"

if not SETUP.is_file():
    raise SystemExit("VolkswagenAmarokSetup.cs is missing; generate the Amarok source first.")

s = SETUP.read_text(encoding="utf-8")

# Capture the exact component layout immediately before serialization. If Unity
# turns one of those components into a missing-script slot while saving the
# prefab, the post-save validator can report the original type instead of only
# saying that one script is missing.
old_pre_save = '''            EnsureNoAudiDonorHierarchy(root);
            EnsureNoMissingScripts(root);
            EnsureNoErrorShaders(root);

            var result = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);'''
new_pre_save = '''            EnsureNoAudiDonorHierarchy(root);
            EnsureNoMissingScripts(root);
            EnsureNoErrorShaders(root);
            var componentLayoutBeforeSave = CaptureComponentLayout(root);

            var result = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);'''
if "componentLayoutBeforeSave = CaptureComponentLayout(root)" not in s:
    if old_pre_save not in s:
        raise SystemExit("Could not locate Amarok pre-save validation block.")
    s = s.replace(old_pre_save, new_pre_save, 1)

old_post = '''            EnsureNoAudiDonorHierarchy(result);
            EnsureNoMissingScripts(result);
            EnsureNoErrorShaders(result);'''
new_post = '''            EnsureNoAudiDonorHierarchy(result);
            EnsureNoMissingScripts(result, componentLayoutBeforeSave);
            EnsureNoErrorShaders(result);'''
if "EnsureNoMissingScripts(result, componentLayoutBeforeSave);" not in s:
    if old_post not in s:
        raise SystemExit("Could not locate Amarok post-save validation block.")
    s = s.replace(old_post, new_post, 1)

old_method = r'''    private static void EnsureNoMissingScripts(GameObject root)
    {
        var missing = 0;
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform != null)
                missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
        }
        if (missing != 0)
            throw new InvalidOperationException(
                $"Generated Amarok prefab still contains {missing} missing script component(s).");
    }
'''
new_method = r'''    private static void EnsureNoMissingScripts(
        GameObject root,
        Dictionary<string, string[]>? expectedLayout = null)
    {
        var details = new List<string>();
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform == null)
                continue;
            var gameObject = transform.gameObject;
            var missingCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
            if (missingCount <= 0)
                continue;

            var path = GetRelativeTransformPath(root.transform, transform);
            var components = gameObject.GetComponents<Component>();
            for (var index = 0; index < components.Length; index++)
            {
                if (components[index] != null)
                    continue;
                var expected = "<unknown-before-save>";
                if (expectedLayout != null &&
                    expectedLayout.TryGetValue(path, out var types) &&
                    index < types.Length)
                {
                    expected = types[index];
                }
                var previous = index > 0 && components[index - 1] != null
                    ? components[index - 1].GetType().FullName
                    : "<none>";
                var next = index + 1 < components.Length && components[index + 1] != null
                    ? components[index + 1].GetType().FullName
                    : "<none>";
                details.Add(
                    $"path='{path}' slot={index} expectedBeforeSave='{expected}' " +
                    $"previous='{previous}' next='{next}'");
            }
        }

        if (details.Count == 0)
            return;

        foreach (var detail in details)
            Debug.LogError("VolkswagenAmarok missing-script diagnostic: " + detail);
        throw new InvalidOperationException(
            $"Generated Amarok prefab still contains {details.Count} missing script component(s): " +
            string.Join(" | ", details));
    }

    private static Dictionary<string, string[]> CaptureComponentLayout(GameObject root)
    {
        var result = new Dictionary<string, string[]>();
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform == null)
                continue;
            var path = GetRelativeTransformPath(root.transform, transform);
            var components = transform.gameObject.GetComponents<Component>();
            var types = new string[components.Length];
            for (var index = 0; index < components.Length; index++)
                types[index] = components[index]?.GetType().FullName ?? "<missing-before-save>";
            result[path] = types;
        }
        return result;
    }

    private static string GetRelativeTransformPath(Transform root, Transform target)
    {
        if (target == root)
            return root.name;
        var names = new List<string>();
        for (var current = target; current != null && current != root; current = current.parent)
            names.Add(current.name);
        names.Reverse();
        return root.name + "/" + string.Join("/", names);
    }
'''

if "CaptureComponentLayout(GameObject root)" not in s:
    if old_method not in s:
        raise SystemExit("Could not locate EnsureNoMissingScripts implementation.")
    s = s.replace(old_method, new_method, 1)

required = [
    "componentLayoutBeforeSave = CaptureComponentLayout(root)",
    "EnsureNoMissingScripts(result, componentLayoutBeforeSave);",
    "VolkswagenAmarok missing-script diagnostic:",
    "expectedBeforeSave=",
]
missing = [item for item in required if item not in s]
if missing:
    raise SystemExit("Missing-script diagnostic patch failed; missing: " + ", ".join(missing))

SETUP.write_text(s, encoding="utf-8", newline="\n")
print("Patched Amarok missing-script diagnostics.")
print("Next Setup Volkswagen Amarok run will report the exact GameObject path, component slot and pre-save component type.")
