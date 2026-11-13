using System;
using System.Globalization;

namespace Siegeworks.Bench
{
    // Command-line options for the size test:
    //   -mode go|dod  -count N  -seconds S  -warmup W  -out path
    // Anything not passed keeps the BenchRunner inspector default.
    public struct BenchArgs
    {
        public string Mode;
        public int Count;
        public float Seconds;
        public float Warmup;
        public string OutPath;
        public bool FromCommandLine;

        public static BenchArgs Parse(string mode, int count, float seconds, float warmup)
        {
            var a = new BenchArgs { Mode = mode, Count = count, Seconds = seconds, Warmup = warmup };
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                string v = args[i + 1];
                switch (args[i])
                {
                    case "-mode": a.Mode = v; a.FromCommandLine = true; break;
                    case "-count": a.Count = int.Parse(v, CultureInfo.InvariantCulture); break;
                    case "-seconds": a.Seconds = float.Parse(v, CultureInfo.InvariantCulture); break;
                    case "-warmup": a.Warmup = float.Parse(v, CultureInfo.InvariantCulture); break;
                    case "-out": a.OutPath = v; break;
                }
            }
            return a;
        }
    }
}
