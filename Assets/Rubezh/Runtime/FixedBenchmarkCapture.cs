using System;
using System.Collections.Generic;

namespace Rubezh.UnityPort
{
    [Serializable] public sealed class BenchmarkMetric
    {
        public int count;
        public double totalMs,meanMs,maxMs,p50Ms,p95Ms,p99Ms;
    }
    [Serializable] public sealed class FixedBenchmarkReport
    {
        public bool completed,simulationStayedPaused,mapStayedUnchanged,cameraMotionTest;
        public string mode,pairId;public int phase;
        public float startRealtime,endRealtime,elapsed;
        public int startTick,endTick,startMapRevision,endMapRevision,renderCount,storedOperations,droppedOperations,cameraChanges;
        public double rendersPerSecond;
        public BenchmarkMetric render,camera,compose,scene,mask,frameIntervals;
        public PerformanceSpike[] operations;
        public string scope="Only this controlled-camera benchmark; cameraMotionTest identifies the scripted pan/zoom test. Render/camera are CPU wall durations, not GPU timings. Frame intervals are between consecutive completed renders; no pre-test frames. Totals cover all renders; percentiles and operations retain the first 8192 renders, with any overflow reported explicitly. Initial cold render is included. Camera changes exclude the first render.";
    }
    // Independent from the rolling slow-operation queue and lifetime FPS history.
    // No Unity API: can regression-test collection/overflow without a graphics device.
    public sealed class FixedBenchmarkCapture
    {
        const int Capacity=8192;
        readonly List<PerformanceSpike> samples=new List<PerformanceSpike>(2048);
        readonly float start;
        readonly string mode,pairId;readonly int phase;readonly bool moving;
        readonly int startTick,startRevision;
        int count,cameraChanges,endTick,endRevision;
        float end;
        bool completed,finished,simulationStayedPaused=true,mapStayedUnchanged=true;
        readonly Totals render=new Totals(),camera=new Totals(),compose=new Totals(),scene=new Totals(),mask=new Totals(),intervals=new Totals();
        float previousEnd;
        sealed class Totals
        {
            public int Count;public double Sum,Max;
            public void Add(double value){Count++;Sum+=value;Max=Math.Max(Max,value);}
        }
        public FixedBenchmarkCapture(float startRealtime,int tick,int revision,string mode="",string pairId="",int phase=1,bool moving=false)
        {this.moving=moving;this.mode=mode;this.pairId=pairId;this.phase=phase;start=startRealtime;startTick=endTick=tick;startRevision=endRevision=revision;end=start;}
        public void Record(PerformanceSpike operation)
        {
            if(finished)return;
            if(count>0){intervals.Add(Math.Max(0,(operation.time-previousEnd)*1000.0));if(operation.cameraChanged)cameraChanges++;}
            previousEnd=operation.time;end=operation.time;endTick=operation.tick;endRevision=operation.mapRevision;
            simulationStayedPaused&=endTick==startTick;mapStayedUnchanged&=endRevision==startRevision;
            count++;render.Add(operation.ms);camera.Add(operation.cameraMs);compose.Add(operation.composeMs);scene.Add(operation.sceneMs);mask.Add(operation.maskMs);
            if(samples.Count<Capacity)samples.Add(operation);
        }
        public void Finish(float time,int tick,int revision,bool success)
        {end=time;endTick=tick;endRevision=revision;completed=success;finished=true;simulationStayedPaused&=tick==startTick;mapStayedUnchanged&=revision==startRevision;}
        static BenchmarkMetric Metric(Totals total,double[] values)
        {
            Array.Sort(values);
            return new BenchmarkMetric{count=total.Count,totalMs=total.Sum,meanMs=total.Count>0?total.Sum/total.Count:0,maxMs=total.Max,
                p50Ms=Percentile(values,.5),p95Ms=Percentile(values,.95),p99Ms=Percentile(values,.99)};
        }
        static double Percentile(double[] values,double p)
        {return values.Length==0?0:values[(int)Math.Ceiling(p*values.Length)-1];}
        double[] Values(Func<PerformanceSpike,double> value)
        {var result=new double[samples.Count];for(int i=0;i<result.Length;i++)result[i]=value(samples[i]);return result;}
        public FixedBenchmarkReport Snapshot()
        {
            var frameValues=new double[Math.Max(0,samples.Count-1)];
            for(int i=0;i<frameValues.Length;i++)frameValues[i]=Math.Max(0,(samples[i+1].time-samples[i].time)*1000.0);
            return new FixedBenchmarkReport{cameraMotionTest=moving,mode=mode,pairId=pairId,phase=phase,completed=completed,simulationStayedPaused=simulationStayedPaused,mapStayedUnchanged=mapStayedUnchanged,
                startRealtime=start,endRealtime=end,elapsed=Math.Max(0,end-start),startTick=startTick,endTick=endTick,startMapRevision=startRevision,endMapRevision=endRevision,
                renderCount=count,storedOperations=samples.Count,droppedOperations=count-samples.Count,cameraChanges=cameraChanges,
                rendersPerSecond=end>start?count/(double)(end-start):0,
                render=Metric(render,Values(x=>x.ms)),camera=Metric(camera,Values(x=>x.cameraMs)),compose=Metric(compose,Values(x=>x.composeMs)),
                scene=Metric(scene,Values(x=>x.sceneMs)),mask=Metric(mask,Values(x=>x.maskMs)),frameIntervals=Metric(intervals,frameValues),operations=samples.ToArray()};
        }
    }
}
