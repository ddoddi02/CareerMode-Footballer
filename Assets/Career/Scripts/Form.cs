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

        /// <summary>An attribute as it plays in a match: attribute x personal form x (1 + team form), 1..100.</summary>
        public static int Apply(int attribute, float personalForm, float teamForm)
        {
            float f = Mathf.Clamp(personalForm, PersonalMin, PersonalMax);
            return Mathf.Clamp(Mathf.RoundToInt(attribute * f * (1f + teamForm)), StatScale.Min, StatScale.Max);
        }
    }
}
