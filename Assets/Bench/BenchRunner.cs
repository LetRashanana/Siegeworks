using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Siegeworks.Bench
{
    // Phase 1 size test driver. Spawns one army mode, waits out a warmup, records frame timings
    // for N seconds with vsync off, writes a JSON result and quits (or leaves Play mode).
    [DefaultExecutionOrder(1000)]
    public class BenchRunner : MonoBehaviour
    {
        [SerializeField] string mode = "dod";
        [SerializeField] int count = 2000;
        [SerializeField] float seconds = 5f;
        [SerializeField] float warmup = 3f;
        [SerializeField] Mesh unitMesh;
        [SerializeField] Material teamA;
        [SerializeField] Material teamB;
        [SerializeField] Camera cam;

        BenchArgs _args;
        IBenchArmy _army;
        float _phaseStart = -1f;
        bool _measuring, _done, _shot;
        string _shotPath;
        readonly FrameTiming[] _ft = new FrameTiming[1];
        readonly List<float> _frame = new List<float>(), _sim = new List<float>(),
            _cpuMain = new List<float>(), _cpuRender = new List<float>(), _gpu = new List<float>();

        void Awake()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
        }

        void Start()
        {
            _args = BenchArgs.Parse(mode, count, seconds, warmup);

            float extent = Mathf.Sqrt(_args.Count * 0.5f) * SimSettings.Spacing + SimSettings.Gap;
            cam.transform.position = new Vector3(0f, extent * 1.1f, -extent * 1.3f);
            cam.transform.LookAt(Vector3.zero);
            cam.farClipPlane = extent * 6f;

            var go = new GameObject(_args.Mode + "Army");
            _army = _args.Mode == "go" ? go.AddComponent<GoArmy>() : (IBenchArmy)go.AddComponent<DodArmy>();
            _army.Init(_args.Count, unitMesh, teamA, teamB);
            Debug.Log($"[BENCH] start mode={_args.Mode} count={_args.Count}");
        }

        void LateUpdate()
        {
            if (_done) return;
            float now = Time.realtimeSinceStartup;
            if (_phaseStart < 0f) { _phaseStart = now; return; } // the spawn frame doesn't count toward warmup

            if (!_measuring)
            {
                if (now - _phaseStart >= _args.Warmup) { _measuring = true; _phaseStart = now; }
                return;
            }

            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _ft) > 0)
            {
                _cpuMain.Add((float)_ft[0].cpuMainThreadFrameTime);
                _cpuRender.Add((float)_ft[0].cpuRenderThreadFrameTime);
                _gpu.Add((float)_ft[0].gpuFrameTime);
            }
            _frame.Add(Time.unscaledDeltaTime * 1000f);
            _sim.Add((float)_army.SimMs);

            if (!_shot && now - _phaseStart >= _args.Seconds * 0.5f)
            {
                _shot = true;
                _shotPath = Path.ChangeExtension(ResultPath(), ".png");
                ScreenCapture.CaptureScreenshot(_shotPath);
            }

            if (now - _phaseStart >= _args.Seconds) Finish(now - _phaseStart);
        }

        void Finish(float elapsed)
        {
            _done = true;
            var r = new BenchResult
            {
                mode = _args.Mode,
                count = _args.Count,
                seconds = elapsed,
                frames = _frame.Count,
                fps = _frame.Count / elapsed,
                editor = Application.isEditor,
                burstActive = BurstProbeJob.Run(),
                frameTimingEnabled = FrameTimingManager.IsFeatureEnabled(),
                engagedFraction = _army.EngagedFraction(),
                hits = _army.Hits(),
                screenshot = _shotPath,
                unity = Application.unityVersion,
                gpu = SystemInfo.graphicsDeviceName,
                gfxApi = SystemInfo.graphicsDeviceType.ToString(),
                cpu = $"{SystemInfo.processorType} x{SystemInfo.processorCount}",
                resolution = $"{Screen.width}x{Screen.height}",
                frameMs = Stat.Of(_frame),
                simMs = Stat.Of(_sim),
                cpuMainMs = Stat.Of(_cpuMain),
                cpuRenderMs = Stat.Of(_cpuRender),
                gpuMs = Stat.Of(_gpu),
            };

            string path = ResultPath();
            File.WriteAllText(path, JsonUtility.ToJson(r, true));
            Debug.Log($"[BENCH] done mode={r.mode} count={r.count} fps={r.fps:F1} frame={r.frameMs.mean:F2}ms " +
                      $"sim={r.simMs.mean:F2}ms gpu={r.gpuMs.mean:F2}ms burst={r.burstActive} " +
                      $"engaged={r.engagedFraction:P0} hits={r.hits} -> {path}");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        string ResultPath() => string.IsNullOrEmpty(_args.OutPath)
            ? Path.Combine(Application.persistentDataPath, $"bench_{_args.Mode}_{_args.Count}.json")
            : _args.OutPath;
    }
}
