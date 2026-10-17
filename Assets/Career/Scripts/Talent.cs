namespace Career
{
    /// <summary>
    /// Talent - the difficulty, picked before anything else at character creation (user,
    /// 2026-10-17). It sets where the player starts - his overall and the perk points he gets -
    /// and how his career goes: growth, picking up good traits along the way, ageing, what a
    /// spell without training costs, and how much a match teaches. The career systems do not
    /// exist yet, so those numbers wait in the table for them; the start is live now.
    /// </summary>
    public enum TalentTier
    {
        GreatestEver,   // 인류 역사상 최고 - very easy
        WorldClass,     // 월드클래스 - easy
        TopFlight,      // 1부리거 - normal
        Dullard,        // 둔재 - hard
    }

    /// <summary>How growing taller (or not) turns into choices over the career (user, 2026-10-17).</summary>
    public enum HeightEvents
    {
        /// <summary>No events: the body grows and that is all.</summary>
        None,
        /// <summary>Now and then - a big spurt - the player picks how his play changes.</summary>
        Sometimes,
        /// <summary>Every growth step asks the player how his play changes.</summary>
        Always,
    }

    public class TalentDef
    {
        public TalentTier tier;
        public string name, difficulty, blurb;

        /// <summary>
        /// The overall he starts at: every creation stat begins here, and here is where it costs
        /// nothing (CreationRules.Overall). User: 75 / 65 / 55 / 45, ten apart. Times the number
        /// of stats it is also the stat budget the player shares out (CreationRules.StatBudget).
        /// </summary>
        public int startOverall;

        /// <summary>All experience that turns into stats, against an ordinary pro (1).</summary>
        public float growthXp;
        /// <summary>The odds of picking up a good trait during the career, against an ordinary pro (1).</summary>
        public float goodTraitChance;
        /// <summary>How fast stats fall with age, against an ordinary pro (1). 0 would be never.</summary>
        public float ageDecline;
        /// <summary>How fast stats fall on days without training, against an ordinary pro (1). 0 = they do not.</summary>
        public float idleDecline;
        /// <summary>What playing a match teaches, as a share of a full training day's experience.</summary>
        public float matchXpShare;

        /// <summary>
        /// Does growing tall cost anything? Off for the easy talents (user: no drawbacks from
        /// height - growing taller does not take agility, ball control or suppleness away). A short
        /// player is still worse in the air on every talent. Live now, in BodyModel.HeightEffects.
        /// </summary>
        public bool heightDownsides;
        /// <summary>How growth turns into playstyle choices. Waits for the growth and event systems.</summary>
        public HeightEvents heightEvents;

        /// <summary>
        /// How fast muscle grows, against an ordinary pro (1). Everyone starts under the same
        /// ordinary-15-year-old ceiling (user); this is where talents part. Multiplies with the
        /// "builds muscle" perk (x1.5). Waits for the training system.
        /// </summary>
        public float muscleGrowth;

        /// <summary>
        /// How much of the young body's acceleration shortfall he feels, 0..1 (user, 2026-10-18:
        /// "lower than an adult, but the easier the talent the smaller the gap"). The muscle curve
        /// puts a 15-year-old at x0.85-0.94; he loses this share of that gap. Physicality is not
        /// softened - it stays clearly under an adult's on every talent.
        /// </summary>
        public float youthAccelPenalty;

        /// <summary>
        /// The least of the possible growth (potential - height now) he reaches, however he lives
        /// (user: 90% whatever the habits - for the greatest ever only). Habits fill the rest.
        /// Waits for the growth system (PROJECT.md 10.6).
        /// </summary>
        public float minHeightGrowth;
    }

    /// <summary>
    /// The four talents. PROVISIONAL numbers (PROJECT.md 10.7): the 1부리거 is the yardstick at 1,
    /// and the user's sentences set the ends - the greatest ever loses nothing for not
    /// training and grows from matches alone; the dullard barely grows unless every day is
    /// training, and good traits rarely come. Start overall (user): 75 / 65 / 55 / 45, ten apart.
    /// Perk points are 10 for everyone (user) - talent does not change them.
    /// </summary>
    public static class TalentCatalog
    {
        public static readonly TalentDef[] All =
        {
            new TalentDef { tier = TalentTier.GreatestEver, name = "인류 역사상 최고", difficulty = "매우 쉬움",
                startOverall = 75, heightDownsides = false, heightEvents = HeightEvents.None, muscleGrowth = 1.6f, youthAccelPenalty = 0.1f, minHeightGrowth = 0.9f,
                growthXp = 2.0f, goodTraitChance = 2.0f, ageDecline = 0.4f, idleDecline = 0f, matchXpShare = 1.0f,
                blurb = "축구를 위해 태어난 몸. 무엇을 해도 빨리 늘고, 경기만 뛰어도 실력이 오른다. 훈련을 쉬어도 녹슬지 않고, 나이가 들어도 오래 버틴다. 좋은 특성이 쉽게 붙는다." },
            new TalentDef { tier = TalentTier.WorldClass, name = "월드클래스", difficulty = "쉬움",
                startOverall = 65, heightDownsides = false, heightEvents = HeightEvents.None, muscleGrowth = 1.3f, youthAccelPenalty = 0.4f, minHeightGrowth = 0.5f,
                growthXp = 1.5f, goodTraitChance = 1.5f, ageDecline = 0.7f, idleDecline = 0.3f, matchXpShare = 0.6f,
                blurb = "세계 무대에 설 그릇. 남들보다 빨리 늘고, 경기에서도 많이 배운다. 며칠 쉬어도 크게 떨어지지 않고, 나이에도 강하다." },
            new TalentDef { tier = TalentTier.TopFlight, name = "1부리거", difficulty = "보통",
                startOverall = 55, heightDownsides = true, heightEvents = HeightEvents.Sometimes, muscleGrowth = 1.0f, youthAccelPenalty = 0.7f, minHeightGrowth = 0.5f,
                growthXp = 1.0f, goodTraitChance = 1.0f, ageDecline = 1.0f, idleDecline = 1.0f, matchXpShare = 0.3f,
                blurb = "1부 리그에서 뛸 만한 재능. 훈련한 만큼 늘고, 쉬면 그만큼 떨어진다. 경기에서도 배우지만 기본은 훈련이다." },
            new TalentDef { tier = TalentTier.Dullard, name = "둔재", difficulty = "어려움",
                startOverall = 45, heightDownsides = true, heightEvents = HeightEvents.Always, muscleGrowth = 0.7f, youthAccelPenalty = 1.0f, minHeightGrowth = 0.5f,
                growthXp = 0.6f, goodTraitChance = 0.4f, ageDecline = 1.3f, idleDecline = 2.0f, matchXpShare = 0.05f,
                blurb = "타고난 것은 없다. 남는 것은 노력뿐이다. 모든 일정을 훈련으로 채워야 겨우 오르고, 경기만으로는 거의 늘지 않는다. 쉬면 금세 떨어지고, 좋은 특성은 좀처럼 붙지 않는다." },
        };

        /// <summary>One line for the talent card.</summary>
        public static string HeightLine(TalentDef t)
        {
            if (!t.heightDownsides) return "키: 커져도 민첩·볼 컨트롤 손해 없음, 자랄 때 이벤트 없음 (작으면 공중볼 불리는 그대로)";
            return t.heightEvents == HeightEvents.Always
                ? "키: 장단점 모두 — 자랄 때마다 플레이스타일을 직접 고른다"
                : "키: 장단점 모두 — 크게 자라면 가끔 플레이스타일을 고른다";
        }

        public static TalentDef Get(TalentTier t)
        {
            foreach (var d in All) if (d.tier == t) return d;
            return All[2];
        }
    }
}
