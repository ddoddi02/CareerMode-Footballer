using UnityEngine;

namespace Career
{
    /// <summary>
    /// Form, 0..100 with 50 as usual - a state that moves day by day, like fatigue. The user:
    /// a squad that gets on well plays better, so form follows team chemistry
    /// (<see cref="Squad.TeamChemistry"/>). What form does in a match is not decided. Every
    /// number here is PROVISIONAL (PROJECT.md 10.9).
    /// </summary>
    public static class FormModel
    {
        public const float Min = 0f, Max = 100f, Neutral = 50f;
        /// <summary>Share of the gap to its target that form closes in a day.</summary>
        public const float Follow = 0.1f;
        /// <summary>Most form moves in a day, either way.</summary>
        public const float MaxDailyChange = 5f;

        /// <summary>
        /// Tomorrow's form. The target is team chemistry itself, so a neutral squad (50) holds
        /// form at 50 and a close one (80) draws it up to 80 over a few weeks. Other pulls
        /// (results, fatigue) will move the target when they exist.
        /// </summary>
        public static float NextDay(float form, float teamChemistry)
        {
            float step = Mathf.Clamp(Follow * (teamChemistry - form), -MaxDailyChange, MaxDailyChange);
            return Mathf.Clamp(form + step, Min, Max);
        }
    }
}
