from pathlib import Path
import re

REPO = Path(__file__).resolve().parents[1]
SETUP = REPO / "Assets/Mods/Volkswagen_Amarok/Editor/VolkswagenAmarokSetup.cs"

if not SETUP.is_file():
    raise SystemExit(f"Generated Amarok setup was not found: {SETUP}")

text = SETUP.read_text(encoding="utf-8")
marker = "    public static void GenerateAndBuild()\n"

method = '''    [MenuItem("Big Ambitions Mods/Build Volkswagen Amarok AssetBundle")]
    public static void BuildStandaloneWindowsAssetBundle()
    {
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        const string bundleFileName = "volkswagenamarok.unity3d";
        const string bundleBaseName = "volkswagenamarok";
        const string bundleVariant = "unity3d";

        var projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
        var temporaryOutputDirectory = System.IO.Path.Combine(
            projectRoot, "Temp", "VolkswagenAmarokAssetBundle", "Windows");
        if (System.IO.Directory.Exists(temporaryOutputDirectory))
            System.IO.Directory.Delete(temporaryOutputDirectory, true);
        System.IO.Directory.CreateDirectory(temporaryOutputDirectory);

        var build = new AssetBundleBuild
        {
            // Match the SDK ModPackager convention: "volkswagenamarok.unity3d"
            // is a base bundle name plus the "unity3d" variant, not a literal
            // base name containing a dot.
            assetBundleName = bundleBaseName,
            assetBundleVariant = bundleVariant,
            assetNames = new[]
            {
                VehicleAssetPath,
                VehiclePrefabPath,
            },
        };

        Debug.Log(
            $"VolkswagenAmarok: building Windows AssetBundle in temporary folder " +
            $"'{temporaryOutputDirectory}' from '{VehicleAssetPath}' and '{VehiclePrefabPath}'.");

        var manifest = BuildPipeline.BuildAssetBundles(
            temporaryOutputDirectory,
            new[] { build },
            BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64);
        if (manifest == null)
            throw new InvalidOperationException(
                "Volkswagen Amarok AssetBundle build failed. See the Unity Console entries immediately before this exception for the BuildPipeline error.");

        var producedBundlePath = System.IO.Path.Combine(temporaryOutputDirectory, bundleFileName);
        if (!System.IO.File.Exists(producedBundlePath))
        {
            var producedFiles = System.IO.Directory.Exists(temporaryOutputDirectory)
                ? string.Join(", ", System.IO.Directory.GetFiles(temporaryOutputDirectory))
                : "<temporary output directory missing>";
            throw new InvalidOperationException(
                $"Volkswagen Amarok AssetBundle manifest was created, but '{bundleFileName}' was not. " +
                $"Produced files: {producedFiles}");
        }

        var finalOutputDirectory = System.IO.Path.Combine(
            Application.dataPath, "Mods", "Volkswagen_Amarok", "AssetBundles", "Windows");
        System.IO.Directory.CreateDirectory(finalOutputDirectory);
        var finalBundlePath = System.IO.Path.Combine(finalOutputDirectory, bundleFileName);
        System.IO.File.Copy(producedBundlePath, finalBundlePath, true);

        var producedManifestPath = producedBundlePath + ".manifest";
        if (System.IO.File.Exists(producedManifestPath))
            System.IO.File.Copy(producedManifestPath, finalBundlePath + ".manifest", true);

        var bundle = AssetBundle.LoadFromFile(finalBundlePath);
        if (bundle == null)
            throw new InvalidOperationException(
                $"Could not load freshly built Amarok bundle '{finalBundlePath}'.");
        try
        {
            var vehicleType = bundle.LoadAsset<UnityEngine.Object>(VehicleAssetPath);
            var prefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
            if (vehicleType == null || prefab == null)
                throw new InvalidOperationException(
                    "Fresh Amarok bundle is missing VolkswagenAmarok.asset or VolkswagenAmarok.prefab.");
        }
        finally
        {
            bundle.Unload(false);
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log(
            $"VolkswagenAmarok: built Windows AssetBundle " +
            $"'Assets/Mods/Volkswagen_Amarok/AssetBundles/Windows/{bundleFileName}'.");
    }

'''

existing_pattern = re.compile(
    r'    \[MenuItem\("Big Ambitions Mods/Build Volkswagen Amarok AssetBundle"\)\]\n'
    r'    public static void BuildStandaloneWindowsAssetBundle\(\)\n'
    r'    \{.*?\n    \}\n\n(?=    public static void GenerateAndBuild\(\))',
    re.S,
)

if existing_pattern.search(text):
    text, count = existing_pattern.subn(method, text, count=1)
    if count != 1:
        raise SystemExit("Could not replace existing standalone AssetBundle build method.")
    print("Replaced Volkswagen Amarok standalone AssetBundle build with SDK-compatible build logic.")
else:
    if marker not in text:
        raise SystemExit("Could not find GenerateAndBuild() insertion point in VolkswagenAmarokSetup.cs.")
    text = text.replace(marker, method + marker, 1)
    print("Added Unity menu command: Big Ambitions Mods > Build Volkswagen Amarok AssetBundle")

SETUP.write_text(text, encoding="utf-8", newline="\n")

required = [
    '[MenuItem("Big Ambitions Mods/Build Volkswagen Amarok AssetBundle")]',
    'BuildTarget.StandaloneWindows64',
    'assetBundleName = bundleBaseName',
    'assetBundleVariant = bundleVariant',
    'Temp", "VolkswagenAmarokAssetBundle", "Windows"',
    'VehicleAssetPath,',
    'VehiclePrefabPath,',
]
source = SETUP.read_text(encoding="utf-8")
missing = [token for token in required if token not in source]
if missing:
    raise SystemExit("AssetBundle build patch preflight failed; missing: " + ", ".join(missing))

print("Volkswagen Amarok standalone AssetBundle build preflight passed.")
