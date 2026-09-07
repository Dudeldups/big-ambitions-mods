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
        var gate = new AudiRS6RPopGate(42);
        for (int i=0;i<100;i++)
            Require(gate.Sample(true,i*.02,900,0,1)==AudiRS6RPopEvent.None,"Idle popped");
        gate.Reset();
        Require(gate.Sample(true,0,4500,.8f,2)==AudiRS6RPopEvent.None,"Entry popped");
        Require(gate.Sample(true,.1,4400,0,2)==AudiRS6RPopEvent.None,"Throttle edge forced a pop");
        Require(gate.OverrunActive,"Overrun was not armed after load");
        Require(gate.Sample(true,.15,4300,0,3)==AudiRS6RPopEvent.None,"Shift forced an immediate pop");
        gate.Sample(false,.2,4500,.9f,2);
        Require(!gate.OverrunActive,"Pause/exit retained overrun state");
        Require(gate.Sample(true,.3,4300,0,3)==AudiRS6RPopEvent.None,"Pause/exit left stale pop");
        gate.Sample(true,.4,4500,.9f,3);
        Require(gate.Sample(true,.5,4500,0,-1)==AudiRS6RPopEvent.None,"Reverse popped");
        gate.Sample(true,1,4500,.9f,2);
        Require(gate.Sample(true,3,4000,0,2)==AudiRS6RPopEvent.None,"Long suspension left stale pop");
        gate.Reset();
        for (int i=0;i<1000;i++)
            Require(gate.Sample(true,i*.02,5000,1,1+i%5)==AudiRS6RPopEvent.None,"Gear changes under load forced pops");
        int low=CountOverrun(1,.02), high=CountOverrun(5,.02), fast=CountOverrun(1,.01);
        Require(low>high*2 && high>0,"Lower gears must pop more often, with occasional high-gear pops");
        Require(Math.Abs(fast-low)<low*.15,"Pop rate depends excessively on render frame rate");

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
        Console.WriteLine("PASS: idle/load calibration, 1001 RPM crossfades, randomized same-gear overrun/gear bias/frame-rate checks, nine WAVs. Overrun counts: gear1="+low+" gear5="+high);
    }

    private static int CountOverrun(int gear, double step)
    {
        int count=0;
        var intervals=new System.Collections.Generic.HashSet<int>();
        for(int trial=0;trial<100;trial++)
        {
            var gate=new AudiRS6RPopGate(trial);
            double previous=-10;
            for(int frame=0;frame<4/step;frame++)
            {
                double time=frame*step;
                var pop=gate.Sample(true,time,4000,time<.5?.8f:0f,gear);
                if(pop==AudiRS6RPopEvent.None) continue;
                Require(time>=.66 && time<2.91,"Pop outside recently loaded overrun window");
                Require(time-previous>=.159,"Random bursts have no minimum spacing");
                if(previous>=0) intervals.Add((int)((time-previous)*100));
                previous=time;
                count++;
            }
            Require(!gate.OverrunActive,"Overrun did not expire after prolonged coasting");
        }
        Require(intervals.Count>3,"Pop timing follows a fixed cadence");
        return count;
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
