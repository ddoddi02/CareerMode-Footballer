using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// How an attribute turns into a ball. Deliberately a handful of formulas rather
    /// than aerodynamics - if the simulation is noisy the player can never feel the
    /// difference between Passing 12 and Passing 16, and in a career game that
    /// difference IS the game.
    ///
    /// The load-bearing idea: an attribute governs ERROR far more than POWER. Anyone
    /// can strike a ball at 20 m/s; the difference is whether it arrives at a foot or
    /// two metres away from one. So speed gets a narrow band and error gets a wide one.
    ///
    /// The two errors also have different causes, which keeps the maths honest:
    ///   - direction goes wrong because of PRESSURE
    ///   - weight goes wrong because of DISTANCE
    /// Aim error is therefore defined in METRES and converted to an angle, rather than
    /// being an angle in the first place. An angle would compound with distance and a
    /// 40 m ball would spray absurdly.
    /// </summary>
    public static class BallModel
    {
        // ---- speed: narrow band, so power never becomes a stat-check ------------
        [System.Serializable]
        public struct Base
        {
            public const float ShortPass = 14f;
            public const float ThroughBall = 18f;
            public const float Cross = 20f;
            public const float LongPass = 22f;
            public const float Shot = 26f;
        }

        public const float SpeedSpread = 0.30f;   // +-15% around the base

        public static float Speed(float baseSpeed, float stat01)
        {
            return baseSpeed * (1f - SpeedSpread * 0.5f + SpeedSpread * Mathf.Clamp01(stat01));
        }

        // ---- error: wide band, this is where skill actually shows ---------------
        public const float AimErrorMetres = 5.0f;   // worst case at 30 m, stat 0, difficulty 1
        public const float WeightErrorSpread = 0.18f;
        public const float MaxDifficulty = 3.0f;

        /// <summary>
        /// Roughly normal, bounded to [-1, 1]. Most balls are struck well and the
        /// occasional one is badly wrong - which is what a bell curve gives you and a
        /// flat Random.Range does not.
        /// </summary>
        public static float Bell()
        {
            return (Random.value + Random.value + Random.value - 1.5f) / 1.5f;
        }

        /// <summary>
        /// Everything that makes the strike harder. Distance is deliberately NOT in
        /// here - it belongs to weight error only.
        /// </summary>
        public static float Difficulty(float pressure01, float bodyAngleDeg,
                                       bool firstTime, bool weakFoot)
        {
            float d = 1f;
            d *= Mathf.Lerp(1f, 2.0f, Mathf.Clamp01(pressure01));
            d *= Mathf.Lerp(1f, 1.8f, Mathf.Clamp01(bodyAngleDeg / 180f));
            if (firstTime) d *= 1.5f;
            if (weakFoot) d *= 1.6f;
            return Mathf.Min(d, MaxDifficulty);
        }

        /// <summary>Sub-linear in distance: 1.0 at 30 m, 0.6 at 10 m, 1.4 at 50 m.</summary>
        public static float DistanceFactor(float dist)
        {
            return 0.4f + 0.6f * (dist / 30f);
        }

        /// <summary>
        /// How wide the miss can be, in metres, before the dice are thrown - the RADIUS
        /// of the circle the ball might go through.
        ///
        /// Split out from AimError so the thing drawn on screen and the thing the strike
        /// actually rolls against are the same number, computed once. A reticle that
        /// works out its own radius would drift away from the strike it is describing,
        /// and the player would believe the picture (PROJECT.md 3.20).
        ///
        /// Note what it is NOT: a distance the ball stops at. It is a lateral spread at
        /// the aim point, turned into an angle by Resolve, so the ball travels down one
        /// of the lines through that circle and keeps going.
        /// </summary>
        public static float AimSpread(float stat01, float dist, float difficulty)
        {
            return AimErrorMetres * (1f - Mathf.Clamp01(stat01)) * difficulty
                 * DistanceFactor(dist);
        }

        /// <summary>Signed sideways miss, in metres. Convert to an angle at the end.</summary>
        public static float AimError(float stat01, float dist, float difficulty)
        {
            return AimSpread(stat01, dist, difficulty) * Bell();
        }

        /// <summary>
        /// Speed multiplier around 1. Kept small on purpose: with rolling friction the
        /// distance a ball covers goes as v^2, so a 10% speed error is a 21% distance
        /// error. Over-hitting is how long passes actually fail.
        /// </summary>
        public static float WeightError(float stat01, float dist, float difficulty)
        {
            return 1f + WeightErrorSpread * (1f - Mathf.Clamp01(stat01)) * difficulty
                      * (dist / 30f) * Bell();
        }

        public struct Strike
        {
            public Vector3 direction;    // XZ, normalised, error applied
            public float speed;
            public float aimErrorM;      // signed, for telemetry
            public float speedErrorPct;
        }

        /// <summary>
        /// Intended from -> to becomes an actual direction and speed.
        /// Pass <paramref name="baseSpeed"/> &lt;= 0 to keep <paramref name="requiredSpeed"/>
        /// as the intent (used when the physics already solved how hard to hit it).
        /// </summary>
        public static Strike Resolve(Vector3 from, Vector3 to, float baseSpeed, float requiredSpeed,
                                     float stat01, float difficulty)
        {
            Vector3 d = to - from;
            d.y = 0f;
            float dist = d.magnitude;
            Vector3 dir = dist > 1e-4f ? d / dist : Vector3.forward;

            float aimM = AimError(stat01, dist, difficulty);
            float deg = Mathf.Atan2(aimM, Mathf.Max(dist, 0.5f)) * Mathf.Rad2Deg;
            dir = Quaternion.Euler(0f, deg, 0f) * dir;

            float w = WeightError(stat01, dist, difficulty);
            float spd = (baseSpeed > 0f ? Speed(baseSpeed, stat01) : requiredSpeed) * w;

            Strike s;
            s.direction = dir;
            s.speed = spd;
            s.aimErrorM = aimM;
            s.speedErrorPct = (w - 1f) * 100f;
            return s;
        }
    }
}
