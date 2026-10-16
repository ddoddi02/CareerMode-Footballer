using UnityEngine;

namespace Career
{
    /// <summary>
    /// The shot power attribute, 1..100 (user, 2026-10-14). Nobody trains it directly: it is
    /// the player's own base plus what his body adds. Finishing is a different attribute - it
    /// owns accuracy and has no say here.
    ///
    ///   shot power = min(100, base + muscle bonus + flexibility bonus) + trait points
    ///
    /// Only trait points go past 100. 100 is the user's top speed - from 40 m out, at the
    /// goal in under a second; turning the attribute into m/s belongs to the match side's
    /// rolling model (PROJECT.md 10.4).
    ///
    /// To add a factor (the user said "and so on"): a bonus function here and one more term
    /// in <see cref="Attribute"/>.
    /// </summary>
    public static class ShotPower
    {
        // User: muscle adds "10 to 20" when high, flexibility "about 10". Read as the bonus at
        // the top end - the muscle peak (BodyModel.MusclePeak) and flexibility 100 - rising in
        // a straight line from nothing at the default body. PROVISIONAL.
        public const float MuscleBonusMax = 20f;
        public const float FlexibilityBonusMax = 10f;
        public const float FlexibilityReference = 50f;

        /// <summary>
        /// Below the default body, do the bonuses go negative? The user only spoke of adding,
        /// so not yet: a weak body hits like an average one. PROVISIONAL - asked.
        /// </summary>
        public const bool PenaliseBelowAverage = false;

        /// <summary>The parts, for showing where the number comes from.</summary>
        public struct Parts
        {
            public float baseValue, muscle, flexibility, capped, total;
            /// <summary>Body traits and perks together - the only part past the cap.</summary>
            public float traits;
        }

        public static float MuscleBonus(float musclePercent)
        {
            return Bonus(BodyModel.MuscleT(musclePercent), MuscleBonusMax);
        }

        public static float FlexibilityBonus(float flexibility)
        {
            float t = (flexibility - FlexibilityReference) / (StatScale.Max - FlexibilityReference);
            return Bonus(t < -1f ? -1f : (t > 1f ? 1f : t), FlexibilityBonusMax);
        }

        /// <summary>
        /// Points the body traits add to (or take from) shot power, on top of the cap. The
        /// body traits there are today give nothing; the points come from perks picked at
        /// creation ("power shot", "exceptional ankle strength" - PerkCatalog), added in Explain.
        /// </summary>
        public static float TraitPoints(BodyTrait traits)
        {
            return 0f;
        }

        public static Parts Explain(PlayerProfile p)
        {
            Parts r;
            r.baseValue = p.shotPowerBase;
            r.muscle = MuscleBonus(p.MusclePercent);
            r.flexibility = FlexibilityBonus(p.flexibility);
            float sum = r.baseValue + r.muscle + r.flexibility;
            r.capped = sum < StatScale.Min ? StatScale.Min : (sum > StatScale.Max ? StatScale.Max : sum);
            r.traits = TraitPoints(p.traits) + PerkCatalog.ShotPowerPoints(p.perks);
            r.total = Mathf.Max(StatScale.Min, r.capped + r.traits);
            return r;
        }

        /// <summary>This player's shot power. 1..100, more only through traits.</summary>
        public static float Attribute(PlayerProfile p)
        {
            return Explain(p).total;
        }

        /// <summary>
        /// Shot power -> ball speed, the PROVISIONAL two straight lines meeting at the ordinary
        /// player (PROJECT.md 10.4). The match side's to own; here so the drill and the creation
        /// screen show the same number. Defaults: 1 -> 22.1, 60 -> 26, 100 -> 42.5 m/s, and on
        /// past 100 at the same slope.
        /// </summary>
        public static float ProvisionalSpeed(float power, float atOne = 22.1f, float atAverage = 26f,
                                             float averagePower = 60f, float atHundred = 42.5f, bool extendPastHundred = true)
        {
            if (power <= averagePower)
                return Mathf.Lerp(atOne, atAverage, Mathf.InverseLerp(StatScale.Min, averagePower, power));
            float perPoint = (atHundred - atAverage) / (StatScale.Max - averagePower);
            if (!extendPastHundred) power = Mathf.Min(power, StatScale.Max);
            return atAverage + perPoint * (power - averagePower);
        }

        static float Bonus(float t, float max)
        {
            if (t < 0f && !PenaliseBelowAverage) return 0f;
            return max * t;
        }
    }
}
