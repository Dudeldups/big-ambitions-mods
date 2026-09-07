using System;
using System.Collections.Generic;

public static class AudiAudioEventTests
{
    static void Check(bool ok,string text) { if(!ok) throw new Exception(text); }
    static int Trial(int seed,int gear,float rpm,bool gradual,double dt=0.02)
    {
        var gate=new AudiRS6RPopGate(seed);
        int count=0;
        double previous=-10;
        for(int i=0;i<6/dt;i++)
        {
            double t=i*dt;
            float throttle=t<.5?1f:gradual?(float)Math.Max(0,1-(t-.5)/1.5):0f;
            if(gate.Sample(true,t,rpm,throttle,gear,60)==AudiRS6RPopEvent.None) continue;
            Check(t>=.5 && t<1.4,"Lift pop outside bounded burst");
            Check(t-previous>=.084,"Burst pulses too close");
            Check(gate.BurstId==1,"Holding zero throttle started another burst");
            Check(gate.Intensity>0 && gate.Intensity<=1,"Invalid intensity");
            count++; previous=t;
        }
        Check(count<=3,"Lift exceeded three pops");
        Check(!gate.OverrunActive,"Burst never completed");
        return count;
    }
    static int ShiftTrial(int seed,int before,int after,float start,float end,float speed,bool delayed,float throttle=0f)
    {
        var gate=new AudiRS6RPopGate(seed);
        int count=0;
        for(int i=0;i<200;i++)
        {
            double t=i*.02;
            int gear=t<.5?before:after;
            float rpm=t<(delayed?.6:.5)?start:end;
            var pop=gate.Sample(true,t,rpm,throttle,gear,speed);
            if(pop==AudiRS6RPopEvent.None) continue;
            Check(pop==AudiRS6RPopEvent.Downshift,"Wrong downshift reason");
            Check(t>=.7 && t<1.4,"Downshift pop outside burst window");
            count++;
        }
        Check(count<=2,"Downshift exceeded two pops");
        return count;
    }
    public static void Run()
    {
        int strong=0,slow=0,highGear=0,medium=0,low=0,down=0,gentle=0,fast=0,kickdown=0;
        int highGearStrongBursts=0, highGear5500Bursts=0, fourthStrongBursts=0;
        for(int seed=0;seed<500;seed++)
        {
            strong+=Trial(seed,2,6500,false);
            fast+=Trial(seed,2,6500,false,1d/120d);
            slow+=Trial(seed,2,6500,true);
            medium+=Trial(seed,2,4000,false);
            highGear+=Trial(seed,6,4000,false);
            if(Trial(seed,6,6500,false)>0) highGearStrongBursts++;
            if(Trial(seed,6,5500,false)>0) highGear5500Bursts++;
            if(Trial(seed,4,6500,false)>0) fourthStrongBursts++;
            Check(Trial(seed,6,6500,true)==0,"Slow high-gear release triggered pop");
            low+=Trial(seed,2,1000,false);
            down+=ShiftTrial(seed,4,2,2800,4900,60,true);
            kickdown+=ShiftTrial(seed,4,2,2800,4900,60,true,1f);
            gentle+=ShiftTrial(seed,3,2,1100,1450,15,false);
            Check(ShiftTrial(seed,2,3,2800,5000,60,false)==0,"Upshift triggered pop");
            Check(ShiftTrial(seed,3,2,3000,3000,60,false)==0,"No-RPM-rise downshift triggered");
            Check(ShiftTrial(seed,3,2,2800,4900,0,false)==0,"Stationary downshift triggered");
        }
        Check(strong>600 && slow==0 && low==0,"Abrupt high-RPM lift should dominate slow/idle release");
        Check(medium>highGear*5 && highGear>0,"High-gear cruising pop probability too high");
        Check(highGearStrongBursts>320 && highGearStrongBursts<380,"Strong high-gear lift should commonly burst");
        Check(highGear5500Bursts>190 && highGear5500Bursts<250,"5500 RPM high-gear lifts still too suppressed");
        Check(fourthStrongBursts>highGearStrongBursts,"Lower gears must still favor lift bursts");
        Check(down>250 && gentle<5,"Downshift strength/RPM dependency failed");
        Check(kickdown>250,"RPM-raising kickdown burst cancelled by throttle");
        Check(strong==fast,"Abrupt lift outcome changed with render frame rate");
        float previous=-1;
        for(int rpm=900;rpm<=7000;rpm+=10)
        {
            float factor=AudiRS6RPopGate.RpmFactor(rpm);
            Check(factor>=previous && factor-previous<.01f || previous<0,"RPM probability has a step");
            previous=factor;
        }
        for(int gear=1;gear<=8;gear++)
        {
            previous=-1;
            for(int rpm=900;rpm<=7000;rpm+=10)
            {
                float chance=.99f*AudiRS6RPopGate.RpmFactor(rpm)*AudiRS6RPopGate.LiftGearFactor(gear,rpm);
                Check(chance>=0 && chance<=1,"Invalid lift probability");
                Check(previous<0 || (chance>=previous && chance-previous<.01f),"Lift chance has an RPM step");
                previous=chance;
            }
        }
        var gate2=new AudiRS6RPopGate(3);
        int lastId=0; double lastBurst=-10;
        for(int i=0;i<600;i++)
        {
            double t=i*.02;
            float throttle=t%1.2<.55?1:0;
            gate2.Sample(true,t,6500,throttle,2,50);
            if(gate2.BurstId!=lastId)
            {
                Check(t-lastBurst>=1,"Cooldown ignored between bursts");
                lastId=gate2.BurstId; lastBurst=t;
            }
        }
        Check(lastId>=2,"Throttle reapplication did not rearm");
        gate2.Sample(false,12,6500,0,2,50);
        Check(!gate2.OverrunActive,"Pause/exit retained burst");
        for(int i=0;i<100;i++) Check(gate2.Sample(true,12.02+i*.02,6500,0,2,50)==AudiRS6RPopEvent.None,"Resume/coasting retriggered");
        for(int i=0;i<100;i++) Check(gate2.Sample(true,15+i*.02,6500,i<20?1:0,-1,20)==AudiRS6RPopEvent.None,"Reverse popped");
        Console.WriteLine("PASS pop events: strong="+strong+" slow="+slow+" idle="+low+" medium="+medium+" highGear="+highGear+" aggressiveDown="+down+" gentleDown="+gentle);
        Console.WriteLine("PASS lift bursts / 500 releases: sixth6500="+highGearStrongBursts+" sixth5500="+highGear5500Bursts+" fourth6500="+fourthStrongBursts);
    }
}
