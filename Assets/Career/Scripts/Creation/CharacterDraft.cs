using System;
using System.Collections.Generic;
using UnityEngine;

namespace Career
{
    /// <summary>
    /// The point rules of character creation (user, 2026-10-16: Project Zomboid style - points
    /// buy good perks, bad perks pay points back, and that is how the character is balanced).
    /// Everything here is a PROVISIONAL first guess (PROJECT.md 10.6).
    /// </summary>
    public static class CreationRules
    {
        /// <summary>
        /// Points come from the talent (TalentDef.perkPoints: 24 / 20 / 16 / 12 - all above the old
        /// 10, and more the easier). False gives every talent FlatPoints instead.
        /// </summary>
        public static bool PointsByTalent = true;
        public const int FlatPoints = 20;

        public static int StartingPoints(TalentTier t)
        {
            return PointsByTalent ? TalentCatalog.Get(t).perkPoints : FlatPoints;
        }

        /// <summary>
        /// One pool for stats, build and perks (true), or a separate stat budget (false). One
        /// pool is the closer reading of "balance it with points": a point can go into finishing
        /// or into a perk, and the player weighs the two.
        /// </summary>
        public static bool SharedPool = true;
        /// <summary>The stat budget when the pool is not shared. Perks and build use StartingPoints.</summary>
        public const int StatBudget = 8;

        /// <summary>Stats move in steps of this many.</summary>
        public const int StatStep = 5;
        /// <summary>The first this-many points above ordinary cost 1 per step; past it, 2.</summary>
        public const int CheapAbove = 20;
        /// <summary>Lowering a stat pays 1 per step back, down to this far under ordinary; lower still pays nothing.</summary>
        public const int RefundBelow = 20;

        /// <summary>
        /// Points a stat at `value` costs against `ordinary` - the talent's start overall, where
        /// every stat begins. Negative pays back.
        /// </summary>
        public static int StatCost(int value, int ordinary)
        {
            int d = value - ordinary;
            if (d >= 0)
            {
                int cheap = Mathf.Min(d, CheapAbove), dear = Mathf.Max(0, d - CheapAbove);
                return cheap / StatStep + 2 * (dear / StatStep);
            }
            return -(Mathf.Min(-d, RefundBelow) / StatStep);
        }

        // ---- stats on the screen -------------------------------------------------------

        public class StatDef
        {
            public string id, name, effect;
            public PerkLink link;
            public Func<Perk, int> perkDelta;
            public Action<PlayerProfile, int> apply;
            public Func<PlayerProfile, int> read;
        }

        /// <summary>
        /// The stats chosen at creation. One entry per stat - the 1..100 list the user has not
        /// settled yet goes in here. Fatigue is a daily gauge, not chosen; shot power is
        /// worked out, so only its base is chosen and the result is shown.
        /// </summary>
        public static readonly StatDef[] Stats =
        {
            new StatDef { id = "finishing", name = "마무리", effect = "슛 정확도", link = PerkLink.Live,
                          perkDelta = p => p.finishing, apply = (pr, v) => pr.finishing = v, read = pr => pr.finishing },
            new StatDef { id = "shotPowerBase", name = "슛 파워 기본값", effect = "슛 세기 (몸 보너스 전)", link = PerkLink.Live,
                          perkDelta = p => 0, apply = (pr, v) => pr.shotPowerBase = v, read = pr => pr.shotPowerBase },
            new StatDef { id = "flexibility", name = "유연성", effect = "슛 파워 보너스", link = PerkLink.Live,
                          perkDelta = p => p.flexibility, apply = (pr, v) => pr.flexibility = v, read = pr => pr.flexibility },
            new StatDef { id = "mentalStrength", name = "정신력", effect = "역할 미정", link = PerkLink.Placeholder,
                          perkDelta = p => p.mentalStrength, apply = (pr, v) => pr.mentalStrength = v, read = pr => pr.mentalStrength },
        };

        /// <summary>
        /// Overall: the plain mean of the 1..100 stats in <see cref="Stats"/>, perks included.
        /// Not the body (its multipliers are a different kind of number, and the build has its
        /// own cost), and not worked-out shot power (it goes past 100 with perks and would count
        /// muscle twice). Every stat weighs the same until the user says otherwise.
        /// </summary>
        public static float Overall(PlayerProfile p)
        {
            float s = 0f;
            foreach (StatDef d in Stats) s += d.read(p);
            return s / Stats.Length;
        }

        // ---- builds: look and ability at once ----------------------------------------

        public class Build
        {
            public string name;
            public float weightAt180, fatPercent, musclePercent;
            public int cost;
            public float width;     // avatar only
        }

        /// <summary>
        /// Body builds. They are appearance and ability both - the body model reads the masses -
        /// but they are free (user, 2026-10-17: "pick the build as you like"). The cost field is
        /// kept at 0 so a price could come back in one place. Weights are for 180 cm and scale
        /// with height squared.
        /// </summary>
        public static readonly Build[] Builds =
        {
            new Build { name = "마른 체형", weightAt180 = 70f, fatPercent = 7f, musclePercent = 45f, cost = 0, width = 0.88f },
            new Build { name = "보통", weightAt180 = 75f, fatPercent = 10f, musclePercent = 48f, cost = 0, width = 1.00f },
            new Build { name = "근육형", weightAt180 = 80f, fatPercent = 8f, musclePercent = 52f, cost = 0, width = 1.10f },
            new Build { name = "육중한 체형", weightAt180 = 85f, fatPercent = 17f, musclePercent = 46f, cost = 0, width = 1.18f },
        };
    }

    /// <summary>A character being made: what was picked, its points, and the profile it makes.</summary>
    public class CharacterDraft
    {
        public string displayName = "신인";
        /// <summary>Chosen first, on its own step. Sets the start overall and the points (PROJECT.md 10.7); change it with SetTalent.</summary>
        public TalentTier talent = TalentTier.TopFlight;
        public float heightCm = 180f;
        public Appearance appearance = Appearance.Default;
        public readonly int[] stats;
        public readonly List<string> perks = new List<string>();

        public CharacterDraft()
        {
            stats = new int[CreationRules.Stats.Length];
            ResetStats();
        }

        /// <summary>Where every stat starts and costs nothing: the talent's start overall.</summary>
        public int Ordinary { get { return TalentCatalog.Get(talent).startOverall; } }

        public void ResetStats()
        {
            for (int i = 0; i < stats.Length; i++) stats[i] = Ordinary;
        }

        /// <summary>
        /// Change talent. The stats move with the start overall, so what was bought on top of it
        /// stays bought - +10 finishing on a 1부리거 is +10 on a 둔재 too.
        /// </summary>
        public void SetTalent(TalentTier t)
        {
            int shift = TalentCatalog.Get(t).startOverall - Ordinary;
            talent = t;
            for (int i = 0; i < stats.Length; i++) stats[i] = Mathf.Clamp(stats[i] + shift, StatScale.Min, StatScale.Max);
        }

        public int StartingPoints { get { return CreationRules.StartingPoints(talent); } }

        public float Overall
        {
            get
            {
                float s = 0f;
                for (int i = 0; i < stats.Length; i++) s += FinalStat(i);
                return s / stats.Length;
            }
        }

        public CreationRules.Build Build
        {
            get { return CreationRules.Builds[Mathf.Clamp(appearance.build, 0, CreationRules.Builds.Length - 1)]; }
        }

        public int StatSpend
        {
            get
            {
                int s = 0;
                for (int i = 0; i < stats.Length; i++) s += CreationRules.StatCost(stats[i], Ordinary);
                return s;
            }
        }

        public int PerkSpend
        {
            get
            {
                int s = 0;
                foreach (string id in perks) { Perk p = PerkCatalog.Get(id); if (p != null) s += p.cost; }
                return s;
            }
        }

        /// <summary>Points left in the main pool (stats too, when the pool is shared).</summary>
        public int Remaining
        {
            get { return StartingPoints - PerkSpend - Build.cost - (CreationRules.SharedPool ? StatSpend : 0); }
        }

        /// <summary>Stat points left when the pool is not shared.</summary>
        public int StatRemaining { get { return CreationRules.StatBudget - StatSpend; } }

        public bool CanConfirm
        {
            get { return Remaining >= 0 && (CreationRules.SharedPool || StatRemaining >= 0); }
        }

        /// <summary>What perks add to stat `i`.</summary>
        public int PerkDelta(int i)
        {
            int d = 0;
            foreach (string id in perks) { Perk p = PerkCatalog.Get(id); if (p != null) d += CreationRules.Stats[i].perkDelta(p); }
            return d;
        }

        public int FinalStat(int i)
        {
            return Mathf.Clamp(stats[i] + PerkDelta(i), StatScale.Min, StatScale.Max);
        }

        public void StepStat(int i, int dir)
        {
            stats[i] = Mathf.Clamp(stats[i] + dir * CreationRules.StatStep, StatScale.Min, StatScale.Max);
        }

        /// <summary>Pick or drop a perk. A perk ruled out by one already picked is not taken.</summary>
        public bool TogglePerk(string id)
        {
            if (perks.Remove(id)) return true;
            if (PerkCatalog.Blocked(id, perks)) return false;
            perks.Add(id);
            return true;
        }

        public PlayerProfile ToProfile()
        {
            var p = new PlayerProfile();
            p.displayName = string.IsNullOrEmpty(displayName) ? "신인" : displayName;
            p.talent = talent;
            p.heightCm = Mathf.Clamp(heightCm, PlayerProfile.HeightMin, PlayerProfile.HeightMax);
            p.appearance = appearance;

            CreationRules.Build b = Build;
            float h = p.heightCm / 180f;
            float w = b.weightAt180 * h * h;
            p.fatKg = w * b.fatPercent / 100f;
            p.muscleKg = w * b.musclePercent / 100f;
            p.frameKg = w - p.fatKg - p.muscleKg;

            for (int i = 0; i < stats.Length; i++) CreationRules.Stats[i].apply(p, FinalStat(i));
            p.perks = new List<string>(perks);
            p.traits = BodyModel.NextTraits(BodyTrait.None, p.BodyFatPercent, p.MusclePercent);
            return p;
        }
    }
}
