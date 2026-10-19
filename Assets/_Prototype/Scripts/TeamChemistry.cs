using System.Collections.Generic;
using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// How well each pair of team-mates get on, 0..100 (2026-10-20, the user's call): a
    /// side that does not get on misplaces more passes and dribbles more, one that does
    /// passes more naturally, and a bot asked for the ball by the human gives it to him
    /// more readily the better they get on.
    ///
    /// The values are the career side's to fill - they are built up off the pitch. Until
    /// then every pair is `baseline`, and `pairs` holds any that differ. 50 is the match
    /// exactly as it played before this existed, except the call for the ball, which is
    /// new and is refused one time in five at 50. The side's average also sets its team
    /// form (Form.TeamBonus). The call table is the user's; the rest is a placeholder
    /// (PROJECT.md 3.17d).
    ///
    /// No TeamChemistry in the scene means no chemistry at all: every call is answered,
    /// no pass is any better or worse, nobody leans either way, no team form.
    /// </summary>
    public class TeamChemistry : MonoBehaviour
    {
        [System.Serializable]
        public struct Pair
        {
            public Transform a;
            public Transform b;
            [Range(0f, 100f)] public float value;
        }

        [Tooltip("Every pair not listed below. 50 = the match as it was.")]
        [Range(0f, 100f)] public float baseline = 50f;
        [Tooltip("Pairs that differ from the baseline. Order does not matter.")]
        public List<Pair> pairs = new List<Pair>();

        [System.Serializable]
        public struct Step
        {
            [Tooltip("Chemistry this step starts at.")]
            public float from;
            [Range(0f, 1f)] public float chance;
        }

        [Header("What it does")]
        [Tooltip("Chance a bot plays it to the human when he calls for it, by their chemistry - the highest step whose `from` it reaches. The user's table (2026-10-20): 100 always, 80+ 90 %, 31..79 80 %, 21..30 50 %, 11..20 30 %, 0..10 15 %.")]
        public Step[] accept =
        {
            new Step { from = 100f, chance = 1f },
            new Step { from = 80f, chance = 0.9f },
            new Step { from = 31f, chance = 0.8f },
            new Step { from = 21f, chance = 0.5f },
            new Step { from = 11f, chance = 0.3f },
            new Step { from = 0f, chance = 0.15f },
        };
        [Tooltip("Multiplies the difficulty of a pass between the two - both the sideways miss and the weight. 1 at 50.")]
        public float errorAt0 = 1.3f;
        public float errorAt100 = 0.85f;
        [Tooltip("How hard a side's average leans its man on the ball. Below 50 he runs with it more, above it he passes more; at 0 (lean 1) the forward ball a free man takes has to gain twice as much and he carries up to twice as long, at 100 half.")]
        public float leanStrength = 1f;

        public static TeamChemistry Active { get; private set; }

        void OnEnable() { Active = this; }
        void OnDisable() { if (Active == this) Active = null; }

        /// <summary>The two of them, 0..100.</summary>
        public float Between(Transform a, Transform b)
        {
            if (a == null || b == null || a == b) return baseline;
            for (int i = 0; i < pairs.Count; i++)
            {
                Pair p = pairs[i];
                if ((p.a == a && p.b == b) || (p.a == b && p.b == a)) return p.value;
            }
            return baseline;
        }

        public void Set(Transform a, Transform b, float value)
        {
            value = Mathf.Clamp(value, 0f, 100f);
            for (int i = 0; i < pairs.Count; i++)
            {
                Pair p = pairs[i];
                if ((p.a == a && p.b == b) || (p.a == b && p.b == a))
                {
                    p.value = value;
                    pairs[i] = p;
                    return;
                }
            }
            pairs.Add(new Pair { a = a, b = b, value = value });
        }

        /// <summary>Does the man on the ball give it to the man calling for it?</summary>
        public static float AcceptChance(Transform caller, Transform carrier)
        {
            if (Active == null) return 1f;
            float c = Active.Between(caller, carrier);
            float best = float.NegativeInfinity, chance = 1f;
            for (int i = 0; i < Active.accept.Length; i++)
            {
                Step s = Active.accept[i];
                if (c >= s.from && s.from > best) { best = s.from; chance = s.chance; }
            }
            return chance;
        }

        /// <summary>What a pass from one to the other does to the strike's difficulty. 1 at 50.</summary>
        public static float ErrorScale(Transform passer, Transform receiver)
        {
            if (Active == null || passer == null || receiver == null) return 1f;
            float c = Active.Between(passer, receiver);
            return c < 50f ? Mathf.Lerp(Active.errorAt0, 1f, c / 50f)
                           : Mathf.Lerp(1f, Active.errorAt100, (c - 50f) / 50f);
        }

        /// <summary>A side's average over every pair in it. 50 with no chemistry.</summary>
        public static float Mean(IList<Transform> side)
        {
            if (Active == null) return 50f;
            if (side == null) return Active.baseline;
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < side.Count; i++)
                for (int j = i + 1; j < side.Count; j++)
                {
                    if (side[i] == null || side[j] == null) continue;
                    sum += Active.Between(side[i], side[j]);
                    n++;
                }
            return n > 0 ? sum / n : Active.baseline;
        }

        /// <summary>Which way a side leans with the ball: +1 run with it, 0 as it was, -1 pass.</summary>
        public static float DribbleLean(IList<Transform> side)
        {
            if (Active == null || side == null) return 0f;
            return Mathf.Clamp((50f - Mean(side)) / 50f, -1f, 1f) * Active.leanStrength;
        }
    }
}
