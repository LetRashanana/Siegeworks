using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Siegeworks.Bench
{
    // Data-oriented mode: units are rows in NativeArrays, simulated by Burst jobs,
    // drawn with Graphics.RenderMeshInstanced. No GameObject per unit, no ECS.
    public class DodArmy : MonoBehaviour, IBenchArmy
    {
        const int DrawBatch = 1023;

        int _count, _half, _frame;
        Mesh _mesh;
        RenderParams _rpA, _rpB;

        NativeArray<float2> _pos, _posNext, _facing;
        NativeArray<byte> _team;
        NativeArray<int> _target;
        NativeArray<float> _hp, _cooldown, _searchTimer;
        NativeArray<float4x4> _matrices;
        NativeParallelMultiHashMap<int, int> _grid;
        NativeQueue<int> _hits;

        public double SimMs { get; private set; }

        public void Init(int count, Mesh mesh, Material teamA, Material teamB)
        {
            _count = count;
            _half = count / 2;
            _mesh = mesh;
            var bounds = new Bounds(Vector3.zero, Vector3.one * 4000f);
            _rpA = new RenderParams(teamA) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true, worldBounds = bounds };
            _rpB = new RenderParams(teamB) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true, worldBounds = bounds };

            _pos = new NativeArray<float2>(count, Allocator.Persistent);
            _posNext = new NativeArray<float2>(count, Allocator.Persistent);
            _facing = new NativeArray<float2>(count, Allocator.Persistent);
            _team = new NativeArray<byte>(count, Allocator.Persistent);
            _target = new NativeArray<int>(count, Allocator.Persistent);
            _hp = new NativeArray<float>(count, Allocator.Persistent);
            _cooldown = new NativeArray<float>(count, Allocator.Persistent);
            _searchTimer = new NativeArray<float>(count, Allocator.Persistent);
            _matrices = new NativeArray<float4x4>(count, Allocator.Persistent);
            _grid = new NativeParallelMultiHashMap<int, int>(count, Allocator.Persistent);
            _hits = new NativeQueue<int>(Allocator.Persistent);

            for (int i = 0; i < count; i++)
            {
                byte team = SimSettings.TeamOf(i, count);
                _pos[i] = SimSettings.SpawnPosition(i, count);
                _facing[i] = SimSettings.Forward(team);
                _team[i] = team;
                _target[i] = -1;
                _hp[i] = SimSettings.StartHp;
                _searchTimer[i] = SimSettings.NextSearchDelay(i, 0) - SimSettings.SearchMin; // stagger the first search
            }
        }

        void Update()
        {
            if (_count == 0) return;
            var sw = Stopwatch.StartNew();

            _grid.Clear();
            JobHandle h = new BuildGridJob { Pos = _pos, Grid = _grid.AsParallelWriter() }.Schedule(_count, 256);
            h = new SimJob
            {
                Pos = _pos, Team = _team, Grid = _grid,
                PosNext = _posNext, Facing = _facing, Target = _target, Cooldown = _cooldown,
                SearchTimer = _searchTimer, Hits = _hits.AsParallelWriter(),
                Dt = Time.deltaTime, Frame = ++_frame,
            }.Schedule(_count, 128, h);
            h = new ApplyHitsJob { Hits = _hits, Hp = _hp }.Schedule(h);
            h = new MatrixJob { Pos = _posNext, Facing = _facing, Matrices = _matrices }.Schedule(_count, 256, h);
            h.Complete();
            (_pos, _posNext) = (_posNext, _pos);

            SimMs = sw.Elapsed.TotalMilliseconds;

            var m = _matrices.Reinterpret<Matrix4x4>();
            Draw(_rpA, m, 0, _half);
            Draw(_rpB, m, _half, _count);
        }

        public float EngagedFraction()
        {
            int n = 0;
            for (int i = 0; i < _count; i++) if (_target[i] >= 0) n++;
            return _count > 0 ? (float)n / _count : 0f;
        }

        public long Hits()
        {
            double lost = 0;
            for (int i = 0; i < _count; i++) lost += SimSettings.StartHp - _hp[i];
            return (long)System.Math.Round(lost / SimSettings.Damage);
        }

        void Draw(RenderParams rp, NativeArray<Matrix4x4> m, int from, int to)
        {
            for (int start = from; start < to; start += DrawBatch)
                Graphics.RenderMeshInstanced(rp, _mesh, 0, m, math.min(DrawBatch, to - start), start);
        }

        void OnDestroy()
        {
            if (!_pos.IsCreated) return;
            _pos.Dispose(); _posNext.Dispose(); _facing.Dispose(); _team.Dispose(); _target.Dispose();
            _hp.Dispose(); _cooldown.Dispose(); _searchTimer.Dispose(); _matrices.Dispose();
            _grid.Dispose(); _hits.Dispose();
        }
    }
}
