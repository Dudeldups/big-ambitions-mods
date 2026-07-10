#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BAModTemplate.Editor
{
    public static class ModAssetBundleCli
    {
        public static void BuildForMod()
        {
            var modName = GetArgumentValue("-modName");
            if (string.IsNullOrWhiteSpace(modName))
                throw new ArgumentException("Missing -modName argument.");

            var mod = FindMod(modName);
            if (mod == null)
                throw new ArgumentException("Could not find mod '" + modName + "'.");

            var bundleName = mod.Manifest.AssetBundleName;
            if (string.IsNullOrWhiteSpace(bundleName))
            {
                Debug.Log("Mod '" + mod.Manifest.ModId + "' has no AssetBundleName; nothing to build.");
                return;
            }

            var assignedCount = AssignBundleableAssets(mod, bundleName);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Assigned " + assignedCount + " asset(s) to bundle '" + bundleName + "'.");

            var assetPaths = AssetDatabase.GetAssetPathsFromAssetBundle(bundleName);
            if (assetPaths == null || assetPaths.Length == 0)
            {
                Debug.Log("No assets assigned to bundle '" + bundleName + "'.");
                return;
            }

            var build = CreateBuild(bundleName, assetPaths);
            foreach (var target in ResolveTargets(mod.Manifest.TargetPlatforms))
            {
                var platformFolder = PlatformFolderName(target);
                var outputDir = Path.Combine(mod.ModFolderAbsolutePath, "AssetBundles", platformFolder);
                Directory.CreateDirectory(outputDir);

                var manifest = BuildPipeline.BuildAssetBundles(
                    outputDir,
                    new[] { build },
                    BuildAssetBundleOptions.ChunkBasedCompression,
                    target);

                if (manifest == null)
                    throw new InvalidOperationException("AssetBundle build failed for " + target + ".");

                var producedPath = Path.Combine(outputDir, bundleName);
                if (!File.Exists(producedPath))
                    throw new FileNotFoundException("Expected AssetBundle output was not created.", producedPath);

                Debug.Log("Built AssetBundle: " + producedPath);
            }
        }

        private static DiscoveredMod? FindMod(string modName)
        {
            foreach (var mod in ModDiscovery.DiscoverAll())
            {
                if (string.Equals(mod.Manifest.ModId, modName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(mod.Manifest.DisplayName, modName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(mod.ModFolderAbsolutePath), modName, StringComparison.OrdinalIgnoreCase))
                {
                    return mod;
                }
            }

            return null;
        }

        private static int AssignBundleableAssets(DiscoveredMod mod, string fullBundleName)
        {
            var (baseName, variant) = SplitBundleName(fullBundleName);
            var changedCount = 0;
            var bundleableAssets = new HashSet<string>(FindBundleableModAssets(mod), StringComparer.OrdinalIgnoreCase);

            foreach (var assetPath in FindAllModFileAssets(mod))
            {
                var importer = AssetImporter.GetAtPath(assetPath);
                if (importer == null)
                    continue;

                if (!bundleableAssets.Contains(assetPath))
                {
                    if (string.Equals(importer.assetBundleName, baseName, StringComparison.Ordinal) &&
                        string.Equals(importer.assetBundleVariant, variant, StringComparison.Ordinal))
                    {
                        importer.SetAssetBundleNameAndVariant(string.Empty, string.Empty);
                        importer.SaveAndReimport();
                        changedCount++;
                    }

                    continue;
                }

                if (string.Equals(importer.assetBundleName, baseName, StringComparison.Ordinal) &&
                    string.Equals(importer.assetBundleVariant, variant, StringComparison.Ordinal))
                    continue;

                importer.SetAssetBundleNameAndVariant(baseName, variant);
                importer.SaveAndReimport();
                changedCount++;
            }

            return changedCount;
        }

        private static IEnumerable<string> FindAllModFileAssets(DiscoveredMod mod)
        {
            foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { mod.ModFolderAssetPath }))
            {
                var assetPath = NormaliseAssetPath(AssetDatabase.GUIDToAssetPath(guid));
                if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
                    continue;

                if (assetPath.StartsWith(mod.ModFolderAssetPath + "/AssetBundles/", StringComparison.OrdinalIgnoreCase))
                    continue;

                yield return assetPath;
            }
        }

        private static IEnumerable<string> FindBundleableModAssets(DiscoveredMod mod)
        {
            var excludedPrefixes = new List<string>();
            if (mod.Manifest.LocalesFolder != null)
                excludedPrefixes.Add(NormaliseAssetPath(AssetDatabase.GetAssetPath(mod.Manifest.LocalesFolder)) + "/");

            var enumsPath = mod.Manifest.EnumsFile != null
                ? NormaliseAssetPath(AssetDatabase.GetAssetPath(mod.Manifest.EnumsFile))
                : string.Empty;

            foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { mod.ModFolderAssetPath }))
            {
                var assetPath = NormaliseAssetPath(AssetDatabase.GUIDToAssetPath(guid));
                if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
                    continue;

                if (string.Equals(assetPath, mod.ManifestAssetPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(assetPath, mod.AsmdefAssetPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(assetPath, enumsPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (excludedPrefixes.Any(prefix => assetPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var extension = Path.GetExtension(assetPath);
                if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".asmdef", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.Equals(Path.GetFileName(assetPath), "thumbnail.png", StringComparison.OrdinalIgnoreCase))
                    continue;

                yield return assetPath;
            }
        }

        private static AssetBundleBuild CreateBuild(string fullBundleName, string[] assetPaths)
        {
            var (baseName, variant) = SplitBundleName(fullBundleName);
            return new AssetBundleBuild
            {
                assetBundleName = baseName,
                assetBundleVariant = variant,
                assetNames = assetPaths,
            };
        }

        private static IReadOnlyList<BuildTarget> ResolveTargets(ModTargetPlatforms flags)
        {
            var result = new List<BuildTarget>();
            if ((flags & ModTargetPlatforms.Windows) != 0)
                result.Add(BuildTarget.StandaloneWindows64);
            if ((flags & ModTargetPlatforms.Mac) != 0)
                result.Add(BuildTarget.StandaloneOSX);
            return result;
        }

        private static string PlatformFolderName(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return "Windows";
                case BuildTarget.StandaloneOSX:
                    return "Mac";
                case BuildTarget.StandaloneLinux64:
                    return "Linux";
                default:
                    return target.ToString();
            }
        }

        private static (string name, string variant) SplitBundleName(string fullBundleName)
        {
            var index = fullBundleName.LastIndexOf('.');
            if (index <= 0 || index >= fullBundleName.Length - 1)
                return (fullBundleName, string.Empty);

            return (fullBundleName.Substring(0, index), fullBundleName.Substring(index + 1));
        }

        private static string? GetArgumentValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            return null;
        }

        private static string NormaliseAssetPath(string? path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/').TrimEnd('/');
        }
    }
}
