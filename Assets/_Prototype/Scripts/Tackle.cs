using UnityEngine;

namespace Prototype
{
    public enum TackleResult
    {
        OutOfRange,   // he lunged and never got near the ball
        Won,          // he came away with it
        Lost,         // the attacker held him off
        Foul          // he went through the man instead
    }

    /// <summary>
    /// Whether a challenge wins the ball.
    ///
    /// TWO CONDITIONS, BOTH REQUIRED:
    ///
    ///   1. the DEFENDER is within reach of the BALL   (defender -> ball <= 1 m)
    ///   2. no attacker body sits between them
    ///
    /// Note what is NOT a condition: how far the ball is from the man dribbling it.
    /// That number (Ball.Exposure) only decides WHEN the defender chooses to commit -
    /// he waits for a heavy touch - and never whether the challenge works. A tucked-in
    /// ball is still lost if the defender gets a foot to it unobstructed.
    ///
    /// Two things are layered on the bare rule:
    ///
    ///  - CONTEST IS A ROLL, not an automatic fail. A pure "shielded -> fail" makes a
    ///    shielding attacker untouchable, so the optimal play becomes shielding
    ///    forever. Tackling vs Strength decides that case instead.
    ///
    ///  - A MISSED TACKLE CAN BE A FOUL. Without a cost, lunging is free and the
    ///    defender should simply spam it. Going through the back of a man is what
    ///    stops that, in football and here.
    /// </summary>
    public static class Tackle
    {
        public const float Reach = 1.0f;          // condition 1: defender to ball
        public const float ShieldRadius = 0.45f;  // roughly a torso

        /// <summary>Is p inside the corridor from a to b?</summary>
        public static bool IsBetween(Vector3 p, Vector3 a, Vector3 b, float radius)
        {
            Vector3 ab = b - a; ab.y = 0f;
            float len = ab.magnitude;
            if (len < 1e-4f) return false;

            Vector3 dir = ab / len;
            Vector3 ap = p - a; ap.y = 0f;
            float along = Vector3.Dot(ap, dir);
            if (along < 0f || along > len) return false;          // not between them

            return (ap - dir * along).magnitude < radius;
        }

        /// <summary>
        /// The two gate conditions, with no dice rolled. Used by the HUD so the rule
        /// is visible while playing.
        /// </summary>
        public static void Probe(Vector3 defenderPos, Vector3 ballPos, Vector3 attackerPos,
                                 float reach, out float ballGap, out bool inReach, out bool shielded)
        {
            Vector3 toBall = ballPos - defenderPos;
            toBall.y = 0f;
            ballGap = toBall.magnitude;
            inReach = ballGap <= reach;
            shielded = IsBetween(attackerPos, defenderPos, ballPos, ShieldRadius);
        }

        public struct Input
        {
            public Vector3 defenderPos;
            public Vector3 ballPos;
            public Vector3 attackerPos;
            public Vector2 attackerFacing;
            /// <summary>Moving or actively shielding. Standing idle is not resisting.</summary>
            public bool attackerResisting;
            public float tackling01;
            public float strength01;
            public float reach;
        }

        public static TackleResult Resolve(Input i, out string reason, out bool shielded)
        {
            float ballGap;
            bool inReach;
            Probe(i.defenderPos, i.ballPos, i.attackerPos, i.reach, out ballGap, out inReach, out shielded);

            // --- condition 1 ------------------------------------------------------
            if (!inReach)
            {
                reason = string.Format("공까지 {0:0.00}m — 발이 닿지 않았습니다", ballGap);
                return TackleResult.OutOfRange;
            }

            // --- condition 2 ------------------------------------------------------
            if (!shielded)
            {
                reason = string.Format("공까지 {0:0.00}m, 사이에 몸이 없었습니다", ballGap);
                return TackleResult.Won;
            }

            // Both conditions would have won it, but a body is in the way. The
            // specified rule says a passive attacker still loses it.
            if (!i.attackerResisting)
            {
                reason = "가만히 서서는 공을 못 지킵니다";
                return TackleResult.Won;
            }

            // Contested: he is between the defender and the ball, and he is working.
            float p = Mathf.Clamp(0.5f + 0.35f * (i.tackling01 - i.strength01), 0.08f, 0.92f);
            if (Random.value < p)
            {
                reason = "몸싸움에서 밀렸습니다";
                return TackleResult.Won;
            }

            // He held him off. Did the defender go through him?
            Vector3 toDef = i.defenderPos - i.attackerPos;
            toDef.y = 0f;
            Vector2 td = new Vector2(toDef.x, toDef.z).normalized;
            bool fromBehind = Vector2.Dot(td, i.attackerFacing) < -0.30f;

            float foulChance = fromBehind ? 0.55f : 0.18f;
            foulChance *= Mathf.Lerp(1.4f, 0.6f, i.tackling01);   // clumsy tacklers foul

            if (Random.value < foulChance)
            {
                reason = fromBehind ? "뒤에서 들어와 파울입니다" : "발이 먼저 들어가 파울입니다";
                return TackleResult.Foul;
            }

            reason = "몸으로 버텨냈습니다";
            return TackleResult.Lost;
        }
    }
}
