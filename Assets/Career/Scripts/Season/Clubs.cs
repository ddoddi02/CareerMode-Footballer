using System.Collections.Generic;

namespace Career
{
    /// <summary>A club of the calendar. Strength is 0..100 and only decides provisional results (<see cref="MatchSim"/>).</summary>
    public class Club
    {
        public string id, name;
        public float strength;
        /// <summary>Plays in the Spanish league or its cup.</summary>
        public bool domestic;
    }

    /// <summary>
    /// The clubs of the provisional calendar (user, 2026-10-26: the other countries' clubs are
    /// stand-ins). Spanish clubs have made-up names - real ones are a licensing matter, asked in
    /// PROJECT.md 10.8. Nothing here is a real squad or rating.
    /// </summary>
    public static class ClubPool
    {
        public const string PlayerId = "player";
        public const int LeagueSize = 20, LowerSize = 12;
        /// <summary>Stand-ins from other countries: what each continental league needs besides the Spanish places.</summary>
        public const int ChampionsOthers = 32, EuropaOthers = 34, ConferenceOthers = 35;

        static readonly string[] SpanishNames =
        {
            "라 코스타 CF", "비스카야 AC", "안달루스 CF", "몬테 알토 SD", "산 마르코스 FC", "리베라 UD", "라 메사 CD", "푸에르토 FC",
            "세라니아 CF", "에브로 AC", "라 만차 FC", "소리아노 CF", "카르타고 UD", "이베리아 FC", "엘 파로 CD", "메세타 CF",
            "토레 베르데 AC", "파라무노 FC", "론다 UD",
        };

        /// <summary>
        /// All clubs: the player's, 19 more in the league (strength 88 down to 52), 12 from the
        /// lower leagues for the cup, and the stand-ins of the three continental competitions.
        /// </summary>
        public static Dictionary<string, Club> Build(string playerClubName, float playerStrength)
        {
            var all = new Dictionary<string, Club>();
            all[PlayerId] = new Club { id = PlayerId, name = playerClubName, strength = playerStrength, domestic = true };
            for (int i = 0; i < SpanishNames.Length; i++)
                Add(all, "es" + (i + 1).ToString("00"), SpanishNames[i], Ramp(88f, 52f, i, SpanishNames.Length), true);
            for (int i = 0; i < LowerSize; i++)
                Add(all, "lo" + (i + 1).ToString("00"), "2부 팀 " + (i + 1).ToString("00"), Ramp(45f, 30f, i, LowerSize), true);
            Others(all, "ucl", "챔스 임시 팀 ", ChampionsOthers, 92f, 72f);
            Others(all, "uel", "유로파 임시 팀 ", EuropaOthers, 78f, 58f);
            Others(all, "uecl", "컨퍼런스 임시 팀 ", ConferenceOthers, 66f, 46f);
            return all;
        }

        /// <summary>The ids of the clubs of the league, strongest first.</summary>
        public static List<string> LeagueIds(Dictionary<string, Club> all)
        {
            var ids = new List<string>();
            foreach (Club c in all.Values) if (c.domestic && !c.id.StartsWith("lo")) ids.Add(c.id);
            ids.Sort((x, y) => all[y].strength.CompareTo(all[x].strength));
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
                Add(all, prefix + (i + 1).ToString("00"), name + (i + 1).ToString("00"), Ramp(top, bottom, i, count), false);
        }

        static void Add(Dictionary<string, Club> all, string id, string name, float strength, bool domestic)
        {
            all[id] = new Club { id = id, name = name, strength = strength, domestic = domestic };
        }

        static float Ramp(float top, float bottom, int i, int count)
        {
            return top + (bottom - top) * i / (count - 1);
        }
    }
}
