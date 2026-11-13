using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Siegeworks.Bench
{
    [Serializable]
    public class Stat
    {
        public float mean, p50, p95, max;

        public static Stat Of(List<float> v)
        {
            var s = new Stat();
            if (v.Count == 0) return s;
            var sorted = new List<float>(v);
            sorted.Sort();
            float sum = 0f;
            foreach (float x in sorted) sum += x;
            s.mean = sum / sorted.Count;
            s.p50 = sorted[(int)(sorted.Count * 0.50f)];
            s.p95 = sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.95f))];
            s.max = sorted[sorted.Count - 1];
            return s;
        }
    }

    [Serializable]
    public class BenchResult
    {
        public string mode;
        public int count;
        public float seconds;
        public int frames;
        public float fps;
        public bool editor;
        public bool burstActive;
        public bool frameTimingEnabled;
        public float engagedFraction;
        public long hits;
        public string screenshot;
        public string unity, gpu, gfxApi, cpu, resolution;
        public Stat frameMs, simMs, cpuMainMs, cpuRenderMs, gpuMs;
    }

    // Proves Burst is really compiling jobs in this player. [BurstDiscard] code is stripped from
    // Burst-compiled jobs, so the result is 1 under Burst and 0 if jobs fell back to managed code.
    [BurstCompile]
    public struct BurstProbeJob : IJob
    {
        public NativeArray<int> Out;

        public void Execute()
        {
            Out[0] = 1;
            MarkManaged(ref Out);
        }

        [BurstDiscard]
        static void MarkManaged(ref NativeArray<int> o) => o[0] = 0;

        public static bool Run()
        {
            var a = new NativeArray<int>(1, Allocator.TempJob);
            new BurstProbeJob { Out = a }.Schedule().Complete();
            bool burst = a[0] == 1;
            a.Dispose();
            return burst;
        }
    }
}
