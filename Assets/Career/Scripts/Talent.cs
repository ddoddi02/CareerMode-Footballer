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

    public class TalentDef
    {
        public TalentTier tier;
        public string name, difficulty, blurb;

        /// <summary>
        /// The overall he starts at: every creation stat begins here, and here is where it costs
        /// nothing (CreationRules.Overall). User: hard 45, easy 75, and the greatest ever no
        /// higher than easy - "90 from the start is too much".
        /// </summary>
        public int startOverall;
        /// <summary>Points for perks and build (and stats, with one pool). Easier gets more.</summary>
        public int perkPoints;

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
    }

    /// <summary>
    /// The four talents. PROVISIONAL numbers (PROJECT.md 10.7): the 1부리거 is the yardstick at 1,
    /// and the user's sentences set the ends - the greatest ever loses nothing for not
    /// training and grows from matches alone; the dullard barely grows unless every day is
    /// training, and good traits rarely come. Start overall: the user's 45 (hard) and 75 (easy),
    /// 60 between them, and 75 again for the greatest ever (user) - he starts where the world
    /// class does and pulls away through points and growth.
    /// </summary>
    public static class TalentCatalog
    {
        public static readonly TalentDef[] All =
        {
            new TalentDef { tier = TalentTier.GreatestEver, name = "인류 역사상 최고", difficulty = "매우 쉬움",
                startOverall = 75, perkPoints = 24,
                growthXp = 2.0f, goodTraitChance = 2.0f, ageDecline = 0.4f, idleDecline = 0f, matchXpShare = 1.0f,
                blurb = "축구를 위해 태어난 몸. 무엇을 해도 빨리 늘고, 경기만 뛰어도 실력이 오른다. 훈련을 쉬어도 녹슬지 않고, 나이가 들어도 오래 버틴다. 좋은 특성이 쉽게 붙는다." },
            new TalentDef { tier = TalentTier.WorldClass, name = "월드클래스", difficulty = "쉬움",
                startOverall = 75, perkPoints = 20,
                growthXp = 1.5f, goodTraitChance = 1.5f, ageDecline = 0.7f, idleDecline = 0.3f, matchXpShare = 0.6f,
                blurb = "세계 무대에 설 그릇. 남들보다 빨리 늘고, 경기에서도 많이 배운다. 며칠 쉬어도 크게 떨어지지 않고, 나이에도 강하다." },
            new TalentDef { tier = TalentTier.TopFlight, name = "1부리거", difficulty = "보통",
                startOverall = 60, perkPoints = 16,
                growthXp = 1.0f, goodTraitChance = 1.0f, ageDecline = 1.0f, idleDecline = 1.0f, matchXpShare = 0.3f,
                blurb = "1부 리그에서 뛸 만한 재능. 훈련한 만큼 늘고, 쉬면 그만큼 떨어진다. 경기에서도 배우지만 기본은 훈련이다." },
            new TalentDef { tier = TalentTier.Dullard, name = "둔재", difficulty = "어려움",
                startOverall = 45, perkPoints = 12,
                growthXp = 0.6f, goodTraitChance = 0.4f, ageDecline = 1.3f, idleDecline = 2.0f, matchXpShare = 0.05f,
                blurb = "타고난 것은 없다. 남는 것은 노력뿐이다. 모든 일정을 훈련으로 채워야 겨우 오르고, 경기만으로는 거의 늘지 않는다. 쉬면 금세 떨어지고, 좋은 특성은 좀처럼 붙지 않는다." },
        };

        public static TalentDef Get(TalentTier t)
        {
            foreach (var d in All) if (d.tier == t) return d;
            return All[2];
        }
    }
}
