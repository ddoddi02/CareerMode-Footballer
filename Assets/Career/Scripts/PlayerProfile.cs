using System;
using System.Collections.Generic;
using UnityEngine;

namespace Career
{
    /// <summary>
    /// The player's off-pitch state. Only the first items are here (user, 2026-10-14):
    /// body fat, muscle mass, flexibility, mental strength and fatigue - and the two shot
    /// attributes, finishing (accuracy) and the base that shot power is built on.
    ///
    /// The body is stored as masses, not percentages: fat, muscle and the frame (everything
    /// else). Weight is their sum, so putting on fat lowers the muscle share by itself - the
    /// percentages are read off the masses. Weight and muscle are shown in kg, body fat in %;
    /// <see cref="BodyModel"/> turns the percentages into match multipliers and traits.
    ///
    /// There is no strength attribute - the user took it out of the attribute list. Muscle
    /// mass does that job. Attributes are 1..100 (<see cref="StatScale"/>). Flexibility is so
    /// far only an input to shot power; mental strength and fatigue have no role yet, so they
    /// are data only. Every range and default below is PROVISIONAL (PROJECT.md 10).
    ///
    /// Made on the character creation screen (PROJECT.md 10.6) and saved as JSON by
    /// <see cref="CharacterStore"/>; perk stat changes are already added into the fields.
    /// </summary>
    [Serializable]
    public class PlayerProfile
    {
        public const float HeightMin = 150f, HeightMax = 205f;
        // PROVISIONAL ranges.
        public const float FatKgMin = 2f, FatKgMax = 30f;
        public const float MuscleKgMin = 20f, MuscleKgMax = 65f;
        public const float FrameKgMin = 25f, FrameKgMax = 40f;
        public const float FatigueMin = 0f, FatigueMax = 100f;  // gauge, 0 = fully rested

        [Tooltip("Name, shown on screens only.")]
        public string displayName = "신인";

        [Tooltip("Age, years. Every player starts at 15 (user, 2026-10-17). Nothing reads it yet.")]
        public int age = 15;

        [Tooltip("Height now, cm. Up: better in the air; down: more agile - the losing side only on harder talents (BodyModel.HeightEffects).")]
        [Range(HeightMin, HeightMax)] public float heightCm = 180f;

        [Tooltip("Potential height, cm - the most he can grow to, never under the height now. How much of it he reaches depends on how he lives (daily system, not built yet - PROJECT.md 10.6).")]
        [Range(HeightMin, HeightMax)] public float peakHeightCm = 180f;

        [Tooltip("Talent - the difficulty, picked first at creation. Read by growth, ageing and trait systems once they exist (TalentCatalog).")]
        public TalentTier talent = TalentTier.TopFlight;

        public Appearance appearance = Appearance.Default;

        [Tooltip("Perks picked at creation (PerkCatalog ids). Their shot power points and multipliers are read live; their stat changes are already in the fields below.")]
        public List<string> perks = new List<string>();

        // Defaults make a 75 kg player at 10% fat and 48% muscle.
        [Tooltip("Fat, kg. Shown as % of weight. Speed, agility and stamina fall as the share rises; physicality gains a little. 20% or more brings out ExcessFat.")]
        [Range(FatKgMin, FatKgMax)] public float fatKg = 7.5f;

        [Tooltip("Muscle mass, kg. Its share of weight drives physicality, acceleration and shot power. Past the line (BodyModel.ExcessMuscleLine) it brings out ExcessMuscle.")]
        [Range(MuscleKgMin, MuscleKgMax)] public float muscleKg = 36f;

        [Tooltip("Everything else - bone, organs, water outside the muscle, kg. Not expected to change.")]
        [Range(FrameKgMin, FrameKgMax)] public float frameKg = 31.5f;

        [Tooltip("Flexibility, 1..100. One input to shot power (ShotPower); the rest of its role is not decided. PROVISIONAL.")]
        [Range(StatScale.Min, StatScale.Max)] public int flexibility = 50;

        [Tooltip("Finishing, 1..100 - accuracy only (the miss), not power. 60 is an ordinary player (user).")]
        [Range(StatScale.Min, StatScale.Max)] public int finishing = 60;

        [Tooltip("Shot power before the body adds to it, 1..100. Shot power itself is worked out (ShotPower.Attribute). 60 is an ordinary player - PROVISIONAL.")]
        [Range(StatScale.Min, StatScale.Max)] public int shotPowerBase = 60;

        [Tooltip("Mental strength, 1..100. PROVISIONAL - role not decided.")]
        [Range(StatScale.Min, StatScale.Max)] public int mentalStrength = 50;

        [Header("Football attributes, 1..100 (user, 2026-10-18) - chosen at creation, nothing reads them yet")]
        [Range(StatScale.Min, StatScale.Max)] public int topSpeed = 50;   // 최고속도 - no effect yet
        [Range(StatScale.Min, StatScale.Max)] public int acceleration = 50;   // 가속도 - no effect yet
        [Range(StatScale.Min, StatScale.Max)] public int stamina = 50;   // 체력 - no effect yet
        [Range(StatScale.Min, StatScale.Max)] public int positioning = 50;   // 위치선정 - no effect yet
        [Range(StatScale.Min, StatScale.Max)] public int tackling = 50;   // 태클 - no effect yet
        [Range(StatScale.Min, StatScale.Max)] public int longPassing = 50;   // 긴 패스 - no effect yet
        [Range(StatScale.Min, StatScale.Max)] public int shortPassing = 50;   // 짧은 패스 - no effect yet
        [Range(StatScale.Min, StatScale.Max)] public int crossing = 50;   // 크로스 - no effect yet
        [Range(StatScale.Min, StatScale.Max)] public int curve = 50;   // 커브 - no effect yet
        [Range(StatScale.Min, StatScale.Max)] public int jumping = 50;   // 점프 - no effect yet
        [Range(StatScale.Min, StatScale.Max)] public int aerialAbility = 50;   // 공중볼 처리 - no effect yet
        [Range(StatScale.Min, StatScale.Max)] public int ballControl = 50;   // 볼 컨트롤 - no effect yet
        [Range(StatScale.Min, StatScale.Max)] public int tricks = 50;   // 개인기 - no effect yet
        [Range(StatScale.Min, StatScale.Max)] public int dribbling = 50;   // 드리블 - no effect yet
        [Range(StatScale.Min, StatScale.Max)] public int agility = 50;   // 민첩성 - no effect yet
        [Range(StatScale.Min, StatScale.Max)] public int physicality = 50;   // 몸싸움 - no effect yet

        [Tooltip("Fatigue, 0 (rested) .. 100. PROVISIONAL - role not decided.")]
        [Range(FatigueMin, FatigueMax)] public float fatigue = 0f;

        [Tooltip("Form, 0..100, 50 = usual. Moves day by day toward team chemistry (FormModel, Squad.EndDay). PROVISIONAL - no match effect yet.")]
        [Range(FormModel.Min, FormModel.Max)] public float form = FormModel.Neutral;

        [Tooltip("Traits showing right now. Set only by BodyModel.NextTraits - it has the hysteresis.")]
        public BodyTrait traits = BodyTrait.None;

        public float WeightKg { get { return fatKg + muscleKg + frameKg; } }
        public float BodyFatPercent { get { return 100f * fatKg / WeightKg; } }
        public float MusclePercent { get { return 100f * muscleKg / WeightKg; } }
    }
}
