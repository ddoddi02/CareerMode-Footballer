using UnityEngine;

namespace Prototype
{
    /// <summary>The five vertical channels every modern side talks in.</summary>
    public enum Lane
    {
        LeftWing,
        LeftHalf,     // 하프스페이스
        Centre,
        RightHalf,
        RightWing
    }

    /// <summary>
    /// Where on the pitch a point is, in the terms defending decisions are actually made
    /// in: which channel, how close to goal, and how much of the goal it can see.
    ///
    /// This is the first slice of the tactical grid decided in PROJECT.md §3.12 - lanes
    /// only, no bands yet. It is arithmetic on a position, not colliders, so a defender
    /// can ask about a zone he is nowhere near.
    /// </summary>
    public static class TacticalPitch
    {
        public const float HalfW = 34f;
        public const float HalfL = 52.5f;
        public const float PenDepth = 16.5f;
        public const float PenHalfW = 20.16f;
        public const float GoalHalfW = 3.66f;
        public const float ArcRadius = 9.15f;
        public const float PenSpot = 11f;

        /// <summary>Lane boundaries, from the penalty-area width and the goal-area width.</summary>
        public static Lane LaneOf(float x)
        {
            if (x < -PenHalfW) return Lane.LeftWing;
            if (x < -9.16f) return Lane.LeftHalf;
            if (x <= 9.16f) return Lane.Centre;
            if (x <= PenHalfW) return Lane.RightHalf;
            return Lane.RightWing;
        }

        /// <summary>Centre and both half-spaces. The channels a shot or a turn comes from.</summary>
        public static bool IsCentral(Lane l)
        {
            return l == Lane.Centre || l == Lane.LeftHalf || l == Lane.RightHalf;
        }

        /// <summary>The goal a side defending toward this end is protecting.</summary>
        public static Vector3 GoalCentre(bool defendsPositiveZ)
        {
            return new Vector3(0f, 0f, defendsPositiveZ ? HalfL : -HalfL);
        }

        public static bool InPenaltyBox(Vector3 p, bool defendsPositiveZ)
        {
            if (Mathf.Abs(p.x) > PenHalfW) return false;
            return defendsPositiveZ
                ? p.z > HalfL - PenDepth
                : p.z < -HalfL + PenDepth;
        }

        /// <summary>Inside the D, or just outside it - the range a first-time shot comes from.</summary>
        public static bool NearPenaltyArc(Vector3 p, bool defendsPositiveZ)
        {
            Vector3 spot = GoalCentre(defendsPositiveZ);
            spot.z += defendsPositiveZ ? -PenSpot : PenSpot;
            Vector2 d = new Vector2(p.x - spot.x, p.z - spot.z);
            return d.magnitude < ArcRadius + 3f;
        }

        /// <summary>
        /// How dangerous this position is to concede from, 0..1.
        ///
        /// Two multiplied terms, because either one alone lies. Distance alone calls a
        /// wide position by the byline dangerous; angle alone calls the halfway line
        /// dangerous. A shot needs both - to be close AND to be able to see the goal.
        /// The zone bonuses on top are what principle 3 names explicitly.
        /// </summary>
        public static float Danger(Vector3 p, bool defendsPositiveZ)
        {
            Vector3 goal = GoalCentre(defendsPositiveZ);
            Vector2 toGoal = new Vector2(goal.x - p.x, goal.z - p.z);
            float dist = toGoal.magnitude;

            // Nothing is a shot from 40 m; everything is from 6.
            float distTerm = 1f - Mathf.InverseLerp(6f, 40f, dist);

            // How square he is to the goal. Straight on = 1, along the byline = 0.
            float lateral = Mathf.Abs(p.x);
            float angTerm = 1f - Mathf.InverseLerp(GoalHalfW, PenHalfW + 8f, lateral);

            float d = distTerm * Mathf.Max(angTerm, 0.15f);

            Lane lane = LaneOf(p.x);
            if (lane == Lane.LeftHalf || lane == Lane.RightHalf) d += 0.12f;
            if (NearPenaltyArc(p, defendsPositiveZ)) d += 0.15f;
            if (InPenaltyBox(p, defendsPositiveZ)) d += 0.25f;

            return Mathf.Clamp01(d);
        }

        /// <summary>
        /// Unit vector from a point toward the touchline it is nearest to. Principle 4
        /// works by making this the only comfortable direction left.
        /// </summary>
        public static Vector3 OutsideDir(Vector3 p)
        {
            return new Vector3(p.x >= 0f ? 1f : -1f, 0f, 0f);
        }

        /// <summary>Toward the middle of the pitch - the side a presser must shut off.</summary>
        public static Vector3 InsideDir(Vector3 p)
        {
            return -OutsideDir(p);
        }
    }
}
