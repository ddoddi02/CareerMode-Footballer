using System;
using System.Collections.Generic;
using UnityEngine;

namespace Career
{
    /// <summary>
    /// The rules of character creation. Perks are Project Zomboid's (user, 2026-10-16/18): 10
    /// points for everyone, good perks spend them, bad perks add to them. Stats are not bought
    /// with points (user, 2026-10-18: "set the 1..100 stats directly; their mean is the
    /// overall"): the talent gives a stat budget - its start overall times the number of
    /// stats - and the player shares it out, so raising one stat means lowering another and the
    /// mean stays the talent's start overall. Everything here is PROVISIONAL (PROJECT.md 10.6).
    /// </summary>
    public static class CreationRules
    {
        /// <summary>Perk points, the same for every talent (user).</summary>
        public const int PerkPoints = 10;

        /// <summary>Stats move in steps of this many (less at the ends of the scale or of the budget).</summary>
        public const int StatStep = 5;

        /// <summary>The stat points a talent shares out: start overall x the number of stats.</summary>
        public static int StatBudget(TalentTier t)
        {
            return TalentCatalog.Get(t).startOverall * Stats.Length;
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
            new StatDef { id = "flexibility", name = "유연성", effect = "슛 파워·민첩·부상", link = PerkLink.Live,
                          perkDelta = p => p.flexibility, apply = (pr, v) => pr.flexibility = v, read = pr => pr.flexibility },
            new StatDef { id = "mentalStrength", name = "정신력", effect = "역할 미정", link = PerkLink.Placeholder,
                          perkDelta = p => p.mentalStrength, apply = (pr, v) => pr.mentalStrength = v, read = pr => pr.mentalStrength },

            // Football attributes (user, 2026-10-18). All [미정]: chosen and saved, nothing reads them yet.
            new StatDef { id = "topSpeed", name = "최고속도", effect = "달리기 최고 속도", link = PerkLink.Placeholder,
                          perkDelta = p => 0, apply = (pr, v) => pr.topSpeed = v, read = pr => pr.topSpeed },
            new StatDef { id = "acceleration", name = "가속도", effect = "출발·가속", link = PerkLink.Placeholder,
                          perkDelta = p => 0, apply = (pr, v) => pr.acceleration = v, read = pr => pr.acceleration },
            new StatDef { id = "stamina", name = "체력", effect = "지치는 속도", link = PerkLink.Placeholder,
                          perkDelta = p => 0, apply = (pr, v) => pr.stamina = v, read = pr => pr.stamina },
            new StatDef { id = "positioning", name = "위치선정", effect = "자리 잡기", link = PerkLink.Placeholder,
                          perkDelta = p => 0, apply = (pr, v) => pr.positioning = v, read = pr => pr.positioning },
            new StatDef { id = "tackling", name = "태클", effect = "공 뺏기", link = PerkLink.Placeholder,
                          perkDelta = p => 0, apply = (pr, v) => pr.tackling = v, read = pr => pr.tackling },
            new StatDef { id = "longPassing", name = "긴 패스", effect = "먼 거리 패스", link = PerkLink.Placeholder,
                          perkDelta = p => 0, apply = (pr, v) => pr.longPassing = v, read = pr => pr.longPassing },
            new StatDef { id = "shortPassing", name = "짧은 패스", effect = "가까운 패스", link = PerkLink.Placeholder,
                          perkDelta = p => 0, apply = (pr, v) => pr.shortPassing = v, read = pr => pr.shortPassing },
            new StatDef { id = "crossing", name = "크로스", effect = "측면에서 올리기", link = PerkLink.Placeholder,
                          perkDelta = p => 0, apply = (pr, v) => pr.crossing = v, read = pr => pr.crossing },
            new StatDef { id = "curve", name = "커브", effect = "공 휘기", link = PerkLink.Placeholder,
                          perkDelta = p => 0, apply = (pr, v) => pr.curve = v, read = pr => pr.curve },
            new StatDef { id = "jumping", name = "점프", effect = "뛰어오르기 (근육 보너스)", link = PerkLink.Placeholder,
                          perkDelta = p => 0, apply = (pr, v) => pr.jumping = v, read = pr => pr.jumping },
            new StatDef { id = "aerialAbility", name = "공중볼 처리", effect = "낙하지점 판단 (헤딩·캐칭)", link = PerkLink.Placeholder,
                          perkDelta = p => 0, apply = (pr, v) => pr.aerialAbility = v, read = pr => pr.aerialAbility },
            new StatDef { id = "ballControl", name = "볼 컨트롤", effect = "터치마다 공을 몸 가까이", link = PerkLink.Placeholder,
                          perkDelta = p => 0, apply = (pr, v) => pr.ballControl = v, read = pr => pr.ballControl },
            new StatDef { id = "tricks", name = "개인기", effect = "압박 이겨내기·1:1 돌파", link = PerkLink.Placeholder,
                          perkDelta = p => 0, apply = (pr, v) => pr.tricks = v, read = pr => pr.tricks },
            new StatDef { id = "dribbling", name = "드리블", effect = "공 있을 때 속도 손실 줄이기", link = PerkLink.Placeholder,
                          perkDelta = p => 0, apply = (pr, v) => pr.dribbling = v, read = pr => pr.dribbling },
            new StatDef { id = "agility", name = "민첩성", effect = "방향 전환·멈추기", link = PerkLink.Placeholder,
                          perkDelta = p => 0, apply = (pr, v) => pr.agility = v, read = pr => pr.agility },
            new StatDef { id = "physicality", name = "몸싸움", effect = "경합·버티기", link = PerkLink.Placeholder,
                          perkDelta = p => 0, apply = (pr, v) => pr.physicality = v, read = pr => pr.physicality },
        };

        /// <summary>
        /// Overall: the plain mean of the 1..100 stats in <see cref="Stats"/>, perks included
        /// (user: "the mean of the stats is the overall").
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

        // ---- the body: a 15-year-old, free height and weight, capped muscle ------------
        //
        // User, 2026-10-17: height and weight are free (no points), muscle has a ceiling - the
        // player starts at 15, and a 15-year-old cannot be very muscular. The ceiling is the same
        // ordinary-15-year-old one for every talent; talents differ in how fast muscle grows
        // afterwards (TalentDef.muscleGrowth). The body traits (55%) and the muscle bonus peak
        // (53%) are out of reach at creation on purpose.
        //
        // How the three masses are found (fat + muscle + frame = weight, PlayerProfile):
        //   frame  = FrameKgPerM2 x height^2       bone, organs - set by height alone
        //   muscle = weight x muscle %             chosen, up to MuscleCap
        //   fat    = weight - frame - muscle       whatever is left
        // so a heavier body at the same muscle % is a fatter one, and pays for it in speed,
        // agility and stamina (BodyModel). Weight cannot go so low that fat falls under FatFloor.

        public const int StartAge = 15;
        /// <summary>Height now: 155-180 around the 15-year-old average of 170 (user).</summary>
        public const float HeightMin = 155f, HeightMax = 180f, DefaultHeight = 170f;
        /// <summary>
        /// Potential height - the most he can grow to, not a promise (user: how much he reaches
        /// depends on his habits in the daily system). From the height now to this much above it (user: 20). Default 180.
        /// </summary>
        public const float PeakAbove = 20f, DefaultPeakHeight = 180f;
        public const float WeightMin = 45f, WeightMax = 100f, DefaultWeight = 60f;
        public const float MuscleMin = 35f, MuscleCap = 46f, DefaultMuscle = 43f;
        /// <summary>Body fat never goes under this at creation, %.</summary>
        public const float FatFloor = 5f;
        /// <summary>Frame per square metre of height: 31.5 kg at 180 cm, the adult default body's.</summary>
        public const float FrameKgPerM2 = 9.72f;

        public static float FrameKg(float heightCm)
        {
            float m = heightCm / 100f;
            return FrameKgPerM2 * m * m;
        }

        /// <summary>The lightest this height and muscle can be with fat at the floor.</summary>
        public static float MinWeight(float heightCm, float musclePercent)
        {
            return FrameKg(heightCm) / (1f - (musclePercent + FatFloor) / 100f);
        }

        /// <summary>A quick start for the sliders: a body-mass index and a muscle share. Free.</summary>
        public class BodyPreset
        {
            public string name;
            public float bmi, musclePercent;
        }

        public static readonly BodyPreset[] Presets =
        {
            new BodyPreset { name = "마른", bmi = 18.5f, musclePercent = 41f },
            new BodyPreset { name = "보통", bmi = 20.8f, musclePercent = 43f },
            new BodyPreset { name = "탄탄한", bmi = 21.0f, musclePercent = 46f },
            new BodyPreset { name = "통통한", bmi = 24.0f, musclePercent = 41f },
        };
    }

    /// <summary>A character being made: what was picked, its points, and the profile it makes.</summary>
    public class CharacterDraft
    {
        public string displayName = "신인";
        /// <summary>Chosen first, on its own step. Sets the start overall and the points (PROJECT.md 10.7); change it with SetTalent.</summary>
        public TalentTier talent = TalentTier.TopFlight;
        public float heightCm = CreationRules.DefaultHeight;
        public float peakHeightCm = CreationRules.DefaultPeakHeight;
        public float weightKg = CreationRules.DefaultWeight;
        public float musclePercent = CreationRules.DefaultMuscle;
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

        public int StartingPoints { get { return CreationRules.PerkPoints; } }

        public int StatBudget { get { return CreationRules.StatBudget(talent); } }

        /// <summary>Stat points not yet given to a stat. Zero when the stats are all shared out.</summary>
        public int StatLeft
        {
            get
            {
                int s = 0;
                for (int i = 0; i < stats.Length; i++) s += stats[i];
                return StatBudget - s;
            }
        }

        public float Overall
        {
            get
            {
                float s = 0f;
                for (int i = 0; i < stats.Length; i++) s += FinalStat(i);
                return s / stats.Length;
            }
        }

        // ---- the body (CreationRules: free height and weight, capped muscle) --------------

        public float MinWeight { get { return Mathf.Max(CreationRules.WeightMin, CreationRules.MinWeight(heightCm, musclePercent)); } }

        public void SetHeight(float h) { heightCm = h; Normalize(); }
        public void SetPeakHeight(float h) { peakHeightCm = h; Normalize(); }
        public void SetWeight(float w) { weightKg = w; appearance.build = -1; Normalize(); }
        public void SetMuscle(float m) { musclePercent = m; appearance.build = -1; Normalize(); }

        public void ApplyPreset(int i)
        {
            CreationRules.BodyPreset b = CreationRules.Presets[Mathf.Clamp(i, 0, CreationRules.Presets.Length - 1)];
            float h = heightCm / 100f;
            musclePercent = b.musclePercent;
            weightKg = b.bmi * h * h;
            appearance.build = i;
            Normalize();
        }

        /// <summary>Keep the body possible: height and muscle in range, and weight heavy enough to leave fat at the floor.</summary>
        void Normalize()
        {
            heightCm = Mathf.Clamp(heightCm, CreationRules.HeightMin, CreationRules.HeightMax);
            peakHeightCm = Mathf.Clamp(peakHeightCm, heightCm, heightCm + CreationRules.PeakAbove);     // now .. now + 20
            musclePercent = Mathf.Clamp(musclePercent, CreationRules.MuscleMin, CreationRules.MuscleCap);
            weightKg = Mathf.Clamp(weightKg, MinWeight, CreationRules.WeightMax);
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

        /// <summary>Perk points left. Below zero, the character cannot be confirmed.</summary>
        public int Remaining
        {
            get { return StartingPoints - PerkSpend; }
        }

        /// <summary>Perk points not below zero, and every stat point given out.</summary>
        public bool CanConfirm
        {
            get { return Remaining >= 0 && StatLeft == 0; }
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

        /// <summary>
        /// Move a stat a step. Up takes from the unspent budget (so another stat has to come down
        /// first); down gives back to it. Never past 1..100.
        /// </summary>
        public bool StepStat(int i, int dir)
        {
            int by = dir > 0
                ? Mathf.Min(CreationRules.StatStep, StatLeft, StatScale.Max - stats[i])
                : Mathf.Min(CreationRules.StatStep, stats[i] - StatScale.Min);
            if (by <= 0) return false;
            stats[i] += dir > 0 ? by : -by;
            return true;
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
            p.age = CreationRules.StartAge;
            p.heightCm = heightCm;
            p.peakHeightCm = peakHeightCm;
            p.appearance = appearance;

            p.frameKg = CreationRules.FrameKg(heightCm);
            p.muscleKg = weightKg * musclePercent / 100f;
            p.fatKg = weightKg - p.frameKg - p.muscleKg;

            for (int i = 0; i < stats.Length; i++) CreationRules.Stats[i].apply(p, FinalStat(i));
            p.perks = new List<string>(perks);
            p.traits = BodyModel.NextTraits(BodyTrait.None, p.BodyFatPercent, p.MusclePercent);
            return p;
        }
    }
}
