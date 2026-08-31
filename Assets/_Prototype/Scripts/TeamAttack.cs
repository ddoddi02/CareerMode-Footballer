using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// The side in possession. Where <see cref="TeamDefence"/> assigns stations to deny
    /// space, this one assigns them to open a passing lane.
    ///
    /// Every off-ball player is scored against a spread of candidate spots inside his own
    /// zone, and takes the best one. Scoring rather than rules is what lets five
    /// principles that pull in different directions coexist: a run in behind and a
    /// triangle and staying in position are all just terms, and the winning spot is
    /// whichever satisfies most of them at once.
    ///
    ///   1  carrier / his nearest team-mate / you form a triangle with an open lane
    ///   2  front-line players hunt for space inside their own zone
    ///   3  when the lane is congested, mix lateral and vertical movement to break free
    ///   4  run in behind on a timer - without straying offside
    ///   5  if the carrier is outnumbered, one man ahead and one behind come and show
    ///
    /// The candidate spread is a deterministic spiral, not Random.insideUnitCircle, so a
    /// replayed drill produces the same movement (PROJECT.md §3.6).
    /// </summary>
    public class TeamAttack : MonoBehaviour
    {
        [Header("Refs")]
        public Transform ball;
        [Tooltip("The defending side - used for lanes, space and the offside line.")]
        public Transform[] opponents;
        [Tooltip("Everyone of ours including the human, so the carrier can be found.")]
        public Transform[] mates;
        [Tooltip("The ones this actually moves. The human is not in here.")]
        public AttackerAI[] members;

        [Tooltip("True if this side attacks the goal at +Z.")]
        public bool attacksPositiveZ = true;

        [Header("What it knows, and when")]
        public PitchIntel intel = new PitchIntel();

        [Header("Shape")]
        [Range(0f, 1f)] public float lateralFollow = 0.35f;
        [Range(0f, 1f)] public float depthFollow = 0.45f;
        public float maxLateralShift = 10f;
        public float maxDepthShift = 18f;

        [Header("1 - triangles and lanes")]
        [Tooltip("Shortest ball worth offering.")]
        public float passMin = 7f;
        [Tooltip("Longest ball worth offering.")]
        public float passMax = 24f;
        [Tooltip("Clearance either side of the pass that counts as a properly open lane.")]
        public float laneOpen = 2.4f;
        [Tooltip("The man pressing the carrier is not blocking any one lane. Ignore the first stretch of the pass.")]
        public float carrierShadow = 2.5f;
        [Tooltip("Stations are kept this far inside the touchline.")]
        public float pitchInset = 1.5f;
        [Tooltip("Angle at the carrier that makes the best triangle.")]
        public float idealTriangleAngle = 60f;

        [Header("3 - unsticking")]
        [Tooltip("Lane clearance below which he is considered shut down and starts moving to get free.")]
        public float squeezedClearance = 2.6f;

        [Header("4 - runs in behind")]
        [Tooltip("How far short of the offside line he times his run to arrive.")]
        public float onsideMargin = 0.7f;

        [Header("5 - support when outnumbered")]
        public float pressureRadius = 9f;
        [Tooltip("How close the two supporting players try to get.")]
        public float showDistance = 9f;

        [Header("Scoring weights")]
        public float wLane = 1.00f;
        public float wSpace = 0.70f;
        public float wTriangle = 0.55f;
        public float wRange = 0.60f;
        public float wZone = 0.85f;
        public float wRun = 1.20f;
        public float wShow = 1.30f;
        public float wUnstick = 0.65f;

        [Header("Sampling")]
        [Range(6, 48)] public int samples = 24;

        /// <summary>The furthest line our attackers may stand on right now.</summary>
        public float OffsideLine { get; private set; }
        public Vector3 CarrierPos { get; private set; }
        public bool Outnumbered { get; private set; }

        Transform carrier;
        Transform support;
        int showAhead = -1, showBehind = -1;
        Vector3 shift;

        void Update()
        {
            if (ball == null || members == null || members.Length == 0) return;
            if (intel.Tick(Time.time, opponents, ball.position, BlockCentre()))
                Recompute();
        }

        // ---------------------------------------------------------------- picture --

        void Recompute()
        {
            Vector3 ballPos = intel.Ball;

            OffsideLine = Offside.Line(attacksPositiveZ, opponents, ballPos.z);
            carrier = NearestMate(ballPos, null);
            CarrierPos = carrier != null ? carrier.position : ballPos;
            support = carrier != null ? NearestMate(CarrierPos, carrier) : null;

            shift = ShapeShift(ballPos);
            AssessNumbers();

            for (int i = 0; i < members.Length; i++)
            {
                var m = members[i];
                if (m == null) continue;

                // The man on the ball is not looking for a spot to stand in.
                if (carrier != null && m.transform == carrier)
                {
                    m.SetStation(m.transform.position, false, false, ball);
                    continue;
                }

                bool squeezed = LaneClearance(CarrierPos, m.transform.position) < squeezedClearance;
                m.TickUnstick(squeezed);

                if (Formation.RunsInBehind(m.role) && Time.time >= m.nextRunAt) m.BeginRun();

                bool showing = i == showAhead || i == showBehind;
                m.SetStation(BestSpot(m, showing, squeezed), showing, squeezed, ball);
            }
        }

        Vector3 ShapeShift(Vector3 ballPos)
        {
            float dir = attacksPositiveZ ? 1f : -1f;
            float x = Mathf.Clamp(ballPos.x * lateralFollow, -maxLateralShift, maxLateralShift);
            float z = Mathf.Clamp(ballPos.z * dir * depthFollow, -maxDepthShift, maxDepthShift);
            return new Vector3(x, 0f, z * dir);
        }

        /// <summary>
        /// Principle 5. Count the bodies around the man on the ball. If they have more
        /// than we do, the nearest team-mate in front of him and the nearest behind stop
        /// whatever else they were doing and come and offer themselves.
        /// </summary>
        void AssessNumbers()
        {
            showAhead = showBehind = -1;
            Outnumbered = false;
            if (carrier == null) return;

            float dir = attacksPositiveZ ? 1f : -1f;
            int theirs = 0, ours = 0;

            for (int k = 0; opponents != null && k < opponents.Length; k++)
            {
                if (opponents[k] == null) continue;
                if (Flat(intel.Projected(k, Time.time) - CarrierPos).magnitude < pressureRadius) theirs++;
            }
            for (int k = 0; mates != null && k < mates.Length; k++)
            {
                if (mates[k] == null || mates[k] == carrier) continue;
                if (Flat(mates[k].position - CarrierPos).magnitude < pressureRadius) ours++;
            }

            Outnumbered = theirs > ours;
            if (!Outnumbered) return;

            float bestAhead = float.MaxValue, bestBehind = float.MaxValue;
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] == null || members[i].transform == carrier) continue;
                Vector3 d = Flat(members[i].transform.position - CarrierPos);
                float along = d.z * dir;
                float dist = d.magnitude;

                if (along >= 0f && dist < bestAhead) { bestAhead = dist; showAhead = i; }
                if (along < 0f && dist < bestBehind) { bestBehind = dist; showBehind = i; }
            }
        }

        // --------------------------------------------------------------- choosing --

        /// <summary>
        /// Score a spread of spots in his zone and take the best. The spiral is fixed, so
        /// the same situation always produces the same movement.
        /// </summary>
        Vector3 BestSpot(AttackerAI m, bool showing, bool squeezed)
        {
            Vector3 anchor = m.homeSlot + shift;
            float radius = Formation.ZoneRadius(m.role);
            if (m.Running) radius *= 1.5f;      // a run is allowed out of the zone
            if (showing) radius *= 1.35f;

            Vector3 here = m.transform.position;
            Vector3 unstickDir = m.UnstickDir();
            float dir = attacksPositiveZ ? 1f : -1f;

            Vector3 best = anchor;
            float bestScore = float.NegativeInfinity;

            for (int k = 0; k < samples + 2; k++)
            {
                Vector3 cand;
                if (k == 0) cand = anchor;
                else if (k == 1) cand = here;
                else
                {
                    // Golden-angle spiral: even coverage, no randomness.
                    float a = (k - 2) * 2.39996323f;
                    float r = radius * Mathf.Sqrt((k - 1.5f) / samples);
                    cand = anchor + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                }

                if (m.Running)
                {
                    // Attack the line - but arrive on the right side of it.
                    cand.z += dir * radius * 0.8f;
                }
                cand = Offside.KeepOnside(cand, OffsideLine, attacksPositiveZ, onsideMargin);
                cand.x = Mathf.Clamp(cand.x, -TacticalPitch.HalfW + pitchInset, TacticalPitch.HalfW - pitchInset);
                cand.z = Mathf.Clamp(cand.z, -TacticalPitch.HalfL + pitchInset, TacticalPitch.HalfL - pitchInset);

                float s = Score(m, cand, anchor, radius, here, unstickDir, showing, squeezed, dir);
                if (s > bestScore) { bestScore = s; best = cand; }
            }

            return best;
        }

        float Score(AttackerAI m, Vector3 cand, Vector3 anchor, float radius, Vector3 here,
                    Vector3 unstickDir, bool showing, bool squeezed, float dir)
        {
            float total = 0f;

            // 1 - can the ball actually reach him, and does it make a shape?
            float lane = Mathf.Min(LaneClearance(CarrierPos, cand), laneOpen * 2f) / (laneOpen * 2f);
            total += wLane * lane;

            float d = Flat(cand - CarrierPos).magnitude;
            float range = d < passMin ? d / Mathf.Max(passMin, 0.01f)
                        : d > passMax ? Mathf.Max(0f, 1f - (d - passMax) / 12f)
                        : 1f;
            total += wRange * range;

            if (support != null && carrier != null)
            {
                float ang = Vector3.Angle(Flat(support.position - CarrierPos), Flat(cand - CarrierPos));
                total += wTriangle * Mathf.Clamp01(1f - Mathf.Abs(ang - idealTriangleAngle) / 120f);
            }

            // 2 - space, and staying in his own zone while he looks for it.
            total += wSpace * Mathf.Min(NearestOpponent(cand), 8f) / 8f;
            total -= wZone * Mathf.Clamp01(Flat(cand - anchor).magnitude / Mathf.Max(radius, 0.01f));

            // 3 - when he is shut down, reward the direction he is currently working in.
            if (squeezed)
            {
                Vector3 step = Flat(cand - here);
                if (step.sqrMagnitude > 0.25f)
                    total += wUnstick * Vector3.Dot(step.normalized, unstickDir);
            }

            // 4 - on a run, the closer to the line the better. Offside was already clamped.
            if (m.Running)
                total += wRun * Mathf.Clamp01(1f - Mathf.Abs(Offside.Slack(cand, OffsideLine, attacksPositiveZ)) / 6f);

            // 5 - showing for an outnumbered team-mate means getting near him, on his side.
            if (showing)
                total += wShow * Mathf.Clamp01(1f - Mathf.Abs(d - showDistance) / showDistance);

            return total;
        }

        // ------------------------------------------------------------------ utils --

        /// <summary>
        /// How much room the pass has - distance from the nearest defender to the lane.
        ///
        /// The first couple of metres are skipped deliberately. A defender standing on the
        /// carrier's toes sits near the start of EVERY lane out of him, so measuring from
        /// the carrier's feet reports every single pass as blocked and the whole side
        /// reads as permanently shut down. He is pressure on the man, which is a separate
        /// problem; he is not covering any particular passing angle.
        /// </summary>
        public float LaneClearance(Vector3 from, Vector3 to)
        {
            Vector3 a = Flat(from);
            Vector3 b = Flat(to);
            Vector3 ab = b - a;
            float len = ab.magnitude;
            if (len > carrierShadow * 2f) a += ab * (carrierShadow / len);

            float best = float.MaxValue;
            for (int k = 0; opponents != null && k < opponents.Length; k++)
            {
                if (opponents[k] == null) continue;
                float d = PointToSegment(Flat(intel.Projected(k, Time.time)), a, b);
                if (d < best) best = d;
            }
            return best == float.MaxValue ? 99f : best;
        }

        float NearestOpponent(Vector3 p)
        {
            float best = float.MaxValue;
            for (int k = 0; opponents != null && k < opponents.Length; k++)
            {
                if (opponents[k] == null) continue;
                float d = Flat(intel.Projected(k, Time.time) - p).magnitude;
                if (d < best) best = d;
            }
            return best == float.MaxValue ? 99f : best;
        }

        Transform NearestMate(Vector3 p, Transform skip)
        {
            Transform best = null;
            float bd = float.MaxValue;
            for (int k = 0; mates != null && k < mates.Length; k++)
            {
                if (mates[k] == null || mates[k] == skip) continue;
                float d = Flat(mates[k].position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = mates[k]; }
            }
            return best;
        }

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

        static float PointToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-5f) return (p - a).magnitude;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
            return (p - (a + ab * t)).magnitude;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        void OnDrawGizmosSelected()
        {
            // The offside line, drawn across the pitch.
            Gizmos.color = new Color(1f, 0.85f, 0.2f);
            Gizmos.DrawLine(new Vector3(-34f, 0.1f, OffsideLine), new Vector3(34f, 0.1f, OffsideLine));

            if (members == null) return;
            for (int i = 0; i < members.Length; i++)
            {
                var m = members[i];
                if (m == null) continue;
                Gizmos.color = m.Running ? new Color(1f, 0.4f, 0.2f)
                             : m.Showing ? new Color(0.4f, 1f, 0.5f)
                             : new Color(0.4f, 0.7f, 1f);
                Gizmos.DrawWireSphere(m.Station + Vector3.up * 0.1f, 0.8f);
                Gizmos.DrawLine(m.transform.position, m.Station);

                // The lane the ball would have to travel.
                if (carrier != null && m.transform != carrier)
                {
                    float c = LaneClearance(CarrierPos, m.Station);
                    Gizmos.color = c >= laneOpen
                        ? new Color(0.3f, 1f, 0.4f, 0.5f)
                        : new Color(1f, 0.3f, 0.3f, 0.35f);
                    Gizmos.DrawLine(CarrierPos + Vector3.up * 0.2f, m.Station + Vector3.up * 0.2f);
                }
            }
        }
    }
}
