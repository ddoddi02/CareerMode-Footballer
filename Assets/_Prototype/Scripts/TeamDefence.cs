using System.Collections.Generic;
using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// The defending side's shared picture and the standing orders that come out of it.
    /// One of these per team; the individual defenders only execute what it hands them.
    ///
    /// It recomputes only when <see cref="PitchIntel"/> takes a new picture - about
    /// once a second - so the block moves in steps, the way a real line does when
    /// everyone glances up, shuffles across, and settles again.
    ///
    /// The five principles, in the order they are applied. Order matters: each one is
    /// allowed to override the one before it, so a man who must go and mark a striker in
    /// the box (3) gives up his slot in the shape (1), and someone else is then obliged
    /// to fill the hole he left (5).
    ///
    ///   1  hold the slot the formation gives you, slid toward the ball
    ///   2  stay within touching distance of your neighbours - squeeze the space
    ///   3  nobody dangerous is left unmarked
    ///   4  on the break, show the carrier the touchline
    ///   5  cover the zone a team-mate vacated to do 3
    /// </summary>
    public class TeamDefence : MonoBehaviour
    {
        [Header("Refs")]
        public Transform ball;
        public Transform[] opponents;
        public DefenderAI[] members;

        [Tooltip("True if this side defends the goal at +Z.")]
        public bool defendsPositiveZ = true;

        [Header("What it knows, and when")]
        public PitchIntel intel = new PitchIntel();

        [Header("1 - shape")]
        [Tooltip("How much of the ball's lateral offset the whole block slides across.")]
        [Range(0f, 1f)] public float lateralFollow = 0.55f;
        [Tooltip("How much the block drops as the ball advances on it.")]
        [Range(0f, 1f)] public float depthFollow = 0.60f;
        public float maxLateralShift = 13f;
        public float maxDepthShift = 22f;

        [Header("2 - compactness")]
        [Tooltip("Biggest gap allowed between two players standing next to each other in the same line. This is the number: a ball played into the gap has to be pressable before it is controlled.")]
        public float maxNeighbourGap = 12f;
        [Tooltip("Biggest gap allowed between one line and the next - back four to midfield, midfield to the front. Keeps the block one unit rather than three.")]
        public float maxLineGap = 14f;
        [Tooltip("How many relaxation passes per picture. A line is a chain, so fixing one pair re-opens its neighbour - it takes iterations to settle. Four passes leaves a 12 m target sitting at 14.5 m; sixteen lands on it.")]
        [Range(1, 24)] public int compactPasses = 16;
        [Tooltip("How much of each pass's correction is applied. Damping only - the relaxation is monotone, so this never overshoots.")]
        [Range(0.1f, 1f)] public float squeeze = 0.8f;

        [Header("3 - danger")]
        [Tooltip("Danger score above which an opponent must not be left alone.")]
        [Range(0f, 1f)] public float dangerThreshold = 0.34f;
        [Tooltip("How far goal-side of his man a marker stands.")]
        public float markGap = 1.7f;
        [Tooltip("A marker will not travel further than this to pick a man up.")]
        public float maxMarkTravel = 26f;

        [Header("4 - show him wide")]
        [Tooltip("Ball closing on our goal faster than this counts as a break.")]
        public float breakSpeed = 5.0f;

        [Header("5 - cover")]
        [Range(0f, 1f)] public float coverBlend = 0.55f;

        /// <summary>The man currently closing the ball down. Everyone else holds shape.</summary>
        public DefenderAI Presser { get; private set; }

        /// <summary>Opponent in possession, as far as the defence knows.</summary>
        public Vector3 CarrierPos { get; private set; }
        public bool OnTheBreak { get; private set; }

        readonly List<int> threats = new List<int>();
        readonly List<int> row = new List<int>();
        Vector3[] slotNow = new Vector3[0];
        bool[] pulled = new bool[0];
        Vector3 prevBall;
        float prevBallAt;

        void Update()
        {
            if (ball == null || members == null || members.Length == 0) return;

            if (intel.Tick(Time.time, opponents, ball.position, BlockCentre()))
                Recompute();
        }

        // ---------------------------------------------------------------- shape --

        void Recompute()
        {
            int n = members.Length;
            if (slotNow.Length != n) { slotNow = new Vector3[n]; pulled = new bool[n]; }

            Vector3 ballPos = intel.Ball;
            MeasureBreak(ballPos);
            CarrierPos = NearestOpponentTo(ballPos, ballPos);

            // 1 - the formation slot, slid toward the ball.
            Vector3 shift = ShapeShift(ballPos);
            for (int i = 0; i < n; i++)
            {
                pulled[i] = false;
                if (members[i] != null) members[i].covering = false;
                slotNow[i] = members[i] == null ? Vector3.zero : members[i].homeSlot + shift;
            }

            // 2 - squeeze anyone who has drifted away from his neighbours.
            Compact(n);

            // 3 - pick up anyone dangerous.
            AssignMarks(n, ballPos);

            // 5 - fill the holes that 3 just made.
            Cover(n, shift);

            // 4 - and decide who closes the ball, and which way he shows him.
            AssignPresser(n, ballPos);

            for (int i = 0; i < n; i++)
                if (members[i] != null) members[i].SetStation(slotNow[i]);
        }

        /// <summary>How far the whole block slides to follow the ball.</summary>
        Vector3 ShapeShift(Vector3 ballPos)
        {
            float side = defendsPositiveZ ? 1f : -1f;
            float x = Mathf.Clamp(ballPos.x * lateralFollow, -maxLateralShift, maxLateralShift);
            // Positive = toward our own goal. The block drops as the ball comes on to it.
            float z = Mathf.Clamp(ballPos.z * side * depthFollow, -maxDepthShift, maxDepthShift);
            return new Vector3(x, 0f, z * side);
        }

        /// <summary>
        /// Principle 2 - the one a defensive coach actually cares about. Keep the players
        /// close enough together that a ball played into the space between two of them can
        /// be pressed before it is controlled.
        ///
        /// Spacing is measured ALONG A LINE, not against whoever happens to be nearest.
        /// The first version compared each man to his closest team-mate of any kind, and
        /// the back four never squeezed at all: a centre-back's nearest body is the pivot
        /// standing 9 m in front of him, so the 12 m rule read as satisfied while the two
        /// centre-backs sat 15 m apart with a hole between them.
        ///
        /// Relaxed over several passes rather than corrected in one go. A single pass only
        /// removes part of the excess, so the block settles wherever that partial step
        /// lands instead of at the number you asked for.
        /// </summary>
        void Compact(int n)
        {
            for (int pass = 0; pass < compactPasses; pass++)
            {
                SqueezeAlongLines(n);
                SqueezeBetweenLines(n);
            }
        }

        /// <summary>Neighbours in the same line, ordered across the pitch.</summary>
        void SqueezeAlongLines(int n)
        {
            for (int line = 0; line < Formation.LineCount; line++)
            {
                row.Clear();
                for (int i = 0; i < n; i++)
                    if (members[i] != null && Formation.LineOf(members[i].role) == line) row.Add(i);
                if (row.Count < 2) continue;

                row.Sort(delegate (int a, int b) { return slotNow[a].x.CompareTo(slotNow[b].x); });

                for (int k = 0; k + 1 < row.Count; k++)
                {
                    int i = row[k], j = row[k + 1];
                    Vector3 d = Flat(slotNow[j] - slotNow[i]);
                    float gap = d.magnitude;
                    if (gap <= maxNeighbourGap || gap < 1e-3f) continue;

                    Vector3 corr = d / gap * (gap - maxNeighbourGap) * 0.5f * squeeze;
                    slotNow[i] += corr;
                    slotNow[j] -= corr;
                }
            }
        }

        /// <summary>And the lines themselves, so the block does not stretch front to back.</summary>
        void SqueezeBetweenLines(int n)
        {
            for (int line = 0; line + 1 < Formation.LineCount; line++)
            {
                float za, zb;
                int ca = LineDepth(n, line, out za);
                int cb = LineDepth(n, line + 1, out zb);
                if (ca == 0 || cb == 0) continue;

                float gap = Mathf.Abs(za - zb);
                if (gap <= maxLineGap) continue;

                float pull = (gap - maxLineGap) * 0.5f * squeeze * Mathf.Sign(za - zb);
                for (int i = 0; i < n; i++)
                {
                    if (members[i] == null) continue;
                    int L = Formation.LineOf(members[i].role);
                    if (L == line) slotNow[i].z -= pull;
                    else if (L == line + 1) slotNow[i].z += pull;
                }
            }
        }

        int LineDepth(int n, int line, out float z)
        {
            float sum = 0f;
            int c = 0;
            for (int i = 0; i < n; i++)
            {
                if (members[i] == null || Formation.LineOf(members[i].role) != line) continue;
                sum += slotNow[i].z;
                c++;
            }
            z = c > 0 ? sum / c : 0f;
            return c;
        }

        // --------------------------------------------------------------- marking --

        /// <summary>
        /// Principle 3. Rank the opposition by how much damage they could do from where
        /// they are, then give each one the defender who can get there quickest - but
        /// only if he is close enough that leaving his slot is worth it.
        /// </summary>
        void AssignMarks(int n, Vector3 ballPos)
        {
            threats.Clear();
            if (opponents == null) return;

            for (int k = 0; k < opponents.Length; k++)
            {
                if (opponents[k] == null) continue;
                Vector3 p = intel.Projected(k, Time.time);
                if (TacticalPitch.Danger(p, defendsPositiveZ) >= dangerThreshold)
                    threats.Add(k);
            }

            threats.Sort(delegate (int a, int b)
            {
                float da = TacticalPitch.Danger(intel.Projected(a, Time.time), defendsPositiveZ);
                float db = TacticalPitch.Danger(intel.Projected(b, Time.time), defendsPositiveZ);
                return db.CompareTo(da);
            });

            Vector3 goal = TacticalPitch.GoalCentre(defendsPositiveZ);

            for (int t = 0; t < threats.Count; t++)
            {
                Vector3 man = intel.Projected(threats[t], Time.time);

                // Goal-side of him, between him and the shot he wants.
                Vector3 toGoal = Flat(goal - man).normalized;
                Vector3 markPos = man + toGoal * markGap;

                int pick = -1;
                float best = maxMarkTravel;
                for (int i = 0; i < n; i++)
                {
                    if (members[i] == null || pulled[i]) continue;
                    float d = Flat(slotNow[i] - markPos).magnitude;
                    if (d < best) { best = d; pick = i; }
                }

                if (pick < 0) continue;      // nobody close enough; the shape keeps its integrity
                slotNow[pick] = markPos;
                pulled[pick] = true;
                members[pick].markTarget = man;
            }

            for (int i = 0; i < n; i++)
                if (members[i] != null && !pulled[i]) members[i].markTarget = null;
        }

        /// <summary>
        /// Principle 5. Each hole a marker left gets ONE man leaning into it - the nearest
        /// one still holding shape.
        ///
        /// Iterating the other way round, and letting everybody lean toward the nearest
        /// hole, collapses the whole side into a column: one centre-back steps out and all
        /// nine team-mates drift after him. Cover is one man's job, not the team's.
        /// </summary>
        void Cover(int n, Vector3 shift)
        {
            for (int hole = 0; hole < n; hole++)
            {
                if (!pulled[hole] || members[hole] == null) continue;
                Vector3 vacated = members[hole].homeSlot + shift;

                int pick = -1;
                float best = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    if (members[i] == null || pulled[i] || members[i].covering) continue;
                    float d = Flat(slotNow[i] - vacated).sqrMagnitude;
                    if (d < best) { best = d; pick = i; }
                }
                if (pick < 0) continue;

                slotNow[pick] = Vector3.Lerp(slotNow[pick], vacated, coverBlend);
                members[pick].covering = true;
            }
        }

        // -------------------------------------------------------------- pressing --

        void MeasureBreak(Vector3 ballPos)
        {
            float dt = Time.time - prevBallAt;
            if (dt > 0.001f && prevBallAt > 0f)
            {
                Vector3 goal = TacticalPitch.GoalCentre(defendsPositiveZ);
                float was = Flat(goal - prevBall).magnitude;
                float now = Flat(goal - ballPos).magnitude;
                OnTheBreak = (was - now) / dt > breakSpeed;
            }
            prevBall = ballPos;
            prevBallAt = Time.time;
        }

        /// <summary>
        /// Whoever is closest to the man on the ball goes and stops him turning. Everyone
        /// else keeps their station - the whole point is that only one man leaves.
        /// </summary>
        void AssignPresser(int n, Vector3 ballPos)
        {
            int pick = -1;
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (members[i] == null) continue;
                float d = Flat(members[i].transform.position - ballPos).magnitude;
                if (d < best) { best = d; pick = i; }
            }

            Presser = pick >= 0 ? members[pick] : null;

            for (int i = 0; i < n; i++)
            {
                if (members[i] == null) continue;
                bool isPresser = i == pick;
                members[i].SetPressing(isPresser, CarrierPos, defendsPositiveZ, OnTheBreak);
                if (isPresser) members[i].covering = false;
            }
        }

        // ----------------------------------------------------------------- utils --

        Vector3 NearestOpponentTo(Vector3 p, Vector3 fallback)
        {
            if (opponents == null) return fallback;
            Vector3 best = fallback;
            float bd = float.MaxValue;
            for (int k = 0; k < opponents.Length; k++)
            {
                if (opponents[k] == null) continue;
                Vector3 q = intel.Projected(k, Time.time);
                float d = Flat(q - p).sqrMagnitude;
                if (d < bd) { bd = d; best = q; }
            }
            return best;
        }

        /// <summary>Where the block is standing, used to judge whether the ball is on us.</summary>
        Vector3 BlockCentre()
        {
            Vector3 sum = Vector3.zero;
            int c = 0;
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] == null) continue;
                sum += members[i].transform.position;
                c++;
            }
            return c > 0 ? sum / c : transform.position;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        void OnDrawGizmosSelected()
        {
            if (members == null) return;
            for (int i = 0; i < members.Length && i < slotNow.Length; i++)
            {
                if (members[i] == null) continue;
                Gizmos.color = pulled.Length > i && pulled[i]
                    ? new Color(1f, 0.4f, 0.2f)
                    : new Color(0.3f, 0.8f, 1f);
                Gizmos.DrawWireSphere(slotNow[i] + Vector3.up * 0.1f, 0.9f);
                Gizmos.DrawLine(members[i].transform.position, slotNow[i]);
            }
        }
    }
}
