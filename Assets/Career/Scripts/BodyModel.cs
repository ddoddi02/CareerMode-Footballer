using System;
using UnityEngine;

namespace Career
{
    /// <summary>Traits the body brings out past a line (PROJECT.md 10.3).</summary>
    [Flags]
    public enum BodyTrait
    {
        None = 0,
        /// <summary>Body fat at or past its line: more lower-body injuries.</summary>
        ExcessFat = 1 << 0,
        /// <summary>Muscle share at or past its line: stamina drains faster, agility drops, more muscle injuries.</summary>
        ExcessMuscle = 1 << 1,
    }

    /// <summary>
    /// What the body does to the match. Mostly multipliers on whatever the match side already
    /// has (1 = no change). Nothing here is wired into <c>_Prototype/</c> yet; the match
    /// sessions will apply these when they connect it.
    /// </summary>
    public struct BodyModifiers
    {
        public float speed;          // top speed
        public float acceleration;
        public float agility;        // turning, braking
        public float stamina;        // below 1 = runs out sooner
        public float physicality;    // duels, shielding
        /// <summary>Balls in the air - headers, aerial duels. From height only (user: tall is better in the air). No aerial play exists yet.</summary>
        public float aerial;

        // No injury system yet - these only say how much more often.
        public float lowerBodyInjury;
        public float muscleInjury;

        public static BodyModifiers Neutral
        {
            get
            {
                return new BodyModifiers
                {
                    speed = 1f, acceleration = 1f, agility = 1f, stamina = 1f, physicality = 1f, aerial = 1f,
                    lowerBodyInjury = 1f, muscleInjury = 1f,
                };
            }
        }
    }

    /// <summary>
    /// Body composition -> match modifiers and traits. Pure functions, no state of its own.
    /// All numbers are first guesses with their reasons in PROJECT.md 10.3.
    /// </summary>
    public static class BodyModel
    {
        // ---- traits ---------------------------------------------------------------------
        public const float ExcessFatLine = 20f;       // % body fat (user)
        public const float ExcessMuscleLine = 55f;    // % muscle (user)
        /// <summary>A trait goes only once the body drops this many points under its line (user).</summary>
        public const float TraitRelease = 3f;
        public const float ExcessMuscleStamina = 0.85f;
        public const float ExcessMuscleAgility = 0.90f;
        public const float InjuryRate = 1.5f;

        // ---- body fat: big on speed / agility / stamina, small on physicality ------------
        public const float FatReference = 10f;        // outfield pros sit at 8-12%
        public const float FatBigPerPoint = 0.010f;   // above reference: -1% per point (dead weight)
        public const float FatBelowPerPoint = 0.005f; // below reference: +0.5% per point, half as much
        public const float FatSmallPerPoint = 0.003f; // physicality: +0.3% per point, the other way

        // ---- muscle: big on physicality / acceleration (and shot power - ShotPower) -----
        public const float MuscleReference = 48f;     // the default body
        /// <summary>The gain stops this far under the trait line, so the best body sits just short of it.</summary>
        public const float PeakBelowLine = 2f;
        public const float MusclePeak = ExcessMuscleLine - PeakBelowLine;

        // ---- height: balls in the air ---------------------------------------------------
        public const float HeightReference = 180f;
        /// <summary>+0.75% a centimetre: the top of the height range (200 cm) is the full +15% band.</summary>
        public const float AerialPerCm = 0.0075f;

        // No multiplier moves more than this, either way - the same band PROJECT.md 3.8
        // keeps for speed, so the body never becomes the whole stat check.
        public const float MaxSwing = 0.15f;

        /// <summary>
        /// The traits after this body, given the ones showing now. A trait comes on at its
        /// line and goes only <see cref="TraitRelease"/> points under it, so a body sitting
        /// on the line does not flicker - and a player who overshoots has to come well back.
        /// </summary>
        public static BodyTrait NextTraits(BodyTrait current, float bodyFatPercent, float musclePercent)
        {
            BodyTrait next = current;
            next = Toggle(next, BodyTrait.ExcessFat, bodyFatPercent, ExcessFatLine);
            next = Toggle(next, BodyTrait.ExcessMuscle, musclePercent, ExcessMuscleLine);
            return next;
        }

        /// <summary>This body, its height, and the multipliers its perks add (PerkCatalog).</summary>
        public static BodyModifiers Evaluate(PlayerProfile p)
        {
            BodyModifiers m = Evaluate(p.BodyFatPercent, p.MusclePercent, p.traits);
            m.aerial = Aerial(p.heightCm);
            float stamina, injury;
            PerkCatalog.Multipliers(p.perks, out stamina, out injury);
            m.stamina *= stamina;
            m.lowerBodyInjury *= injury;
            m.muscleInjury *= injury;
            return m;
        }

        public static BodyModifiers Evaluate(float bodyFatPercent, float musclePercent, BodyTrait traits)
        {
            BodyModifiers m = BodyModifiers.Neutral;

            float fatBig = FatBig(bodyFatPercent);
            m.speed = fatBig;
            m.agility = fatBig;
            m.stamina = fatBig;
            m.physicality = 1f + Mathf.Clamp(FatSmallPerPoint * (bodyFatPercent - FatReference), -MaxSwing, MaxSwing);

            float t = MuscleT(musclePercent);
            m.physicality *= 1f + MaxSwing * t;
            m.acceleration = 1f + MaxSwing * t;

            if ((traits & BodyTrait.ExcessFat) != 0)
                m.lowerBodyInjury *= InjuryRate;
            if ((traits & BodyTrait.ExcessMuscle) != 0)
            {
                m.stamina *= ExcessMuscleStamina;
                m.agility *= ExcessMuscleAgility;
                m.muscleInjury *= InjuryRate;
            }
            return m;
        }

        /// <summary>Aerial multiplier for a height: 1 at 180 cm, 0.89 at 165, 1.15 at 200.</summary>
        public static float Aerial(float heightCm)
        {
            return 1f + Mathf.Clamp(AerialPerCm * (heightCm - HeightReference), -MaxSwing, MaxSwing);
        }

        /// <summary>Speed / agility / stamina from fat: falls above the reference, rises half as fast below it.</summary>
        static float FatBig(float bodyFatPercent)
        {
            float d = bodyFatPercent - FatReference;
            return 1f + Mathf.Clamp(d > 0f ? -FatBigPerPoint * d : -FatBelowPerPoint * d, -MaxSwing, MaxSwing);
        }

        /// <summary>
        /// Where the muscle share sits, -1..1: 0 at the default body, 1 at the peak, -1 as far
        /// below the default as the peak is above it. Symmetric so one number moves the curve.
        /// Shot power reads the same position (ShotPower.MuscleBonus).
        /// </summary>
        public static float MuscleT(float musclePercent)
        {
            return Mathf.Clamp((musclePercent - MuscleReference) / (MusclePeak - MuscleReference), -1f, 1f);
        }

        static BodyTrait Toggle(BodyTrait t, BodyTrait flag, float value, float line)
        {
            bool showing = (t & flag) != 0;
            if (!showing && value >= line) return t | flag;
            if (showing && value <= line - TraitRelease) return t & ~flag;
            return t;
        }
    }
}
