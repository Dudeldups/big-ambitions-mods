$ErrorActionPreference = 'Stop'
$modRoot = Split-Path -Parent $PSScriptRoot
$probePath = Join-Path ([IO.Path]::GetTempPath()) ('audi-options-' + [guid]::NewGuid().ToString('N') + '.cs')
$probe = @'
using System;
using System.Collections.Generic;
namespace UnityEngine {
    public static class PlayerPrefs {
        static Dictionary<string,int> values = new Dictionary<string,int>();
        public static int GetInt(string key,int fallback) => values.ContainsKey(key)?values[key]:fallback;
        public static void SetInt(string key,int value) { values[key]=value; }
        public static void Save() {}
    }
}
namespace BAModAPI {
    public class TestLogger { public void Info(string text) {} public void Warn(string text) {} }
    public class ModContext { public string ModId; public TestLogger Logger=new TestLogger(); }
}
namespace BigAmbitions.Mods {
    public class ModOptions {
        public string Key; public bool Default; public Action<bool> Callback;
        public ModOptions AddHeader(string label) => this;
        public ModOptions AddToggle(string key,string label,bool initial,Action<bool> callback) {
            Key=key; Default=initial; Callback=callback; return this;
        }
    }
    public static class OptionsService {
        public static ModOptions Current; public static int Count;
        public static void Register(string id,ModOptions options) { Current=options; Count++; }
        public static void RemoveModOptions(string id) { Current=null; Count--; }
    }
}
public static class AudiOptionsProbe {
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
    public static void Run() {
        var context=new BAModAPI.ModContext { ModId="audi-test" };
        AudiRS6ROptions.Initialize(context);
        Check(AudiRS6ROptions.ExhaustPopsEnabled,"Fresh install must enable pops");
        Check(BigAmbitions.Mods.OptionsService.Current.Default,"Native reset default must be enabled");
        var gate=new AudiRS6RPopGate(3);
        int changes=0;
        Action reset=()=>{changes++;gate.Reset();};
        AudiRS6ROptions.Changed+=reset;
        for(int i=0;i<25;i++) gate.Sample(true,i*.02,6500,1,2,50);
        gate.Sample(true,.5,6500,0,2,50);
        Check(gate.OverrunActive,"Test must start with a queued burst");
        BigAmbitions.Mods.OptionsService.Current.Callback(false);
        Check(!AudiRS6ROptions.ExhaustPopsEnabled && !gate.OverrunActive && changes==1,
            "Disabling must immediately clear burst, even without another update (paused menu)");
        Check(UnityEngine.PlayerPrefs.GetInt("m:audi-test:audirs6r_exhaust_pops",-1)==0,"Native preference key not saved");
        for(int i=0;i<100;i++) Check(gate.Sample(AudiRS6ROptions.ExhaustPopsEnabled,.52+i*.02,6500,i<30?1:0,2,50)==AudiRS6RPopEvent.None,"Disabled pop emitted");
        BigAmbitions.Mods.OptionsService.Current.Callback(true);
        for(int i=0;i<100;i++) Check(gate.Sample(AudiRS6ROptions.ExhaustPopsEnabled,3+i*.02,6500,0,2,50)==AudiRS6RPopEvent.None,"Re-enable replayed stale/coasting burst");
        int pops=0;
        for(int i=0;i<100;i++) if(gate.Sample(true,6+i*.02,6500,i<25?1:0,2,50)!=AudiRS6RPopEvent.None) pops++;
        Check(pops>0 && pops<=3,"Re-enabled pops did not resume on a fresh release");
        BigAmbitions.Mods.OptionsService.Current.Callback(false);
        int before=changes;
        BigAmbitions.Mods.OptionsService.Current.Callback(false);
        Check(changes==before,"Unchanged UI value retriggered diagnostics/reset");
        AudiRS6ROptions.Shutdown();
        Check(BigAmbitions.Mods.OptionsService.Count==0,"Options leaked on unload");
        AudiRS6ROptions.Initialize(context);
        Check(!AudiRS6ROptions.ExhaustPopsEnabled,"Saved disabled preference lost before options screen opened");
        BigAmbitions.Mods.OptionsService.Current.Callback(BigAmbitions.Mods.OptionsService.Current.Default);
        Check(AudiRS6ROptions.ExhaustPopsEnabled && UnityEngine.PlayerPrefs.GetInt("m:audi-test:audirs6r_exhaust_pops",0)==1,"Reset to Defaults did not restore pops");
        AudiRS6ROptions.Changed-=reset;
        AudiRS6ROptions.Shutdown();
        Console.WriteLine("PASS pop option: default, native persistence/reload, immediate cancellation, disabled suppression, clean re-enable, reset defaults and unload.");
    }
}
'@
try {
    Set-Content -LiteralPath $probePath -Value $probe
    Add-Type -Path @((Join-Path $modRoot 'Scripts/AudiRS6ROptions.cs'), (Join-Path $modRoot 'Scripts/AudiRS6RPopGate.cs'), $probePath)
    [AudiOptionsProbe]::Run()
} finally { Remove-Item -LiteralPath $probePath -ErrorAction SilentlyContinue }
