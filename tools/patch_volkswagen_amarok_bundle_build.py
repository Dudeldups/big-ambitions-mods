from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
SETUP = REPO / "Assets/Mods/Volkswagen_Amarok/Editor/VolkswagenAmarokSetup.cs"

if not SETUP.is_file():
    raise SystemExit(f"Generated Amarok setup was not found: {SETUP}")

text = SETUP.read_text(encoding="utf-8")

marker = "    public static void GenerateAndBuild()\n"
method_name = "BuildStandaloneWindowsAssetBundle"

if method_name not in text:
    if marker not in text:
        raise SystemExit("Could not find GenerateAndBuild() insertion point in VolkswagenAmarokSetup.cs.")

    method = '''    [MenuItem("Big Ambitions Mods/Build Volkswagen Amarok AssetBundle")]
    public static void BuildStandaloneWindowsAssetBundle()
    {
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        const string bundleFileName = "volkswagenamarok.unity3d";
        var outputDirectory = ModRoot + "/AssetBundles/Windows";
        System.IO.Directory.CreateDirectory(outputDirectory);

        var build = new AssetBundleBuild
        {
            assetBundleName = bundleFileName,
            assetNames = new[]
            {
                VehicleAssetPath,
                VehiclePrefabPath,
            },
        };

        var manifest = BuildPipeline.BuildAssetBundles(
            outputDirectory,
            new[] { build },
            BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64);
        if (manifest == null)
            throw new InvalidOperationException("Volkswagen Amarok AssetBundle build failed. Check the Unity Console.");

        var bundlePath = outputDirectory + "/" + bundleFileName;
        if (!System.IO.File.Exists(bundlePath))
            throw new InvalidOperationException($"Volkswagen Amarok AssetBundle was not created at '{bundlePath}'.");

        var bundle = AssetBundle.LoadFromFile(bundlePath);
        if (bundle == null)
            throw new InvalidOperationException($"Could not load freshly built Amarok bundle '{bundlePath}'.");
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
        Debug.Log($"VolkswagenAmarok: built Windows AssetBundle '{bundlePath}'.");
    }

'''
    text = text.replace(marker, method + marker, 1)
    SETUP.write_text(text, encoding="utf-8", newline="\n")
    print("Added Unity menu command: Big Ambitions Mods > Build Volkswagen Amarok AssetBundle")
else:
    print("Volkswagen Amarok standalone AssetBundle build command is already present.")

required = [
    '[MenuItem("Big Ambitions Mods/Build Volkswagen Amarok AssetBundle")]',
    'BuildTarget.StandaloneWindows64',
    'assetBundleName = bundleFileName',
    'VehicleAssetPath,',
    'VehiclePrefabPath,',
]
source = SETUP.read_text(encoding="utf-8")
missing = [token for token in required if token not in source]
if missing:
    raise SystemExit("AssetBundle build patch preflight failed; missing: " + ", ".join(missing))

print("Volkswagen Amarok standalone AssetBundle build preflight passed.")
