$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
$stubPath = Join-Path ([IO.Path]::GetTempPath()) ('audi-audio-stub-' + [guid]::NewGuid().ToString('N') + '.cs')
$probePath = Join-Path ([IO.Path]::GetTempPath()) ('audi-audio-probe-' + [guid]::NewGuid().ToString('N') + '.cs')
$stub = @'
namespace UnityEngine
{
    public class Object { public static void Destroy(Object value) {} }
    public sealed class AudioClip : Object
    {
        public float[] Samples;
        public static AudioClip Create(string name, int frames, int channels, int rate, bool stream)
            => new AudioClip { Samples = new float[frames * channels] };
        public bool SetData(float[] data, int offset) { Samples = data; return true; }
    }
}
'@
$probe = @'
using System;
using System.IO;
public static class AudiAudioProbe
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(string audioPath)
    {
        float previous = 0;
        for (int step = 0; step <= 1000; step++)
        {
            var n = step / 1000f;
            var hz = AudiRS6RAudioModel.TargetHz(n);
            Require(hz >= previous, "Target pitch must increase continuously with RPM");
            previous = hz;
            double energy = 0;
            for (int i = 0; i < 3; i++)
            {
                var weight = AudiRS6RAudioModel.Weight(n, i);
                Require(weight >= -1e-6 && weight <= 1.000001, "Invalid crossfade weight");
                energy += weight * weight;
                var pitch = AudiRS6RAudioModel.Pitch(n, i);
                if (weight > .001)
                    Require(pitch >= .25 && pitch <= 3, "Audible layer exceeds Unity pitch range");
                Require(Math.Abs(pitch*AudiRS6RAudioModel.ReferenceHz(i)-hz)<.001,
                    "Layers disagree on acoustic pitch in crossfade");
            }
            Require(Math.Abs(energy-1)<.00001, "Crossfade has an energy dip or spike");
        }
        Require(AudiRS6RAudioModel.Normalize(900,900,7000)==0, "Idle normalization");
        Require(AudiRS6RAudioModel.Normalize(9000,900,7000)==1, "Limiter clamp");
        Require(AudiRS6RAudioModel.IdlePitch==1 && AudiRS6RAudioModel.IdleVolume(0)==.24f,
                "Original Car idle calibration must be preserved");
        foreach (var rpm in new[] {0f, 900f, 1100f, 1180f})
            Require(AudiRS6RAudioModel.DrivingBlend(rpm,900,7000)==0,
                    "Generated layers must be silent at idle");
        Require(Math.Abs(AudiRS6RAudioModel.DrivingBlend(1810,900,7000)-.5f)<.00001,
                "Idle/driving transition midpoint changed");
        Require(AudiRS6RAudioModel.DrivingBlend(2440,900,7000)==1 && AudiRS6RAudioModel.IdleVolume(1)==0,
                "Idle must finish fading out above the transition");
        Require(AudiRS6RAudioModel.TargetHz(0)==80 && AudiRS6RAudioModel.TargetHz(1)==180,
                "Lower driving pitch calibration changed");
        Require(AudiRS6RAudioModel.Weight(0,0)==1 && AudiRS6RAudioModel.Weight(.5f,1)==1 &&
                AudiRS6RAudioModel.Weight(1,2)==1, "Low/mid/high anchors");

        Require(AudiRS6RAudioModel.LoadBlend(0)==0 && AudiRS6RAudioModel.LoadBlend(.15f)==0 &&
                AudiRS6RAudioModel.LoadBlend(1)==1, "Load sound must follow throttle");
        AudiAudioEventTests.Run();

        int count=0;
        foreach (var file in Directory.GetFiles(audioPath,"*.wav"))
        {
            var clip=AudiRS6RWave.Load(file);
            Require(clip.Samples.Length>1000,"Decoded clip too short");
            double power=0;
            foreach (var sample in clip.Samples)
            {
                Require(!float.IsNaN(sample) && !float.IsInfinity(sample) && Math.Abs(sample)<.96,
                    "Invalid or clipped WAV sample");
                power+=sample*sample;
            }
            Require(power/clip.Samples.Length>.001,"WAV is silent or unexpectedly quiet");
            count++;
        }
        Require(count==9,"Expected three coast, three load, and three pop variants");
        var bad=Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(bad,new byte[44]);
            bool rejected=false;
            try { AudiRS6RWave.Load(bad); } catch(InvalidDataException) { rejected=true; }
            Require(rejected,"Malformed WAV accepted");
        }
        finally { File.Delete(bad); }
        Console.WriteLine("PASS: original idle, driving calibration, 1001 RPM crossfades and packaged WAV decoding.");
    }


}
'@
try {
    Set-Content -LiteralPath $stubPath -Value $stub
    Set-Content -LiteralPath $probePath -Value $probe
    Add-Type -Path @((Join-Path $modRoot 'Scripts/AudiRS6RAudioModel.cs'), (Join-Path $modRoot 'Scripts/AudiRS6RWave.cs'), (Join-Path $modRoot 'Scripts/AudiRS6RPopGate.cs'), (Join-Path $PSScriptRoot 'AudioEventTests.cs'), $stubPath, $probePath)
    [AudiAudioProbe]::Run((Join-Path $modRoot 'Config/Audio'))
} finally {
    Remove-Item -LiteralPath $stubPath, $probePath -ErrorAction SilentlyContinue
}
