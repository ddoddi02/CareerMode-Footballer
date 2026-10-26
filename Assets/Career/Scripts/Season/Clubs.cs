using System.Collections.Generic;

namespace Career
{
    /// <summary>
    /// A club of the calendar. Its strength (user, 2026-10-26) is the squad's average overall
    /// plus its chemistry: overall + (chemistry - 50) x 0.2, so chemistry 100 is worth 10 points.
    /// It only decides provisional results (<see cref="MatchSim"/>).
    /// </summary>
    public class Club
    {
        public const float ChemistryWeight = 0.2f;

        public string id, name;
        /// <summary>The squad's average overall, 0..100.</summary>
        public float squadOverall;
        /// <summary>Team chemistry, 0..100, 50 neutral (PROJECT.md 10.9).</summary>
        public float chemistry = 50f;
        /// <summary>Plays in the Spanish league or its cup.</summary>
        public bool domestic;

        public float Strength { get { return squadOverall + (chemistry - 50f) * ChemistryWeight; } }
    }

    /// <summary>
    /// The clubs of the provisional calendar (user, 2026-10-26: the other countries' clubs are
    /// stand-ins). Spanish clubs have made-up names - real ones are a licensing matter, asked in
    /// PROJECT.md 10.8. The league's clubs have a place in a made-up table of last season
    /// (<see cref="RankedNames"/>) and a squad level to match it; nothing here is a real squad.
    /// </summary>
    public static class ClubPool
    {
        public const string PlayerId = "player";
        public const int LeagueSize = 20, LowerSize = 12;
        /// <summary>Stand-ins from other countries: what each continental competition needs besides the Spanish places.</summary>
        public const int Continental1Others = 32, Continental2Others = 34, Continental3Others = 35;
        /// <summary>Squad overall of the league's best and worst club last season (provisional).</summary>
        public const float TopLevel = 80f, BottomLevel = 52f;

        /// <summary>The league's clubs in the order of last season's table, champion first.</summary>
        public static readonly string[] RankedNames =
        {
            "라 코스타 CF", "비스카야 AC", "카스티야 FC", "안달루스 CF", "몬테 알토 SD", "산 마르코스 FC", "리베라 UD", "라 메사 CD",
            "푸에르토 FC", "세라니아 CF", "에브로 AC", "라 만차 FC", "소리아노 CF", "카르타고 UD", "이베리아 FC", "엘 파로 CD",
            "메세타 CF", "토레 베르데 AC", "파라무노 FC", "론다 UD",
        };

        /// <summary>The squad level a club of this place in last season's table has, 1 = best.</summary>
        public static float LevelOf(int rank)
        {
            return Ramp(TopLevel, BottomLevel, rank - 1, LeagueSize);
        }

        /// <summary>The id the club of this place has, unless it is the player's.</summary>
        public static string IdOf(int rank) { return "es" + rank.ToString("00"); }

        /// <summary>
        /// All clubs. The player's is the one in <paramref name="playerRank"/> place of last
        /// season's table (id "player"); 12 lower-league clubs fill the cup; the stand-ins of the three
        /// continental competitions have a level by how big the competition is.
        /// </summary>
        public static Dictionary<string, Club> Build(int playerRank)
        {
            var all = new Dictionary<string, Club>();
            for (int rank = 1; rank <= LeagueSize; rank++)
            {
                string id = rank == playerRank ? PlayerId : IdOf(rank);
                // chemistry hovers round 50 (-5..+5), by place, so the others are not all alike
                Add(all, id, RankedNames[rank - 1], LevelOf(rank), true, 50f + (rank * 7) % 11 - 5);
            }
            for (int i = 0; i < LowerSize; i++)
                Add(all, "lo" + (i + 1).ToString("00"), "2부 팀 " + (i + 1).ToString("00"), Ramp(50f, 40f, i, LowerSize), true, 50f);
            Others(all, "cn1", "대륙 1부 임시 팀 ", Continental1Others, 82f, 66f);
            Others(all, "cn2", "대륙 2부 임시 팀 ", Continental2Others, 70f, 56f);
            Others(all, "cn3", "대륙 3부 임시 팀 ", Continental3Others, 62f, 48f);
            return all;
        }

        /// <summary>
        /// The player's club takes the player into its squad: ten of the eleven are at the club's
        /// level, the eleventh is him. Chemistry is the squad's, live.
        /// </summary>
        public static void SetPlayerClub(Dictionary<string, Club> all, int playerRank, float playerOverall, float chemistry)
        {
            Club c = all[PlayerId];
            c.squadOverall = (10f * LevelOf(playerRank) + playerOverall) / 11f;
            c.chemistry = chemistry;
        }

        /// <summary>The ids of the clubs of the league, strongest first.</summary>
        public static List<string> LeagueIds(Dictionary<string, Club> all)
        {
            var ids = new List<string>();
            foreach (Club c in all.Values) if (c.domestic && !c.id.StartsWith("lo")) ids.Add(c.id);
            ids.Sort((x, y) => all[y].Strength.CompareTo(all[x].Strength));
            return ids;
        }

        public static List<string> Prefixed(Dictionary<string, Club> all, string prefix)
        {
            var ids = new List<string>();
            foreach (Club c in all.Values) if (!c.domestic && c.id.StartsWith(prefix)) ids.Add(c.id);
            ids.Sort();
            return ids;
        }

        static void Others(Dictionary<string, Club> all, string prefix, string name, int count, float top, float bottom)
        {
            for (int i = 0; i < count; i++)
                Add(all, prefix + (i + 1).ToString("00"), name + (i + 1).ToString("00"), Ramp(top, bottom, i, count), false, 50f);
        }

        static void Add(Dictionary<string, Club> all, string id, string name, float overall, bool domestic, float chemistry)
        {
            all[id] = new Club { id = id, name = name, squadOverall = overall, chemistry = chemistry, domestic = domestic };
        }

        static float Ramp(float top, float bottom, int i, int count)
        {
            return top + (bottom - top) * i / (count - 1);
        }
    }
}
