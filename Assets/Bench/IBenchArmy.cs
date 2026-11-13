using UnityEngine;

namespace Siegeworks.Bench
{
    public interface IBenchArmy
    {
        // Simulation cost of the last frame, in milliseconds (excludes rendering).
        double SimMs { get; }
        void Init(int count, Mesh mesh, Material teamA, Material teamB);
        // Proof the sim is doing the work: share of units holding a target, and hits landed so far.
        float EngagedFraction();
        long Hits();
    }
}
