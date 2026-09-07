using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class PassageAudioBuild
{
    public static void Build()
    {
        string root = "Assets/Mods/AudiRS6R/Audio/Passage01/";
        string[] paths = { root + "EngineLow.wav", root + "EngineMid.wav", root + "EngineHigh.wav" };
        foreach (var path in paths)
        {
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            var settings = importer.defaultSampleSettings;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null || clip.channels != 1 || clip.frequency != 44100)
                throw new Exception("Invalid passage_01 clip: " + path);
            var samples = new float[clip.samples];
            if (!clip.GetData(samples, 0)) throw new Exception("Unreadable PCM: " + path);
            double energy = 0; float peak = 0;
            foreach (float sample in samples) { energy += sample * sample; peak = Mathf.Max(peak, Mathf.Abs(sample)); }
            if (peak >= .9f || Math.Sqrt(energy / samples.Length) < .05 || Mathf.Abs(samples[0] - samples[samples.Length-1]) > .06f)
                throw new Exception("Audio signal validation failed: " + path);
        }
        var build = new AssetBundleBuild { assetBundleName = "audirs6r-engine.unity3d", assetNames = paths };
        foreach (var target in new[] { BuildTarget.StandaloneWindows64, BuildTarget.StandaloneOSX })
        {
            var output = "AudioBuild/" + (target == BuildTarget.StandaloneWindows64 ? "Windows" : "Mac");
            Directory.CreateDirectory(output);
            if (BuildPipeline.BuildAssetBundles(output, new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression, target) == null)
                throw new Exception("Audio bundle build failed: " + target);
        }
        // Inspect the actual Windows bundle, rather than only the imported source clips.
        var bundle = AssetBundle.LoadFromFile("AudioBuild/Windows/audirs6r-engine.unity3d");
        if (bundle == null) throw new Exception("Cannot reload audio bundle.");
        foreach (var path in paths)
            if (bundle.LoadAsset<AudioClip>(path) == null) throw new Exception("Missing packaged clip: " + path);
        bundle.Unload(true);
        Debug.Log("[PassageAudioBuild] PASS: 3 PCM clips, signal checks, Windows/Mac bundles, packaged clip lookup.");
    }
}
