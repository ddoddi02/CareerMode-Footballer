using System;
using System.Collections.Generic;
using UnityEngine;

namespace Career
{
    /// <summary>What the last season left behind: the league order and the cup's two finalists. Drives who plays what next.</summary>
    public class PreviousSeason
    {
        public List<string> leagueOrder = new List<string>();   // champion first
        public string cupWinner, cupRunnerUp;

        /// <summary>
        /// The table of a season nobody played (first game): the order of <see cref="ClubPool.RankedNames"/>,
        /// with the player's club in <paramref name="playerRank"/> place. The cup was won by the club
        /// in 4th place and lost by the 7th - provisional.
        /// </summary>
        public static PreviousSeason Provisional(int playerRank)
        {
            var p = new PreviousSeason();
            for (int rank = 1; rank <= ClubPool.LeagueSize; rank++)
                p.leagueOrder.Add(rank == playerRank ? ClubPool.PlayerId : ClubPool.IdOf(rank));
            p.cupWinner = p.leagueOrder[3];
            p.cupRunnerUp = p.leagueOrder[6];
            return p;
        }
    }

    /// <summary>Who plays the continental competitions and the super cup this season, from <see cref="PreviousSeason"/>.</summary>
    public class Qualified
    {
        public List<string> continental1 = new List<string>(), continental2 = new List<string>(), continental3 = new List<string>(), superCup = new List<string>();

        /// <summary>
        /// By last season's league place (user): 1st-4th the continental cup, first division; 5th and
        /// the cup winner the second; 6th the third. A place a club already has goes to the next one
        /// down the table. The super cup takes the top two, the cup winner and runner-up, the
        /// same way. PROVISIONAL (PROJECT.md 10.13).
        /// </summary>
        public static Qualified From(PreviousSeason p)
        {
            var q = new Qualified();
            List<string> order = p.leagueOrder;
            var used = new HashSet<string>();
            for (int i = 0; i < 4; i++) { used.Add(order[i]); q.continental1.Add(order[i]); }
            used.Add(order[4]); q.continental2.Add(order[4]);
            q.continental2.Add(Place(p.cupWinner, order, used));
            q.continental3.Add(Place(null, order, used));

            var s = new HashSet<string>();
            q.superCup.Add(Place(order[0], order, s));
            q.superCup.Add(Place(order[1], order, s));
            q.superCup.Add(Place(p.cupWinner, order, s));
            q.superCup.Add(Place(p.cupRunnerUp, order, s));
            return q;
        }

        /// <summary>The wanted club if it is free, else the best of the table that is; marks it taken.</summary>
        static string Place(string wanted, List<string> order, HashSet<string> taken)
        {
            if (wanted != null && taken.Add(wanted)) return wanted;
            foreach (string id in order) if (taken.Add(id)) return id;
            return null;
        }

        public bool Has(string club, CompetitionId c)
        {
            switch (c)
            {
                case CompetitionId.Continental1: return continental1.Contains(club);
                case CompetitionId.Continental2: return continental2.Contains(club);
                case CompetitionId.Continental3: return continental3.Contains(club);
                case CompetitionId.SuperCup: return superCup.Contains(club);
                default: return true;
            }
        }
    }

    /// <summary>
    /// One season's calendar and results (user, 2026-10-26): the league, the domestic cup and super
    /// cup, and the three continental cups, with the continental places given
    /// by last season's table. Day 1 is a Saturday, two weeks before the league starts; the
    /// season ends at the end of May. Numbers are PROVISIONAL (PROJECT.md 10.13).
    ///
    /// The league (380 matches) is laid out at the start. A knockout round - cup, super cup,
    /// the continental ones after their league phase - is drawn and put on the calendar when the
    /// round before it is done, so the calendar fills as the season goes. Results come from
    /// <see cref="AdvanceTo"/> (drawn from the clubs' strength) or <see cref="SetResult"/> (a match played).
    /// </summary>
    public class Season
    {
        public const int PreSeasonDays = 14;
        public const int LeagueRounds = 38;
        public const int CupTeams = 32;
        /// <summary>Teams of a continental league phase, and matches each plays in it (first and second division 8, third 6).</summary>
        public const int EuroTeams = 36;
        const int Sat = 0, Sun = 1, Tue = 3, Wed = 4, Thu = 5;
        /// <summary>Weeks (from the first league Saturday) the league leaves free: an October break, Christmas, the super cup weekend, a March break.</summary>
        static readonly int[] SkipWeeks = { 8, 19, 21, 32 };
        static readonly string[] Weekdays = { "토", "일", "월", "화", "수", "목", "금" };
        static readonly string[] CupRounds = { "32강", "16강", "8강", "4강", "결승" };
        static readonly int[] CupDay1 = { Day(10, Wed), Day(15, Wed), Day(21, Wed), Day(24, Wed), Day(35, Wed) };
        static readonly int CupSemiLeg2 = Day(28, Wed);
        static readonly int SuperSemis = Day(20, Wed), SuperFinal = Day(21, Sun);
        static readonly string[] EuroRounds = { "리그 페이즈", "플레이오프", "16강", "8강", "4강", "결승" };
        static readonly int[] LeaguePhaseWeeks = { 4, 7, 9, 12, 14, 16, 22, 23 };
        static readonly int[][] KnockoutWeeks = { new[] { 26, 27 }, new[] { 29, 30 }, new[] { 33, 34 }, new[] { 36, 37 } };

        public readonly int number;
        public readonly DateTime startDate;
        public readonly Dictionary<string, Club> clubs;
        public readonly string playerId;
        /// <summary>Place in last season's table of the club the player is at - its squad level while it lasts.</summary>
        public readonly int playerRank;
        public readonly PreviousSeason previous;
        public readonly Qualified qualified;
        public readonly List<Fixture> fixtures = new List<Fixture>();
        public readonly List<string> leagueIds;

        readonly System.Random rng;
        int simulatedThrough;

        // domestic cup and super cup
        List<Tie> cupTies, superTies;
        int cupStage, superStage;
        List<string> cupAlive = new List<string>();
        public string cupWinner { get; private set; }
        public string cupRunnerUp { get; private set; }
        public string superCupWinner { get; private set; }

        class Euro
        {
            public CompetitionId comp;
            public List<string> teams;
            public int matches;       // league phase matches each
            public int stage;         // 0 league phase, 1 playoff, 2 round of 16, 3 quarter, 4 semi, 5 final, 6 over
            public List<Tie> ties;
            public List<string> seeds = new List<string>();     // top eight of the league phase
            public string winner;
        }
        readonly List<Euro> euros = new List<Euro>();

        /// <summary>
        /// The first season: the player's club is the one that finished <paramref name="lastRank"/>
        /// last season (chosen at character creation); nobody played that season, its table is made up.
        /// </summary>
        public static Season First(int lastRank, int seed)
        {
            Dictionary<string, Club> clubs = ClubPool.Build(lastRank);
            return new Season(1, new DateTime(2026, 8, 1), clubs, PreviousSeason.Provisional(lastRank), seed, lastRank);
        }

        /// <summary>
        /// Sets the player's club from its live squad (user: team strength = the squad's average
        /// overall + chemistry). Called before results are drawn.
        /// </summary>
        public void SetPlayerClub(Squad squad)
        {
            ClubPool.SetPlayerClub(clubs, squad);
        }

        Season(int number, DateTime start, Dictionary<string, Club> clubs, PreviousSeason previous, int seed, int playerRank)
        {
            this.playerRank = playerRank;
            this.number = number;
            startDate = start;
            this.clubs = clubs;
            playerId = ClubPool.PlayerId;
            this.previous = previous;
            qualified = Qualified.From(previous);
            rng = new System.Random(seed);
            leagueIds = ClubPool.LeagueIds(clubs);
            BuildLeague();
            StartCup();
            StartSuperCup();
            StartEuro(CompetitionId.Continental1, qualified.continental1, "cn1", 8);
            StartEuro(CompetitionId.Continental2, qualified.continental2, "cn2", 8);
            StartEuro(CompetitionId.Continental3, qualified.continental3, "cn3", 6);
        }

        // ------------------------------------------------------------ dates --

        /// <summary>The day of a weekday (Sat 0 .. Thu 5) in a week counted from the first league Saturday.</summary>
        static int Day(int week, int weekday) { return PreSeasonDays + 7 * week + weekday + 1; }

        public DateTime DateOf(int day) { return startDate.AddDays(day - 1); }

        public static string Weekday(int day) { return Weekdays[(day - 1) % 7]; }

        /// <summary>"8/22(토)".</summary>
        public string DateText(int day)
        {
            DateTime d = DateOf(day);
            return d.Month + "/" + d.Day + "(" + Weekday(day) + ")";
        }

        public string ClubName(string id) { return clubs[id].name; }

        public int LastDay
        {
            get
            {
                int last = 0;
                foreach (Fixture f in fixtures) last = Mathf.Max(last, f.day);
                return last;
            }
        }

        // ------------------------------------------------------------ building --

        Fixture Add(int day, CompetitionId comp, string round, string home, string away, bool neutral = false, bool table = false)
        {
            var f = new Fixture { day = day, comp = comp, round = round, home = home, away = away, neutral = neutral, table = table };
            fixtures.Add(f);
            return f;
        }

        Tie MakeTie(CompetitionId comp, string round, string a, string b, int day1, int day2, bool neutral = false)
        {
            var t = new Tie { a = a, b = b };
            t.first = Add(day1, comp, round, a, b, neutral);
            if (day2 > 0) t.second = Add(day2, comp, round, b, a);
            return t;
        }

        void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T t = list[i]; list[i] = list[j]; list[j] = t;
            }
        }

        /// <summary>Round r of the circle method over n teams (n even): n/2 pairs, every pair meeting once over n-1 rounds.</summary>
        static List<int[]> CircleRound(int n, int r)
        {
            var arr = new int[n];
            arr[0] = 0;
            for (int i = 1; i < n; i++) arr[i] = 1 + (i - 1 + r) % (n - 1);
            var pairs = new List<int[]>();
            for (int i = 0; i < n / 2; i++) pairs.Add(new[] { arr[i], arr[n - 1 - i] });
            return pairs;
        }

        void BuildLeague()
        {
            var ids = new List<string>(leagueIds);
            Shuffle(ids);
            int n = ids.Count, half = n - 1;
            int round = 0;
            for (int week = 0; round < LeagueRounds; week++)
            {
                if (Array.IndexOf(SkipWeeks, week) >= 0) continue;
                int r = round % half;
                List<int[]> pairs = CircleRound(n, r);
                Shuffle(pairs);
                for (int m = 0; m < pairs.Count; m++)
                {
                    int a = pairs[m][0], b = pairs[m][1];
                    bool aHome = ((r + Math.Min(a, b)) % 2 == 0) == (round < half);
                    Add(Day(week, m < 6 ? Sat : Sun), CompetitionId.League, (round + 1) + "라운드",
                        ids[aHome ? a : b], ids[aHome ? b : a], false, true);
                }
                round++;
            }
        }

        void StartCup()
        {
            foreach (Club c in clubs.Values) if (c.domestic) cupAlive.Add(c.id);
            cupAlive.Sort();
            cupStage = 0;
            CupRound();
        }

        void CupRound()
        {
            Shuffle(cupAlive);
            cupTies = new List<Tie>();
            for (int i = 0; i + 1 < cupAlive.Count; i += 2)
                cupTies.Add(MakeTie(CompetitionId.Cup, CupRounds[cupStage], cupAlive[i], cupAlive[i + 1], CupDay1[cupStage],
                    cupStage == 3 ? CupSemiLeg2 : 0, cupStage == 4));
        }

        void StartSuperCup()
        {
            List<string> s = qualified.superCup;
            superTies = new List<Tie>
            {
                MakeTie(CompetitionId.SuperCup, "4강", s[0], s[3], SuperSemis, 0, true),
                MakeTie(CompetitionId.SuperCup, "4강", s[1], s[2], SuperSemis, 0, true),
            };
        }

        void StartEuro(CompetitionId comp, List<string> spanish, string prefix, int matches)
        {
            var e = new Euro { comp = comp, matches = matches, teams = new List<string>(spanish) };
            e.teams.AddRange(ClubPool.Prefixed(clubs, prefix));
            Shuffle(e.teams);
            var homes = new Dictionary<string, int>();
            foreach (string t in e.teams) homes[t] = 0;
            for (int md = 0; md < matches; md++)
            {
                List<int[]> pairs = CircleRound(EuroTeams, md);
                for (int i = 0; i < pairs.Count; i++)
                {
                    string a = e.teams[pairs[i][0]], b = e.teams[pairs[i][1]];
                    // the side with fewer home matches so far hosts, so each ends up with about half
                    bool aHome = homes[a] != homes[b] ? homes[a] < homes[b] : rng.Next(2) == 0;
                    string home = aHome ? a : b, away = aHome ? b : a;
                    homes[home]++;
                    Add(Day(LeaguePhaseWeeks[md], comp == CompetitionId.Continental1 ? (i % 2 == 0 ? Tue : Wed) : Thu),
                        comp, "리그 페이즈 " + (md + 1), home, away, false, true);
                }
            }
            euros.Add(e);
        }

        // ------------------------------------------------------------ progress --

        /// <summary>Plays every match through this day that has not been played, results drawn from strength, and draws the knockout rounds that become due.</summary>
        public void AdvanceTo(int day)
        {
            for (int d = simulatedThrough + 1; d <= day; d++)
            {
                for (int i = 0; i < fixtures.Count; i++)
                    if (fixtures[i].day == d && !fixtures[i].played) MatchSim.Play(fixtures[i], clubs[fixtures[i].home], clubs[fixtures[i].away], rng);
                Progress();
            }
            simulatedThrough = Mathf.Max(simulatedThrough, day);
        }

        /// <summary>Records a match that was really played (for when the match is connected). Draws the following rounds as needed.</summary>
        public void SetResult(Fixture f, int homeGoals, int awayGoals)
        {
            f.homeGoals = homeGoals;
            f.awayGoals = awayGoals;
            f.played = true;
            Progress();
        }

        /// <summary>Resolves a level or finished tie: the aggregate, or penalties.</summary>
        string Decide(Tie t)
        {
            int ga = t.first.homeGoals + (t.second != null ? t.second.awayGoals : 0);
            int gb = t.first.awayGoals + (t.second != null ? t.second.homeGoals : 0);
            if (ga != gb) return ga > gb ? t.a : t.b;
            bool aWins = MatchSim.FirstWinsPenalties(clubs[t.a], clubs[t.b], rng);
            Fixture last = t.second ?? t.first;
            string w = aWins ? t.a : t.b;
            last.penWinner = w;
            return w;
        }

        static bool AllDone(List<Tie> ties)
        {
            if (ties == null) return false;
            foreach (Tie t in ties) if (!t.Done) return false;
            return true;
        }

        List<string> Winners(List<Tie> ties)
        {
            var w = new List<string>();
            foreach (Tie t in ties) { t.winner = Decide(t); w.Add(t.winner); }
            return w;
        }

        void Progress()
        {
            if (AllDone(cupTies))
            {
                List<string> w = Winners(cupTies);
                if (cupStage == 4)
                {
                    cupWinner = w[0];
                    cupRunnerUp = cupTies[0].a == w[0] ? cupTies[0].b : cupTies[0].a;
                    cupTies = null;
                }
                else { cupStage++; cupAlive = w; CupRound(); }
            }
            if (AllDone(superTies))
            {
                List<string> w = Winners(superTies);
                if (superStage == 0)
                {
                    superStage = 1;
                    superTies = new List<Tie> { MakeTie(CompetitionId.SuperCup, "결승", w[0], w[1], SuperFinal, 0, true) };
                }
                else { superCupWinner = w[0]; superTies = null; }
            }
            foreach (Euro e in euros) ProgressEuro(e);
        }

        void ProgressEuro(Euro e)
        {
            if (e.stage == 0)
            {
                foreach (Fixture f in fixtures) if (f.comp == e.comp && f.table && !f.played) return;
                List<TableRow> rank = Table(e.comp);
                for (int i = 0; i < 8; i++) e.seeds.Add(rank[i].club);
                // 9th-24th: 9th v 24th, 10th v 23rd ...; the lower-placed side hosts the first match
                var ties = new List<Tie>();
                for (int i = 0; i < 8; i++) ties.Add(KnockoutTie(e, 1, i, rank[23 - i].club, rank[8 + i].club));
                e.ties = ties;
                e.stage = 1;
                return;
            }
            if (e.stage >= 6 || !AllDone(e.ties)) return;
            List<string> w = Winners(e.ties);
            if (e.stage == 5) { e.winner = w[0]; e.ties = null; e.stage = 6; return; }
            var next = new List<Tie>();
            if (e.stage == 1)
            {
                // the seeds meet the playoff winners, drawn
                Shuffle(w);
                for (int i = 0; i < 8; i++) next.Add(KnockoutTie(e, 2, i, w[i], e.seeds[i]));
            }
            else
            {
                Shuffle(w);
                if (e.stage == 4) next.Add(MakeTie(e.comp, "결승", w[0], w[1], FinalDay(e.comp), 0, true));
                else for (int i = 0; i + 1 < w.Count; i += 2) next.Add(KnockoutTie(e, e.stage + 1, i / 2, w[i], w[i + 1]));
            }
            e.ties = next;
            e.stage++;
        }

        /// <summary>A two-legged tie of a continental knockout round (stage 1 playoff .. 4 semi-final); i is its place in the round.</summary>
        Tie KnockoutTie(Euro e, int stage, int i, string a, string b)
        {
            int[] weeks = KnockoutWeeks[stage - 1];
            int weekday = e.comp == CompetitionId.Continental1 ? (i % 2 == 0 ? Tue : Wed) : Thu;
            return MakeTie(e.comp, EuroRounds[stage], a, b, Day(weeks[0], weekday), Day(weeks[1], weekday));
        }

        static int FinalDay(CompetitionId c)
        {
            switch (c)
            {
                case CompetitionId.Continental1: return Day(40, Wed);
                case CompetitionId.Continental2: return Day(39, Thu);
                default: return Day(40, Thu);
            }
        }

        // ------------------------------------------------------------ reading --

        /// <summary>The season is over: every competition has its winner and every match is played.</summary>
        public bool IsOver
        {
            get
            {
                if (cupWinner == null || superCupWinner == null) return false;
                foreach (Euro e in euros) if (e.stage < 6) return false;
                foreach (Fixture f in fixtures) if (!f.played) return false;
                return true;
            }
        }

        public string Winner(CompetitionId c)
        {
            switch (c)
            {
                case CompetitionId.Cup: return cupWinner;
                case CompetitionId.SuperCup: return superCupWinner;
                case CompetitionId.League: return IsOver ? Table(c)[0].club : null;
                default:
                    foreach (Euro e in euros) if (e.comp == c) return e.winner;
                    return null;
            }
        }

        public List<string> Participants(CompetitionId c)
        {
            switch (c)
            {
                case CompetitionId.League: return leagueIds;
                case CompetitionId.Cup: { var l = new List<string>(); foreach (Club x in clubs.Values) if (x.domestic) l.Add(x.id); return l; }
                case CompetitionId.SuperCup: return qualified.superCup;
                default:
                    foreach (Euro e in euros) if (e.comp == c) return e.teams;
                    return new List<string>();
            }
        }

        /// <summary>The competitions a club has a place in this season.</summary>
        public List<CompetitionId> CompetitionsOf(string club)
        {
            var l = new List<CompetitionId>();
            foreach (CompetitionId c in Enum.GetValues(typeof(CompetitionId)))
                if (Participants(c).Contains(club)) l.Add(c);
            return l;
        }

        /// <summary>League table, or a continental league phase. Points, goal difference, goals for, then name.</summary>
        public List<TableRow> Table(CompetitionId c)
        {
            var rows = new Dictionary<string, TableRow>();
            foreach (string id in Participants(c)) rows[id] = new TableRow { club = id };
            foreach (Fixture f in fixtures)
            {
                if (f.comp != c || !f.table || !f.played) continue;
                Count(rows[f.home], f.homeGoals, f.awayGoals);
                Count(rows[f.away], f.awayGoals, f.homeGoals);
            }
            var list = new List<TableRow>(rows.Values);
            list.Sort((x, y) =>
            {
                int r = y.Points.CompareTo(x.Points);
                if (r == 0) r = y.GoalDifference.CompareTo(x.GoalDifference);
                if (r == 0) r = y.goalsFor.CompareTo(x.goalsFor);
                return r != 0 ? r : string.CompareOrdinal(x.club, y.club);
            });
            return list;
        }

        static void Count(TableRow r, int gf, int ga)
        {
            r.played++;
            r.goalsFor += gf;
            r.goalsAgainst += ga;
            if (gf > ga) r.won++; else if (gf == ga) r.drawn++; else r.lost++;
        }

        /// <summary>A club's unplayed matches from this day on, soonest first.</summary>
        public List<Fixture> Upcoming(string club, int fromDay, int count)
        {
            var l = new List<Fixture>();
            foreach (Fixture f in fixtures) if (!f.played && f.day >= fromDay && f.Involves(club)) l.Add(f);
            l.Sort((x, y) => x.day.CompareTo(y.day));
            if (l.Count > count) l.RemoveRange(count, l.Count - count);
            return l;
        }

        public Fixture NextFixture(string club, int fromDay)
        {
            List<Fixture> l = Upcoming(club, fromDay, 1);
            return l.Count > 0 ? l[0] : null;
        }

        /// <summary>"8/22(토) 리그 3라운드 · 홈 vs 라 코스타 CF".</summary>
        public string Describe(Fixture f, string club)
        {
            string where = f.neutral ? "중립" : f.home == club ? "홈" : "원정";
            return DateText(f.day) + " " + Competitions.Name(f.comp) + " " + f.round + " · " + where + " vs " + ClubName(f.Other(club));
        }

        // ------------------------------------------------------------ next season --

        /// <summary>This season's league order and cup finalists, for the next one. Only once it is over.</summary>
        public PreviousSeason Summary()
        {
            var p = new PreviousSeason { cupWinner = cupWinner, cupRunnerUp = cupRunnerUp };
            foreach (TableRow r in Table(CompetitionId.League)) p.leagueOrder.Add(r.club);
            return p;
        }

        /// <summary>The next season, 52 weeks on, the same clubs; its continental places come from this table.</summary>
        public Season Next(int seed)
        {
            return new Season(number + 1, startDate.AddDays(364), clubs, Summary(), seed, playerRank);
        }
    }
}
