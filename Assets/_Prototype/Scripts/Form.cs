using System.Collections.Generic;
using UnityEngine;

namespace Prototype
{
    /// <summary>A body that carries an individual form, 60..120 %.</summary>
    public interface IForm
    {
        int FormPercent { get; }
    }

    /// <summary>
    /// How much of a player's ability shows up in this match (2026-10-20, the user's call):
    /// his own form and his side's, multiplied in separately.
    ///
    ///   used = stat x personal x (1 + team), cut to 1..100
    ///
    /// Personal is 60..120 % - an inspector value on each body for now (IForm), 100 by
    /// default; what moves it is the career side's to decide. Team is a step on the side's
    /// average chemistry (TeamChemistry.Mean): 90+ +5 %, 70..89 +3 %, 31..69 0, 11..30 -5 %,
    /// 0..10 -10 %. Every number is a placeholder (PROJECT.md 3.17d).
    ///
    /// Applied where an ability is USED - the strike, the shielding - not where somebody
    /// else judges it: the pass selector still ranks a team-mate by his stat as written.
    /// </summary>
    public static class Form
    {
        public const int MinPercent = 60, MaxPercent = 120;

        /// <summary>His own form, 0.6..1.2. 1 for a body that carries none.</summary>
        public static float Personal(Transform body)
        {
            IForm f = body != null ? body.GetComponent<IForm>() : null;
            return f != null ? Mathf.Clamp(f.FormPercent, MinPercent, MaxPercent) / 100f : 1f;
        }

        /// <summary>The side's form from its average chemistry, -0.10..+0.05.</summary>
        public static float TeamBonus(float meanChemistry)
        {
            if (meanChemistry >= 90f) return 0.05f;
            if (meanChemistry >= 70f) return 0.03f;
            if (meanChemistry > 30f) return 0f;
            if (meanChemistry > 10f) return -0.05f;
            return -0.10f;
        }

        /// <summary>What his ability is multiplied by in this match.</summary>
        public static float Scale(Transform body, IList<Transform> side)
        {
            return Personal(body) * (1f + TeamBonus(TeamChemistry.Mean(side)));
        }

        /// <summary>A 1..100 attribute as it plays today.</summary>
        public static float Stat(float stat, Transform body, IList<Transform> side)
        {
            return Career.StatScale.Clamp(stat * Scale(body, side));
        }

        /// <summary>The same for an attribute already on the 0..1 scale the kick model uses.</summary>
        public static float Stat01(float stat01, Transform body, IList<Transform> side)
        {
            return Career.StatScale.To01(Stat(Career.StatScale.From01(stat01), body, side));
        }
    }
}
