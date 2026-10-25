using System.Collections.Generic;

namespace Career
{
    /// <summary>How far a perk's effect reaches today (PROJECT.md 10.6).</summary>
    public enum PerkLink
    {
        /// <summary>Changes something already played: shot power, flexibility, finishing (shot drill), morning recovery (daily map).</summary>
        Live,
        /// <summary>Changes a BodyModifiers multiplier the match has not been wired to yet.</summary>
        MultiplierOnly,
        /// <summary>The system it would act on does not exist yet (training, diet, recovery, mental strength).</summary>
        Placeholder,
    }

    /// <summary>
    /// A perk picked at character creation, Project Zomboid style: a good one costs points,
    /// a bad one pays them back (user, 2026-10-16). Not the same thing as a BodyTrait - those
    /// come and go with the body by themselves; a perk is chosen once and stays.
    /// </summary>
    public class Perk
    {
        public string id, name, effect;
        /// <summary>Points it costs. Negative pays points back.</summary>
        public int cost;
        public PerkLink link;
        /// <summary>Perks it cannot be taken with.</summary>
        public string[] excludes = new string[0];

        // What it does. Stat deltas are added to the chosen value when the character is made.
        public int finishing, flexibility, mentalStrength;
        /// <summary>Shot power points on top of the 100 cap (ShotPower.TraitPoints's slot).</summary>
        public float shotPowerPoints;
        public float staminaMul = 1f, injuryMul = 1f;
        /// <summary>Scales the energy won back each morning (DailyLife.MorningRecovery).</summary>
        public float recoveryMul = 1f;
        /// <summary>For placeholders: what it will scale once its system exists.</summary>
        public float futureMul = 1f;

        public bool Good { get { return cost > 0; } }
    }

    /// <summary>
    /// The perks there are. One entry per perk - add one here and it shows up on the creation
    /// screen with its cost and its effect. Costs and effects are PROVISIONAL first guesses.
    /// </summary>
    public static class PerkCatalog
    {
        public static readonly Perk[] All =
        {
            // ---- good ----------------------------------------------------------------
            new Perk { id = "power_shot", name = "강슛", cost = 6, link = PerkLink.Live,
                       effect = "슛 파워 +8 (100 상한 밖)", shotPowerPoints = 8f, excludes = new[] { "weak_kick" } },
            new Perk { id = "ankle_strength", name = "특출난 발목 힘", cost = 4, link = PerkLink.Live,
                       effect = "슛 파워 +5 (100 상한 밖)", shotPowerPoints = 5f, excludes = new[] { "weak_kick" } },
            new Perk { id = "supple", name = "타고난 유연성", cost = 3, link = PerkLink.Live,
                       effect = "유연성 +15", flexibility = 15, excludes = new[] { "stiff" } },
            new Perk { id = "cool_head", name = "강심장", cost = 2, link = PerkLink.Placeholder,
                       effect = "정신력 +15 (정신력의 역할은 미정)", mentalStrength = 15, excludes = new[] { "nervous" } },
            new Perk { id = "sturdy", name = "튼튼한 몸", cost = 4, link = PerkLink.MultiplierOnly,
                       effect = "부상 빈도 ×0.7", injuryMul = 0.7f, excludes = new[] { "injury_prone" } },
            new Perk { id = "big_lungs", name = "지치지 않는 폐", cost = 4, link = PerkLink.MultiplierOnly,
                       effect = "체력 ×1.10", staminaMul = 1.10f, excludes = new[] { "tires_easily" } },
            new Perk { id = "builds_muscle", name = "근육이 잘 붙는 체질", cost = 4, link = PerkLink.Placeholder,
                       effect = "근육 성장 ×1.5 (훈련이 생기면)", futureMul = 1.5f },
            new Perk { id = "quick_recovery", name = "빠른 회복", cost = 3, link = PerkLink.Live,
                       effect = "아침 체력 회복 ×1.5", recoveryMul = 1.5f },

            // ---- bad -----------------------------------------------------------------
            new Perk { id = "weak_kick", name = "약한 킥", cost = -5, link = PerkLink.Live,
                       effect = "슛 파워 −8", shotPowerPoints = -8f, excludes = new[] { "power_shot", "ankle_strength" } },
            new Perk { id = "stiff", name = "뻣뻣한 몸", cost = -3, link = PerkLink.Live,
                       effect = "유연성 −15", flexibility = -15, excludes = new[] { "supple" } },
            new Perk { id = "nervous", name = "새가슴", cost = -2, link = PerkLink.Placeholder,
                       effect = "정신력 −15 (정신력의 역할은 미정)", mentalStrength = -15, excludes = new[] { "cool_head" } },
            new Perk { id = "injury_prone", name = "유리몸", cost = -5, link = PerkLink.MultiplierOnly,
                       effect = "부상 빈도 ×1.4", injuryMul = 1.4f, excludes = new[] { "sturdy" } },
            new Perk { id = "tires_easily", name = "쉽게 지침", cost = -4, link = PerkLink.MultiplierOnly,
                       effect = "체력 ×0.90", staminaMul = 0.90f, excludes = new[] { "big_lungs" } },
            new Perk { id = "gains_fat", name = "살이 잘 찌는 체질", cost = -3, link = PerkLink.Placeholder,
                       effect = "체지방 증가 ×1.5 (식단이 생기면)", futureMul = 1.5f },
        };

        static Dictionary<string, Perk> byId;

        public static Perk Get(string id)
        {
            if (byId == null)
            {
                byId = new Dictionary<string, Perk>();
                foreach (var p in All) byId[p.id] = p;
            }
            Perk r;
            return id != null && byId.TryGetValue(id, out r) ? r : null;
        }

        /// <summary>Is `id` ruled out by something already picked?</summary>
        public static bool Blocked(string id, ICollection<string> picked)
        {
            Perk p = Get(id);
            if (p == null) return true;
            foreach (string other in picked)
            {
                if (other == id) continue;
                Perk o = Get(other);
                if (o == null) continue;
                if (System.Array.IndexOf(p.excludes, other) >= 0 || System.Array.IndexOf(o.excludes, id) >= 0) return true;
            }
            return false;
        }

        public static float ShotPowerPoints(IEnumerable<string> perks)
        {
            float s = 0f;
            if (perks == null) return s;
            foreach (string id in perks) { Perk p = Get(id); if (p != null) s += p.shotPowerPoints; }
            return s;
        }

        public static float RecoveryMultiplier(IEnumerable<string> perks)
        {
            float m = 1f;
            if (perks == null) return m;
            foreach (string id in perks) { Perk p = Get(id); if (p != null) m *= p.recoveryMul; }
            return m;
        }

        public static void Multipliers(IEnumerable<string> perks, out float stamina, out float injury)
        {
            stamina = injury = 1f;
            if (perks == null) return;
            foreach (string id in perks)
            {
                Perk p = Get(id);
                if (p == null) continue;
                stamina *= p.staminaMul;
                injury *= p.injuryMul;
            }
        }
    }
}
