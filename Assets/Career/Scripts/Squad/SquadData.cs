using System;
using System.Collections.Generic;
using UnityEngine;

namespace Career
{
    /// <summary>
    /// Where a squad's stats come from (user, 2026-10-26): each club has an average level by its
    /// place in the table, and each position has its main stats - so the stats of a player are not
    /// all the same average: the ones his position needs are high, the ones it needs less are low,
    /// and the whole still averages the club's level. The weights and the numbers are PROVISIONAL
    /// (PROJECT.md 10.15); this is the one place they live.
    /// </summary>
    public static class SquadData
    {
        /// <summary>How far a stat of weight 1 sits above (or -1 below) the club's level, in points.</summary>
        public const float Spread = 18f;
        /// <summary>How far a single stat strays from its weight at random, either way, so two players of a position differ.</summary>
        public const float Jitter = 3f;

        /// <summary>Position families, by slot label (<c>Formation.F4123</c>).</summary>
        public static string Family(string slot)
        {
            switch (slot)
            {
                case "GK": return "GK";
                case "LCB": case "RCB": return "CB";
                case "LB": case "RB": return "FB";
                case "DM": return "DM";
                case "LCM": case "RCM": return "CM";
                case "LW": case "RW": return "W";
                default: return "ST";
            }
        }

        /// <summary>
        /// What each position needs, -1 (matters least) .. +1 (matters most), by stat id
        /// (<c>CreationRules.Stats</c>); a stat left out is 0. Rows are made to average 0 over the
        /// stats (<see cref="Weights"/>), so a club's level is kept whatever the position.
        /// The goalkeeper has a row of his own: there are no keeper stats yet, so it is the
        /// same stats with a keeper's emphasis.
        /// </summary>
        static readonly Dictionary<string, Dictionary<string, float>> Raw = new Dictionary<string, Dictionary<string, float>>
        {
            { "CB", Row("tackling", 1f, "physicality", 1f, "aerialAbility", 1f, "jumping", 0.7f, "positioning", 0.7f, "mentalStrength", 0.4f, "topSpeed", 0.1f,
                        "dribbling", -1f, "tricks", -1f, "finishing", -0.8f, "curve", -0.7f, "crossing", -0.6f, "shotPowerBase", -0.5f, "ballControl", -0.3f) },
            { "FB", Row("stamina", 0.8f, "crossing", 0.8f, "topSpeed", 0.7f, "acceleration", 0.6f, "tackling", 0.6f, "positioning", 0.3f,
                        "finishing", -0.7f, "shotPowerBase", -0.5f, "aerialAbility", -0.4f, "tricks", -0.3f, "curve", -0.3f) },
            { "DM", Row("tackling", 0.9f, "positioning", 0.8f, "shortPassing", 0.7f, "stamina", 0.6f, "longPassing", 0.5f, "mentalStrength", 0.5f, "physicality", 0.5f,
                        "finishing", -0.8f, "crossing", -0.5f, "curve", -0.5f, "tricks", -0.4f, "topSpeed", -0.2f) },
            { "CM", Row("shortPassing", 0.9f, "longPassing", 0.7f, "ballControl", 0.7f, "stamina", 0.7f, "positioning", 0.4f, "dribbling", 0.3f,
                        "aerialAbility", -0.4f, "finishing", -0.3f, "physicality", -0.2f) },
            { "W", Row("topSpeed", 0.9f, "acceleration", 0.9f, "dribbling", 0.9f, "crossing", 0.7f, "agility", 0.7f, "tricks", 0.6f, "ballControl", 0.5f, "curve", 0.3f, "finishing", 0.3f,
                       "tackling", -1f, "aerialAbility", -0.8f, "physicality", -0.5f, "jumping", -0.4f) },
            { "ST", Row("finishing", 1f, "shotPowerBase", 0.9f, "positioning", 0.9f, "physicality", 0.5f, "jumping", 0.4f, "aerialAbility", 0.4f, "acceleration", 0.4f, "ballControl", 0.4f, "topSpeed", 0.3f,
                        "tackling", -1f, "longPassing", -0.6f, "crossing", -0.5f, "stamina", -0.2f) },
            { "GK", Row("aerialAbility", 1f, "agility", 0.9f, "positioning", 0.9f, "jumping", 0.8f, "mentalStrength", 0.7f, "flexibility", 0.6f, "longPassing", 0.3f, "physicality", 0.2f,
                        "dribbling", -0.9f, "tricks", -0.9f, "tackling", -0.8f, "crossing", -0.7f, "curve", -0.7f, "topSpeed", -0.5f, "shotPowerBase", -0.5f, "acceleration", -0.4f, "stamina", -0.3f) },
        };

        static Dictionary<string, float> Row(params object[] pairs)
        {
            var d = new Dictionary<string, float>();
            for (int i = 0; i < pairs.Length; i += 2) d[(string)pairs[i]] = (float)pairs[i + 1];
            return d;
        }

        static readonly Dictionary<string, float[]> normalised = new Dictionary<string, float[]>();

        /// <summary>The weights of a position, in the order of <c>CreationRules.Stats</c>, with their mean taken off.</summary>
        public static float[] Weights(string slot)
        {
            string family = Family(slot);
            float[] w;
            if (normalised.TryGetValue(family, out w)) return w;
            w = new float[CreationRules.Stats.Length];
            float sum = 0f;
            for (int i = 0; i < w.Length; i++)
            {
                float v;
                Raw[family].TryGetValue(CreationRules.Stats[i].id, out v);
                w[i] = v;
                sum += v;
            }
            float mean = sum / w.Length;
            for (int i = 0; i < w.Length; i++) w[i] -= mean;
            normalised[family] = w;
            return w;
        }

        /// <summary>The stats of one player of this position at a club whose level is <paramref name="level"/>: level + weight x spread + a little at random, 1..100.</summary>
        public static Dictionary<string, int> Generate(string slot, float level, System.Random rng)
        {
            float[] w = Weights(slot);
            var stats = new Dictionary<string, int>();
            for (int i = 0; i < w.Length; i++)
            {
                float noise = ((float)rng.NextDouble() * 2f - 1f) * Jitter;
                stats[CreationRules.Stats[i].id] = Mathf.Clamp(Mathf.RoundToInt(level + Spread * w[i] + noise), StatScale.Min, StatScale.Max);
            }
            return stats;
        }

        /// <summary>The plain mean of a player's stats - the same "overall" the creation screen shows.</summary>
        public static float Overall(Dictionary<string, int> stats)
        {
            float s = 0f;
            foreach (int v in stats.Values) s += v;
            return stats.Count == 0 ? 0f : s / stats.Count;
        }

        /// <summary>
        /// The human's stats for a match: the profile's, with the body's points on the ones it
        /// touches (<see cref="BodyModel.Points"/>), shot power as it is worked out (muscle,
        /// flexibility, perks - cut to 100), 1..100.
        /// </summary>
        public static Dictionary<string, int> PlayerStats(PlayerProfile p)
        {
            BodyModel.BodyPoints bp = BodyModel.Points(p);
            var stats = new Dictionary<string, int>();
            foreach (CreationRules.StatDef d in CreationRules.Stats)
            {
                int v = d.read(p);
                switch (d.id)
                {
                    case "topSpeed": v = BodyModel.WithBody(v, bp.topSpeed); break;
                    case "acceleration": v = BodyModel.WithBody(v, bp.acceleration); break;
                    case "stamina": v = BodyModel.WithBody(v, bp.stamina); break;
                    case "agility": v = BodyModel.WithBody(v, bp.agility); break;
                    case "physicality": v = BodyModel.WithBody(v, bp.physicality); break;
                    case "jumping": v = BodyModel.WithBody(v, bp.jumping); break;
                    case "shotPowerBase": v = Mathf.Clamp(Mathf.RoundToInt(ShotPower.Attribute(p)), StatScale.Min, StatScale.Max); break;
                }
                stats[d.id] = v;
            }
            return stats;
        }

        /// <summary>A number that is the same on every run and machine (string.GetHashCode is not): what seeds a club's squad.</summary>
        public static int StableHash(string s)
        {
            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < s.Length; i++) h = (h ^ s[i]) * 16777619u;
                return (int)(h & 0x7fffffff);
            }
        }
    }
}
