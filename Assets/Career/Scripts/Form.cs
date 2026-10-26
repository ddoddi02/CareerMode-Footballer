using System.Collections.Generic;
using UnityEngine;

namespace Career
{
    /// <summary>
    /// Form (user, 2026-10-20): how much of a player's core attributes he gets out on the day.
    /// Two parts that apply separately - his own form, and the team's, which comes from team
    /// chemistry in steps. In a match: attribute x personal form x (1 + team form), cut to
    /// 1..100. The match side does that sum itself with these numbers (PROJECT.md 10.9).
    /// </summary>
    public static class FormModel
    {
        /// <summary>Personal form, as a share of the attribute: 60% .. 120%, 100% as usual. What moves it is not decided.</summary>
        public const float PersonalMin = 0.6f, PersonalMax = 1.2f, PersonalNeutral = 1f;

        /// <summary>
        /// Team form from team chemistry (<see cref="Squad.TeamChemistry"/>), added to 1:
        /// 90+ +5%, 70..89 +3%, 31..69 nothing, 11..30 -5%, 0..10 -10% (user).
        /// </summary>
        public static float TeamForm(float teamChemistry)
        {
            if (teamChemistry >= 90f) return 0.05f;
            if (teamChemistry >= 70f) return 0.03f;
            if (teamChemistry > 30f) return 0f;
            if (teamChemistry > 10f) return -0.05f;
            return -0.10f;
        }

        /// <summary>
        /// What moves personal form (user, 2026-10-26: fatigue, mental strength, recent results,
        /// chemistry). The numbers are PROVISIONAL (PROJECT.md 10.15): from 1, fatigue takes up to
        /// <see cref="FatigueWeight"/> off, each of the last three results adds or takes
        /// <see cref="ResultWeight"/> (win / loss), and chemistry with the teammates adds up to
        /// <see cref="ChemistryWeight"/> above 50 and takes as much below. Mental strength does not
        /// move it itself but how far the rest does: x1.3 at 1, x0.7 at 100 - a steady head swings less
        /// either way. Kept inside 0.6..1.2.
        /// </summary>
        public const float FatigueWeight = 0.25f, ResultWeight = 0.03f, ChemistryWeight = 0.08f;
        public const float ShakeAtLowMental = 1.3f, ShakeAtHighMental = 0.7f;
        public const int ResultsKept = 3;

        /// <param name="fatigue">0 (rested) .. 100.</param>
        /// <param name="mentalStrength">1..100.</param>
        /// <param name="recentResults">Last results, +1 win, 0 draw, -1 loss; only the last <see cref="ResultsKept"/> count.</param>
        /// <param name="chemistry">The player's chemistry with his teammates, 0..100.</param>
        public static float Personal(float fatigue, int mentalStrength, IList<int> recentResults, float chemistry)
        {
            float dev = -FatigueWeight * Mathf.Clamp01(fatigue / 100f);
            if (recentResults != null)
                for (int i = Mathf.Max(0, recentResults.Count - ResultsKept); i < recentResults.Count; i++)
                    dev += ResultWeight * Mathf.Clamp(recentResults[i], -1, 1);
            dev += ChemistryWeight * Mathf.Clamp((chemistry - 50f) / 50f, -1f, 1f);
            float shake = Mathf.Lerp(ShakeAtLowMental, ShakeAtHighMental, StatScale.To01(mentalStrength));
            return Mathf.Clamp(PersonalNeutral + dev * shake, PersonalMin, PersonalMax);
        }

        /// <summary>An attribute as it plays in a match: attribute x personal form x (1 + team form), 1..100.</summary>
        public static int Apply(int attribute, float personalForm, float teamForm)
        {
            float f = Mathf.Clamp(personalForm, PersonalMin, PersonalMax);
            return Mathf.Clamp(Mathf.RoundToInt(attribute * f * (1f + teamForm)), StatScale.Min, StatScale.Max);
        }
    }
}
