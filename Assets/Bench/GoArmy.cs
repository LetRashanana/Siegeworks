using System.Collections.Generic;
using System.Diagnostics;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Siegeworks.Bench
{
    // Plain-GameObject mode: one GameObject + MeshRenderer + GoUnit (MonoBehaviour.Update) per unit.
    // This manager only snapshots positions and rebuilds the shared grid at the start of each frame,
    // so units never do an O(N^2) search. Runs before every GoUnit.
    [DefaultExecutionOrder(-100)]
    public class GoArmy : MonoBehaviour, IBenchArmy
    {
        public GoUnit[] Units;
        public float2[] Pos;
        public byte[] Team;
        public int Frame;

        readonly Dictionary<int, List<int>> _grid = new Dictionary<int, List<int>>();
        readonly Stack<List<int>> _spareLists = new Stack<List<int>>();
        long _frameStart;

        public double SimMs { get; private set; }

        public void Init(int count, Mesh mesh, Material teamA, Material teamB)
        {
            Units = new GoUnit[count];
            Pos = new float2[count];
            Team = new byte[count];
            for (int i = 0; i < count; i++)
            {
                byte team = SimSettings.TeamOf(i, count);
                var go = new GameObject("unit");
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = team == 0 ? teamA : teamB;
                mr.shadowCastingMode = ShadowCastingMode.On;
                var u = go.AddComponent<GoUnit>();
                Team[i] = team;
                Pos[i] = SimSettings.SpawnPosition(i, count);
                u.Setup(this, i, Pos[i], SimSettings.NextSearchDelay(i, 0) - SimSettings.SearchMin);
                Units[i] = u;
            }
        }

        void Update()
        {
            if (Units == null) return;
            _frameStart = Stopwatch.GetTimestamp();
            Frame++;

            foreach (var list in _grid.Values) { list.Clear(); _spareLists.Push(list); }
            _grid.Clear();
            for (int i = 0; i < Units.Length; i++)
            {
                float2 p = Units[i].Pos;
                Pos[i] = p;
                int2 c = SimSettings.Cell(p);
                int key = SimSettings.CellKey(c.x, c.y);
                if (!_grid.TryGetValue(key, out var list))
                {
                    list = _spareLists.Count > 0 ? _spareLists.Pop() : new List<int>(64);
                    _grid.Add(key, list);
                }
                list.Add(i);
            }
        }

        // Runs after every GoUnit.Update, so this spans the grid rebuild plus all unit updates.
        void LateUpdate()
        {
            if (Units == null) return;
            SimMs = (Stopwatch.GetTimestamp() - _frameStart) * 1000.0 / Stopwatch.Frequency;
        }

        public float EngagedFraction()
        {
            int n = 0;
            foreach (var u in Units) if (u.HasTarget) n++;
            return Units.Length > 0 ? (float)n / Units.Length : 0f;
        }

        public long Hits()
        {
            double lost = 0;
            foreach (var u in Units) lost += SimSettings.StartHp - u.Hp;
            return (long)System.Math.Round(lost / SimSettings.Damage);
        }

        public int FindNearestEnemy(float2 p, byte team)
        {
            int2 c = SimSettings.Cell(p);
            int best = -1;
            float bestSq = float.MaxValue;
            for (int dz = -SimSettings.SearchRing; dz <= SimSettings.SearchRing; dz++)
            for (int dx = -SimSettings.SearchRing; dx <= SimSettings.SearchRing; dx++)
            {
                if (!_grid.TryGetValue(SimSettings.CellKey(c.x + dx, c.y + dz), out var list)) continue;
                for (int k = 0; k < list.Count; k++)
                {
                    int j = list[k];
                    if (Team[j] == team) continue;
                    float sq = math.distancesq(Pos[j], p);
                    if (sq < bestSq) { bestSq = sq; best = j; }
                }
            }
            return best;
        }

        public float2 Separation(int i, float2 p)
        {
            int2 c = SimSettings.Cell(p);
            float2 push = float2.zero;
            if (!_grid.TryGetValue(SimSettings.CellKey(c.x, c.y), out var list)) return push;
            int checks = 0;
            for (int k = 0; k < list.Count; k++)
            {
                int j = list[k];
                if (j == i) continue;
                float2 away = p - Pos[j];
                float d = math.length(away);
                if (d < SimSettings.SeparationRadius && d > 1e-4f)
                    push += away / d * (SimSettings.SeparationRadius - d) * 0.5f;
                if (++checks >= SimSettings.MaxSeparationChecks) break;
            }
            return push;
        }
    }
}
