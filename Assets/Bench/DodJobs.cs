using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Siegeworks.Bench
{
    // Burst jobs for the data-oriented mode: no GameObject per unit, arrays + jobs + instanced draws.

    [BurstCompile]
    public struct BuildGridJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float2> Pos;
        public NativeParallelMultiHashMap<int, int>.ParallelWriter Grid;

        public void Execute(int i)
        {
            int2 c = SimSettings.Cell(Pos[i]);
            Grid.Add(SimSettings.CellKey(c.x, c.y), i);
        }
    }

    [BurstCompile]
    public struct SimJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float2> Pos;
        [ReadOnly] public NativeArray<byte> Team;
        [ReadOnly] public NativeParallelMultiHashMap<int, int> Grid;

        public NativeArray<float2> PosNext;
        public NativeArray<float2> Facing;
        public NativeArray<int> Target;
        public NativeArray<float> Cooldown;
        public NativeArray<float> SearchTimer;
        public NativeQueue<int>.ParallelWriter Hits;

        public float Dt;
        public int Frame;

        public void Execute(int i)
        {
            float2 p = Pos[i];
            byte team = Team[i];
            int target = Target[i];

            float timer = SearchTimer[i] - Dt;
            if (timer <= 0f)
            {
                target = FindNearestEnemy(p, team);
                timer = SimSettings.NextSearchDelay(i, Frame);
            }

            float2 move = SimSettings.Forward(team);
            float2 facing = move;
            float cooldown = math.max(0f, Cooldown[i] - Dt);
            if (target >= 0)
            {
                float2 to = Pos[target] - p;
                float d = math.length(to);
                facing = d > 1e-4f ? to / d : facing;
                if (d > SimSettings.AttackRange)
                {
                    move = facing;
                }
                else
                {
                    move = float2.zero;
                    if (cooldown <= 0f)
                    {
                        Hits.Enqueue(target);
                        cooldown = SimSettings.AttackCooldown;
                    }
                }
            }

            PosNext[i] = p + move * (SimSettings.Speed * Dt) + Separation(i, p);
            Facing[i] = facing;
            Target[i] = target;
            Cooldown[i] = cooldown;
            SearchTimer[i] = timer;
        }

        int FindNearestEnemy(float2 p, byte team)
        {
            int2 c = SimSettings.Cell(p);
            int best = -1;
            float bestSq = float.MaxValue;
            for (int dz = -SimSettings.SearchRing; dz <= SimSettings.SearchRing; dz++)
            for (int dx = -SimSettings.SearchRing; dx <= SimSettings.SearchRing; dx++)
            {
                int key = SimSettings.CellKey(c.x + dx, c.y + dz);
                if (!Grid.TryGetFirstValue(key, out int j, out var it)) continue;
                do
                {
                    if (Team[j] == team) continue;
                    float sq = math.distancesq(Pos[j], p);
                    if (sq < bestSq) { bestSq = sq; best = j; }
                } while (Grid.TryGetNextValue(out j, ref it));
            }
            return best;
        }

        float2 Separation(int i, float2 p)
        {
            int2 c = SimSettings.Cell(p);
            float2 push = float2.zero;
            int checks = 0;
            if (!Grid.TryGetFirstValue(SimSettings.CellKey(c.x, c.y), out int j, out var it)) return push;
            do
            {
                if (j == i) continue;
                float2 away = p - Pos[j];
                float d = math.length(away);
                if (d < SimSettings.SeparationRadius && d > 1e-4f)
                    push += away / d * (SimSettings.SeparationRadius - d) * 0.5f;
                if (++checks >= SimSettings.MaxSeparationChecks) break;
            } while (Grid.TryGetNextValue(out j, ref it));
            return push;
        }
    }

    [BurstCompile]
    public struct ApplyHitsJob : IJob
    {
        public NativeQueue<int> Hits;
        public NativeArray<float> Hp;

        public void Execute()
        {
            while (Hits.TryDequeue(out int t)) Hp[t] -= SimSettings.Damage;
        }
    }

    [BurstCompile]
    public struct MatrixJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float2> Pos;
        [ReadOnly] public NativeArray<float2> Facing;
        public NativeArray<float4x4> Matrices;

        public void Execute(int i)
        {
            float2 p = Pos[i], f = Facing[i];
            quaternion rot = quaternion.LookRotationSafe(new float3(f.x, 0f, f.y), math.up());
            Matrices[i] = float4x4.TRS(new float3(p.x, SimSettings.UnitY, p.y), rot, new float3(1f));
        }
    }
}
