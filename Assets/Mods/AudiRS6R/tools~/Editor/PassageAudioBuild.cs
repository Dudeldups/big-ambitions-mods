using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class PassageAudioBuild
{
    public static void Build()
    {
        string root = "Assets/Mods/AudiRS6R/Audio/Passage01/";
        string[] paths = { root + "EngineBody.wav" };
        float previous = 0;
        for (int i = 0; i <= 1000; i++)
        {
            float pitch = AudiRS6REngineTone.Pitch(i / 1000f);
            if (float.IsNaN(pitch) || pitch < .649f || pitch > 1.851f || pitch < previous)
                throw new Exception("Invalid production pitch curve.");
            previous = pitch;
        }
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
        if (bundle.LoadAllAssets<AudioClip>().Length != 1) throw new Exception("Expected exactly one engine voice.");
        foreach (var path in paths)
            if (bundle.LoadAsset<AudioClip>(path) == null) throw new Exception("Missing packaged clip: " + path);
        bundle.Unload(true);
        Debug.Log("[PassageAudioBuild] PASS: one PCM clip, signal checks, 1001 production pitch samples, Windows/Mac bundles, packaged clip lookup.");
    }
}
