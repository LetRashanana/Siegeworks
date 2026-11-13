using Unity.Mathematics;

namespace Siegeworks.Bench
{
    // One rule set shared by both benchmark modes, so they do identical work per unit.
    // Units live on the XZ plane as float2(x, z). Team 0 marches +Z, team 1 marches -Z.
    public static class SimSettings
    {
        public const float Speed = 5f;
        public const float AttackRange = 1.5f;
        public const float AttackCooldown = 1f;
        public const float Damage = 10f;
        // Nobody dies, so the unit count stays constant while measuring. Keep it at 1e6: at 1e9 a
        // float32 step is 64, so "hp -= 10" rounds to no change and hits become invisible.
        public const float StartHp = 1e6f;
        public const float CellSize = 8f;
        public const int SearchRing = 2;            // 5x5 cells = 40 m search square
        public const float SearchMin = 0.8f;        // staggered re-search interval, seconds
        public const float SearchMax = 1.2f;
        public const float SeparationRadius = 1f;
        public const int MaxSeparationChecks = 8;
        public const float Spacing = 1.2f;
        public const float Gap = 20f;
        public const float UnitY = 1f;              // capsule centre height

        public static int CellKey(int cx, int cz) => (cx + 4096) * 8192 + (cz + 4096);

        public static int2 Cell(float2 p) => (int2)math.floor(p / CellSize);

        public static byte TeamOf(int i, int count) => (byte)(i < count / 2 ? 0 : 1);

        public static float2 Forward(byte team) => team == 0 ? new float2(0f, 1f) : new float2(0f, -1f);

        // Each team spawns as a square block facing the other across the gap.
        public static float2 SpawnPosition(int i, int count)
        {
            int half = count / 2;
            byte team = TeamOf(i, count);
            int j = team == 0 ? i : i - half;
            int teamCount = team == 0 ? half : count - half;
            int side = (int)math.ceil(math.sqrt(teamCount));
            int row = j / side, col = j % side;
            float x = (col - side * 0.5f) * Spacing;
            float z = Gap * 0.5f + row * Spacing;
            return new float2(x, team == 0 ? -z : z);
        }

        public static float NextSearchDelay(int i, int frame)
        {
            var rng = Random.CreateFromIndex((uint)(i * 7919 + frame * 104729 + 1));
            return rng.NextFloat(SearchMin, SearchMax);
        }
    }
}
