using System.Collections.Generic;
using UnityEngine;

namespace Prototype
{
    public enum PassKind
    {
        Ground,   // along the floor, through a lane nobody is standing in
        Lofted,   // over the top, because there was no lane left
        Blind     // nobody was there - struck down the line anyway, which is a mistake
    }

    /// <summary>One option, with the reasoning left attached so it can be drawn and logged.</summary>
    public struct PassCandidate
    {
        public Transform receiver;
        public Vector3 target;
        public float distance;
        public float clearance;      // nearest defender to the lane
        public float flightTime;
        public int cellDefenders;    // how crowded the block he is standing in is

        // the terms, each 0..1 before weighting
        public float threat;         // 1 - how dangerous the position he would receive in is
        public float quality;        // 2 - how much better a passer he is than the man on the ball
        public float freedom;        // 3 - how unpressed he is, and how empty his block
        public float risk;           // the cost: how long the lane has to stay open

        public float total;
        public bool open;            // survived the gates
        public string blockedBy;     // which gate rejected him, for the readout
    }

    /// <summary>What a passer decided to do, and enough of the reasoning to draw it.</summary>
    public struct PassPlan
    {
        public bool valid;
        public Transform receiver;
        public Vector3 from;
        public Vector3 target;        // where it is aimed, lead included
        public float distance;
        public float clearance;       // nearest defender to the chosen lane
        public PassKind kind;
        public int rejected;          // options the gates threw out
        public int cellDefenders;     // how crowded the zone he was picked from is
        public PassCandidate winner;  // the full score sheet for the ball that was played
    }

    [System.Serializable]
    public class PassRules
    {
        [Header("Gates - a ball that fails these is not an option at all")]
        [Tooltip("Half-width of the corridor a ground pass has to travel down. A defender inside it kills the pass. This is a gate, not a preference: a ball played into a body is a turnover, not a slightly worse option.")]
        public float laneHalfWidth = 2f;

        [Tooltip("Longest ground pass anybody attempts. Past this the only option is a ball over the top.")]
        public float groundRange = 30f;

        [Tooltip("Shorter than this and it is not a pass, it is a touch to a man standing next to you.")]
        public float minRange = 5f;

        [Tooltip("Most defenders allowed in the receiver's block. Deliberately LOOSE - it is the knob the calibration run is meant to find, and a tight guess here would hide the data that finds it.")]
        public int maxCellDefenders = 3;

        [Tooltip("The man pressing the passer is not blocking any one lane - he is near the start of every one of them. Ignore the first stretch of the line. (PROJECT.md 3.16)")]
        public float carrierShadow = 2.5f;

        [Header("Weights - what makes one open ball better than another")]
        [Tooltip("1 - play it to a man in a dangerous position: the box, the half-spaces. Uses the same Danger() the defence ranks threats by.")]
        public float wThreat = 1.00f;
        [Tooltip("2 - give it to a better passer than yourself. Zero when nobody is better, which is how this criterion steps aside instead of blocking.")]
        public float wQuality = 0.55f;
        [Tooltip("3 - play it to whoever has the most room, in the emptiest block.")]
        public float wFreedom = 0.80f;
        [Tooltip("The cost of a long ball: every extra second of flight is another second the lane has to stay open, and a defender shuts a 2 m corridor in 0.3.")]
        public float wRisk = 0.70f;

        [Header("Term scaling")]
        [Tooltip("How much of criterion 1 is PROGRESSION rather than absolute danger. Danger() measures how close to conceding a position is, so it reads zero across your own half and most of midfield - on its own the criterion is dead weight for eighty per cent of the pitch, and the score collapses back to 'shortest open ball', which is the ladder it was meant to replace.")]
        [Range(0f, 1f)] public float progressionShare = 0.5f;
        [Tooltip("Metres of ground gained toward the goal that counts as a full point of progression.")]
        public float progressionSpan = 18f;
        [Tooltip("Passing advantage that counts as a full point on criterion 2.")]
        public float qualitySpan = 0.20f;
        [Tooltip("Distance from the nearest defender that counts as completely free.")]
        public float freeSpace = 8f;
        [Tooltip("Defenders in a block that counts as completely congested.")]
        public float denseBlock = 3f;
        [Tooltip("How much of criterion 3 is his own space rather than his block's emptiness.")]
        [Range(0f, 1f)] public float personalSpaceShare = 0.5f;
        [Tooltip("Flight time that counts as a full point of risk. Has to sit ABOVE the longest ball actually attempted or every pass past ~16 m scores the same maximum and the term stops separating them - at which point the shortest ball always wins and nothing else in the score matters.")]
        public float riskTime = 3.2f;

        [Header("The human's wedge")]
        [Tooltip("Half-angle of the wedge a human's stick input picks a team-mate out of. Outside it he strikes the raw direction instead and the pass goes nowhere.")]
        [Range(2f, 60f)] public float coneHalfAngle = 15f;

        [Header("Ball")]
        [Tooltip("How much of the receiver's own run the ball is played in front of. 0 puts every ball on his standing foot.")]
        [Range(0f, 1.5f)] public float leadFactor = 0.55f;
        public float maxLead = 4f;

        [Tooltip("Apex of a ball over the top, per metre of distance. It has to clear the heads in between, and that is what the pass is for.")]
        public float loftApexPerMetre = 0.16f;
        public float loftApexMin = 3f;
        public float loftApexMax = 9f;

        [Tooltip("Pace a ground pass should still be doing when it reaches him. Too dead and he has to come back for it - and the slower the ball, the longer the lane has to survive.")]
        public float arrivePace = 3.5f;
    }

    /// <summary>
    /// Who to pass to.
    ///
    /// This used to be a ladder - nearest man first, next nearest if his lane was shut -
    /// and the ladder was the problem. A ladder gives the first criterion a veto over
    /// every other one, so the ball went to the man standing next to you while a better
    /// ball existed and nothing in the code was allowed to say so.
    ///
    /// It is a score now, for the same reason the off-ball movement is one (PROJECT.md
    /// §3.16): the things that make a pass good pull in different directions and cannot
    /// be ranked ahead of each other. A dangerous position, a better passer to give it
    /// to, and a man with room are three separate goods, and the ball goes to whoever
    /// satisfies the most of them at once.
    ///
    ///   1  threat   - is he receiving somewhere that hurts? the box, the half-spaces
    ///   2  quality  - is he a better passer than me? (zero when I am the best - it
    ///                 steps aside rather than blocking)
    ///   3  freedom  - has he got room, and is his block empty?
    ///      risk     - minus the flight time, because a lane only has to stay open for
    ///                 as long as the ball is in the air, and that is where passes die
    ///
    /// WHAT IS NOT A TERM: whether the lane is open. That stays a hard gate. A ball
    /// played across a defender is not a slightly worse pass, it is a turnover, and
    /// letting a high threat score buy its way through a body would undo the entire
    /// point of the lane test.
    ///
    /// Distance is not a term either - it enters only through risk, as flight time.
    /// That is the whole fix: proximity is a cost now, not an ordering.
    /// </summary>
    public static class PassPlanner
    {
        /// <summary>
        /// Score every option and take the best. `scratch` is filled with the full
        /// ranking - the caller keeps it so the debug view can draw the losers too.
        /// </summary>
        public static PassPlan Choose(Transform self, Vector3 from,
                                      IList<Transform> mates, IList<Transform> opponents,
                                      PassRules r, bool attacksPositiveZ, Ball ball,
                                      List<PassCandidate> scratch)
        {
            PassPlan plan = new PassPlan();
            plan.from = from;
            if (scratch != null) scratch.Clear();
            if (mates == null || mates.Count == 0) return plan;

            Vector3 f = Flat(from);
            float selfPassing = PassingOf(self);
            float decel = ball != null ? ball.rollDecel : 5f;

            int best = -1;
            float bestScore = float.NegativeInfinity;
            int rejected = 0;

            for (int i = 0; i < mates.Count; i++)
            {
                if (mates[i] == null || mates[i] == self) continue;

                PassCandidate c = new PassCandidate();
                c.receiver = mates[i];
                c.target = LeadPoint(from, mates[i], r, ball, false);
                c.distance = Flat(c.target - f).magnitude;
                c.cellDefenders = DefendersInCell(c.target, opponents, attacksPositiveZ);
                c.clearance = LaneClearance(from, c.target, opponents, r.carrierShadow);

                float v0 = Ball.SpeedToReach(c.distance, r.arrivePace, decel);
                c.flightTime = Ball.TravelTime(c.distance, v0, decel);
                if (float.IsInfinity(c.flightTime)) c.flightTime = r.riskTime;

                // --- gates ------------------------------------------------------
                c.open = true;
                if (c.distance < r.minRange) { c.open = false; c.blockedBy = "너무 가까움"; }
                else if (c.distance > r.groundRange) { c.open = false; c.blockedBy = "사거리 초과"; }
                else if (c.clearance < r.laneHalfWidth) { c.open = false; c.blockedBy = "레인 막힘"; }
                else if (c.cellDefenders > r.maxCellDefenders) { c.open = false; c.blockedBy = "밀집 지역"; }

                // --- terms ------------------------------------------------------
                // 1. somewhere that hurts - and, since nowhere in your own half does,
                //    also simply moving the ball toward the goal. The first half is the
                //    same Danger() the defence ranks threats by, read from the other side.
                c.threat = Threat(from, c.target, r, attacksPositiveZ);

                // 2. a better passer than me. Zero if I am the best on the pitch, which
                //    is how "본인이 가장 높다면 다음으로" falls out without a special case.
                c.quality = Mathf.Clamp01((PassingOf(mates[i]) - selfPassing)
                                          / Mathf.Max(r.qualitySpan, 0.01f));

                // 3. room. Half his own, half his block's.
                float personal = Mathf.Clamp01(NearestOpponent(c.target, opponents) / Mathf.Max(r.freeSpace, 0.1f));
                float block = 1f - Mathf.Clamp01(c.cellDefenders / Mathf.Max(r.denseBlock, 0.1f));
                c.freedom = Mathf.Lerp(block, personal, r.personalSpaceShare);

                // the cost.
                c.risk = Mathf.Clamp01(c.flightTime / Mathf.Max(r.riskTime, 0.01f));

                c.total = r.wThreat * c.threat
                        + r.wQuality * c.quality
                        + r.wFreedom * c.freedom
                        - r.wRisk * c.risk;

                if (scratch != null) scratch.Add(c);

                if (!c.open) { rejected++; continue; }
                if (c.total > bestScore) { bestScore = c.total; best = scratch != null ? scratch.Count - 1 : i; }
            }

            plan.rejected = rejected;

            if (best >= 0 && scratch != null)
            {
                PassCandidate w = scratch[best];
                plan.valid = true;
                plan.receiver = w.receiver;
                plan.target = w.target;
                plan.distance = w.distance;
                plan.clearance = w.clearance;
                plan.cellDefenders = w.cellDefenders;
                plan.kind = PassKind.Ground;
                plan.winner = w;
                return plan;
            }

            // --- nothing on the floor. Hit the emptiest block over the top ---------
            return LongBall(self, from, mates, opponents, r, attacksPositiveZ, ball, rejected);
        }

        /// <summary>
        /// Every lane is shut, so the ball goes over the heads that shut them - to
        /// whoever is standing in the emptiest cell of the tactical grid.
        ///
        /// This one stays an ordering rather than a score, because there is only one
        /// thing being asked: where is there nobody. Height already answers everything
        /// else - a ball in the air cannot be intercepted (§3.18), so the lane it
        /// travels down does not need to be open.
        /// </summary>
        static PassPlan LongBall(Transform self, Vector3 from, IList<Transform> mates,
                                 IList<Transform> opponents, PassRules r,
                                 bool attacksPositiveZ, Ball ball, int rejected)
        {
            PassPlan plan = new PassPlan();
            plan.from = from;
            plan.rejected = rejected;

            Vector3 f = Flat(from);
            float dir = attacksPositiveZ ? 1f : -1f;

            int best = -1;
            int bestCount = int.MaxValue;
            float bestForward = float.NegativeInfinity;

            for (int i = 0; i < mates.Count; i++)
            {
                if (mates[i] == null || mates[i] == self) continue;
                if (Flat(mates[i].position - f).magnitude < r.minRange) continue;

                int crowd = DefendersInCell(mates[i].position, opponents, attacksPositiveZ);
                float forward = mates[i].position.z * dir;

                // Emptiest block wins; ties go to the man furthest up the pitch, because
                // a ball hoisted backwards into space is not what this rule is for.
                if (crowd < bestCount || (crowd == bestCount && forward > bestForward))
                {
                    bestCount = crowd;
                    bestForward = forward;
                    best = i;
                }
            }

            if (best < 0) return plan;

            plan.valid = true;
            plan.receiver = mates[best];
            plan.target = LeadPoint(from, mates[best], r, ball, true);
            plan.distance = Flat(plan.target - f).magnitude;
            plan.clearance = LaneClearance(from, plan.target, opponents, r.carrierShadow);
            plan.kind = PassKind.Lofted;
            plan.cellDefenders = bestCount;
            return plan;
        }

        /// <summary>
        /// Distance from the lane to the nearest defender. The first `shadow` metres of
        /// the line are skipped - see PassRules.carrierShadow.
        /// </summary>
        public static float LaneClearance(Vector3 from, Vector3 to, IList<Transform> opponents, float shadow)
        {
            Vector3 a = Flat(from);
            Vector3 b = Flat(to);
            Vector3 ab = b - a;
            float len = ab.magnitude;
            if (len > shadow * 2f) a += ab * (shadow / len);

            float best = float.MaxValue;
            for (int k = 0; opponents != null && k < opponents.Count; k++)
            {
                if (opponents[k] == null) continue;
                float d = PointToSegment(Flat(opponents[k].position), a, b);
                if (d < best) best = d;
            }
            return best == float.MaxValue ? 99f : best;
        }

        /// <summary>
        /// The human's version. He points, and the ball goes to the nearest team-mate
        /// inside the wedge - not down the raw stick direction, which is a direction no
        /// footballer has ever passed in.
        ///
        /// Nearest, not best: this one IS his decision, and scoring it for him would be
        /// playing the game on his behalf. The bots score because nobody is holding
        /// their stick.
        ///
        /// If the wedge is empty he strikes the line anyway. That is not a fallback, it
        /// is the mistake: he has passed into space nobody was running into, and the ball
        /// is there to be picked off.
        /// </summary>
        public static PassPlan PickInCone(Transform self, Vector3 from, Vector2 aim,
                                          IList<Transform> mates, PassRules r,
                                          float straightDistance)
        {
            PassPlan plan = new PassPlan();
            plan.from = from;

            Vector3 f = Flat(from);
            Vector3 dir = new Vector3(aim.x, 0f, aim.y);
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            dir.Normalize();

            Transform best = null;
            float bestDist = float.MaxValue;

            for (int i = 0; mates != null && i < mates.Count; i++)
            {
                if (mates[i] == null || mates[i] == self) continue;
                Vector3 d = Flat(mates[i].position - f);
                float dist = d.magnitude;
                if (dist < 1.5f) continue;
                if (Vector3.Angle(dir, d) > r.coneHalfAngle) continue;
                if (dist < bestDist) { bestDist = dist; best = mates[i]; }
            }

            plan.valid = true;
            if (best != null)
            {
                plan.receiver = best;
                plan.target = Flat(best.position);
                plan.distance = bestDist;
                plan.kind = PassKind.Ground;
            }
            else
            {
                plan.receiver = null;
                plan.target = f + dir * straightDistance;
                plan.distance = straightDistance;
                plan.kind = PassKind.Blind;
            }
            return plan;
        }

        /// <summary>
        /// Criterion 1, in two parts.
        ///
        /// Danger() answers "how much does it hurt to concede from here", which is the
        /// right question in the final third and a useless one on your own edge of the
        /// box - it reads flat zero for both a square ball and a fifteen-metre ball
        /// forward. Progression carries the term until Danger wakes up: how much closer
        /// to the goal the ball ends up.
        /// </summary>
        public static float Threat(Vector3 from, Vector3 to, PassRules r, bool attacksPositiveZ)
        {
            Vector3 goal = TacticalPitch.GoalCentre(attacksPositiveZ);
            float was = Flat(goal - from).magnitude;
            float now = Flat(goal - to).magnitude;
            float progression = Mathf.Clamp01((was - now) / Mathf.Max(r.progressionSpan, 0.1f));

            return Mathf.Lerp(TacticalPitch.Danger(to, attacksPositiveZ), progression, r.progressionShare);
        }

        /// <summary>Apex for a ball over the top of this length.</summary>
        public static float LoftApex(float distance, PassRules r)
        {
            return Mathf.Clamp(distance * r.loftApexPerMetre, r.loftApexMin, r.loftApexMax);
        }

        /// <summary>How many defenders share the receiver's cell of the tactical grid.</summary>
        public static int DefendersInCell(Vector3 p, IList<Transform> opponents, bool attacksPositiveZ)
        {
            int cell = TacticalPitch.CellOf(p, attacksPositiveZ);
            int n = 0;
            for (int k = 0; opponents != null && k < opponents.Count; k++)
            {
                if (opponents[k] == null) continue;
                if (TacticalPitch.CellOf(opponents[k].position, attacksPositiveZ) == cell) n++;
            }
            return n;
        }

        public static float NearestOpponent(Vector3 p, IList<Transform> opponents)
        {
            float best = float.MaxValue;
            for (int k = 0; opponents != null && k < opponents.Count; k++)
            {
                if (opponents[k] == null) continue;
                float d = Flat(opponents[k].position - p).magnitude;
                if (d < best) best = d;
            }
            return best == float.MaxValue ? 99f : best;
        }

        // ------------------------------------------------------------------ lead ---

        /// <summary>
        /// Where to aim, given that he is moving. Solved the same way the drill's lead
        /// pass is (PROJECT.md 3.5): guess the flight time, move him along it, re-solve.
        /// Three passes is plenty - the correction shrinks fast.
        /// </summary>
        static Vector3 LeadPoint(Vector3 from, Transform receiver, PassRules r, Ball ball, bool lofted)
        {
            Vector3 here = Flat(receiver.position);
            Vector3 v = Flat(VelocityOf(receiver));
            if (v.sqrMagnitude < 0.25f || r.leadFactor <= 0f) return here;

            float decel = ball != null ? ball.rollDecel : 5f;
            float g = ball != null ? ball.gravity : 9.81f;
            Vector3 target = here;

            for (int i = 0; i < 3; i++)
            {
                float d = Flat(target - from).magnitude;
                float t;
                if (lofted)
                {
                    t = Ball.HangTime(LoftApex(d, r), g);
                }
                else
                {
                    float v0 = Ball.SpeedToReach(d, r.arrivePace, decel);
                    t = Ball.TravelTime(d, v0, decel);
                    if (float.IsInfinity(t) || t > 4f) t = 4f;
                }

                Vector3 lead = Vector3.ClampMagnitude(v * r.leadFactor * t, r.maxLead);
                target = here + lead;
            }

            target.x = Mathf.Clamp(target.x, -TacticalPitch.HalfW + 1f, TacticalPitch.HalfW - 1f);
            target.z = Mathf.Clamp(target.z, -TacticalPitch.HalfL + 1f, TacticalPitch.HalfL - 1f);
            return target;
        }

        /// <summary>
        /// How the receiver is moving. Looked up rather than passed in because this runs
        /// once, at the instant of the pass, not every frame for every candidate.
        /// </summary>
        static Vector3 VelocityOf(Transform t)
        {
            AttackerAI a = t.GetComponent<AttackerAI>();
            if (a != null) return a.Velocity;
            FootballerController f = t.GetComponent<FootballerController>();
            if (f != null) return f.Velocity;
            return Vector3.zero;
        }

        /// <summary>His passing attribute, whoever he happens to be.</summary>
        public static float PassingOf(Transform t)
        {
            if (t == null) return 0.5f;
            AttackerAI a = t.GetComponent<AttackerAI>();
            if (a != null) return a.passing;
            FootballerController f = t.GetComponent<FootballerController>();
            if (f != null) return f.passing;
            return 0.5f;
        }

        // ----------------------------------------------------------------- utils ---

        public static float PointToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-5f) return (p - a).magnitude;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
            return (p - (a + ab * t)).magnitude;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
