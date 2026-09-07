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
        Require(AudiRS6RAudioModel.Weight(0,0)==1 && AudiRS6RAudioModel.Weight(.5f,1)==1 &&
                AudiRS6RAudioModel.Weight(1,2)==1, "Low/mid/high anchors");

        var gate = new AudiRS6RPopGate();
        for (int i=0;i<100;i++)
            Require(gate.Sample(true,i*.02,900,0,1)==AudiRS6RPopEvent.None,"Idle popped");
        gate.Reset();
        Require(gate.Sample(true,0,4500,.8f,2)==AudiRS6RPopEvent.None,"Entry popped");
        Require(gate.Sample(true,.1,4400,0,2)==AudiRS6RPopEvent.ThrottleRelease,"Lift did not pop");
        Require(gate.Sample(true,.2,4300,0,2)==AudiRS6RPopEvent.None,"Steady coasting repeated pop");
        gate.Sample(true,.3,4500,.9f,2);
        Require(gate.Sample(true,.4,4200,.8f,3)==AudiRS6RPopEvent.None,"Cooldown ignored");
        gate.Sample(true,.7,4600,.9f,3);
        gate.Sample(true,1,4600,.9f,3);
        Require(gate.Sample(true,1.1,3500,.8f,4)==AudiRS6RPopEvent.Upshift,"Upshift did not pop");
        gate.Reset();
        gate.Sample(true,2,4500,.9f,3);
        Require(gate.Sample(true,2.1,4700,.9f,2)==AudiRS6RPopEvent.None,"Downshift popped");
        gate.Sample(false,2.2,4500,.9f,2);
        Require(gate.Sample(true,2.3,4300,0,3)==AudiRS6RPopEvent.None,"Pause/exit left stale pop");
        gate.Sample(true,2.4,4500,.9f,3);
        Require(gate.Sample(true,2.5,4500,0,-1)==AudiRS6RPopEvent.None,"Reverse popped");
        gate.Sample(true,3,4500,.9f,2);
        Require(gate.Sample(true,5,4000,0,2)==AudiRS6RPopEvent.None,"Long suspension left stale pop");
        gate.Reset();
        for (int i=0;i<1000;i++)
            Require(gate.Sample(true,i*.02,5000,1,2)==AudiRS6RPopEvent.None,"Steady acceleration popped repeatedly");

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
        Require(count==6,"Expected three layers and three pop variants");
        var bad=Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(bad,new byte[44]);
            bool rejected=false;
            try { AudiRS6RWave.Load(bad); } catch(InvalidDataException) { rejected=true; }
            Require(rejected,"Malformed WAV accepted");
        }
        finally { File.Delete(bad); }
        Console.WriteLine("PASS: 1001 RPM crossfades/pitch alignment; pop events/cooldown/inactive states; six decoded WAVs and malformed-WAV rejection.");
    }
}
'@
try {
    Set-Content -LiteralPath $stubPath -Value $stub
    Set-Content -LiteralPath $probePath -Value $probe
    Add-Type -Path @((Join-Path $modRoot 'Scripts/AudiRS6RAudioModel.cs'), (Join-Path $modRoot 'Scripts/AudiRS6RWave.cs'), $stubPath, $probePath)
    [AudiAudioProbe]::Run((Join-Path $modRoot 'Config/Audio'))
} finally {
    Remove-Item -LiteralPath $stubPath, $probePath -ErrorAction SilentlyContinue
}
