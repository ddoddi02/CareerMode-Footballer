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

        // ---- height: up for balls in the air, down for agility (user, 2026-10-17) --------
        // Against the average 15-year-old boy (user: "the 15-year-old average"), about 170 cm -
        // a round figure, to be checked against the KDCA 2017 growth chart's table. The other
        // body multipliers stay against an adult pro (muscle 48% = 1).
        public const float HeightReference = 170f;
        /// <summary>+0.75% a centimetre above 170, -0.75% below.</summary>
        public const float AerialPerCm = 0.0075f;
        /// <summary>The same slope the other way: -0.75% a centimetre above 170, +0.75% below.</summary>
        public const float AgilityPerCm = 0.0075f;

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
            // A young body short of an adult's muscle: the talent decides how much of that
            // shows in acceleration (TalentDef.youthAccelPenalty). Physicality takes it all.
            // Everyone is 15 for now; when ageing exists this may want to fade with age.
            if (m.acceleration < 1f)
                m.acceleration = 1f - (1f - m.acceleration) * TalentCatalog.Get(p.talent).youthAccelPenalty;
            float aerial, agility;
            HeightEffects(p.heightCm, p.talent, out aerial, out agility);
            m.aerial = aerial;
            m.agility *= agility * FlexAgility(p.flexibility);     // on top of what fat (and too much muscle) do to it
            m.lowerBodyInjury *= FlexInjury(p.flexibility);
            m.muscleInjury *= FlexInjury(p.flexibility);
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

        // ---- body effects as points (user, 2026-10-18) -----------------------------------
        // The body does not multiply the stats it touches; it adds to them, the way shot power
        // is base + body bonus. One percent of the old multiplier is one point on the 1..100
        // scale, so the +-15% band is +-15 points. The sum stops at 100 like shot power's (only
        // perks and traits go past it) and at 1 at the bottom. Nothing reads these in a match yet.

        /// <summary>Points the body adds to the stats it touches. Negative takes away.</summary>
        public struct BodyPoints
        {
            public int topSpeed, acceleration, stamina, agility, physicality, jumping;
        }

        public static int ToPoints(float multiplier)
        {
            return Mathf.RoundToInt((multiplier - 1f) * 100f);
        }

        public static BodyPoints Points(PlayerProfile p)
        {
            BodyModifiers m = Evaluate(p);
            BodyPoints r;
            r.topSpeed = ToPoints(m.speed);
            r.acceleration = ToPoints(m.acceleration);
            r.stamina = ToPoints(m.stamina);
            r.agility = ToPoints(m.agility);
            r.physicality = ToPoints(m.physicality);
            // Jumping goes with muscle (user), not height: the same muscle curve as acceleration,
            // without the young-acceleration softening.
            r.jumping = ToPoints(1f + MaxSwing * MuscleT(p.MusclePercent));
            return r;
        }

        /// <summary>A stat with the body's points on it: 1..100.</summary>
        public static int WithBody(int stat, int points)
        {
            return Mathf.Clamp(stat + points, StatScale.Min, StatScale.Max);
        }

        /// <summary>Aerial multiplier for a height: 1 at 170 cm, 0.89 at 155, 1.075 at 180.</summary>
        public static float Aerial(float heightCm)
        {
            return 1f + Mathf.Clamp(AerialPerCm * (heightCm - HeightReference), -MaxSwing, MaxSwing);
        }

        /// <summary>
        /// What the height now does, for this talent. On the easy talents growing tall costs
        /// nothing (TalentDef.heightDownsides): agility does not fall under 1. Being short still
        /// costs in the air on every talent (user, 2026-10-18).
        /// </summary>
        public static void HeightEffects(float heightCm, TalentTier talent, out float aerial, out float agility)
        {
            aerial = Aerial(heightCm);
            agility = HeightAgility(heightCm);
            if (!TalentCatalog.Get(talent).heightDownsides) agility = Mathf.Max(1f, agility);
        }

        // ---- flexibility: agility and injuries (user, 2026-10-18; shot power is ShotPower's) --
        /// <summary>Fixed, not the talent's start: the same flexibility does the same on every talent. The shot power bonus's zero, the same number.</summary>
        public const float FlexibilityReference = ShotPower.FlexibilityReference;
        /// <summary>Agility at flexibility 100 (and the mirror at 1): +-8%. A help, not the main thing - fat and height are.</summary>
        public const float FlexAgilitySwing = 0.08f;
        /// <summary>Injuries at flexibility 100: x0.85; at 1: x1.15. Suppleness is what keeps muscles from tearing.</summary>
        public const float FlexInjurySwing = 0.15f;

        static float FlexT(float flexibility)
        {
            return Mathf.Clamp((flexibility - FlexibilityReference) / (StatScale.Max - FlexibilityReference), -1f, 1f);
        }

        public static float FlexAgility(float flexibility) { return 1f + FlexAgilitySwing * FlexT(flexibility); }
        public static float FlexInjury(float flexibility) { return 1f - FlexInjurySwing * FlexT(flexibility); }

        /// <summary>Agility from height alone: 1 at 170 cm, 1.11 at 155, 0.925 at 180.</summary>
        public static float HeightAgility(float heightCm)
        {
            return 1f + Mathf.Clamp(-AgilityPerCm * (heightCm - HeightReference), -MaxSwing, MaxSwing);
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
