using System;
using System.Collections.Generic;
using UnityEngine;

namespace Career
{
    /// <summary>The competitions of a season (user, 2026-10-26): the league, the domestic cup and super cup, and three continental ones. Generic names, no real ones.</summary>
    public enum CompetitionId { League, Cup, SuperCup, Continental1, Continental2, Continental3 }

    public static class Competitions
    {
        public static string Name(CompetitionId c)
        {
            switch (c)
            {
                case CompetitionId.League: return "리그";
                case CompetitionId.Cup: return "컵 대회";
                case CompetitionId.SuperCup: return "슈퍼컵";
                case CompetitionId.Continental1: return "대륙 간 컵 대회 1부";
                case CompetitionId.Continental2: return "대륙 간 컵 대회 2부";
                default: return "대륙 간 컵 대회 3부";
            }
        }

        public static bool IsContinental(CompetitionId c)
        {
            return c == CompetitionId.Continental1 || c == CompetitionId.Continental2 || c == CompetitionId.Continental3;
        }
    }

    /// <summary>One match of the calendar. <see cref="day"/> counts from 1 on the Saturday the season opens (<see cref="Season"/>).</summary>
    public class Fixture
    {
        public int day;
        public CompetitionId comp;
        public string round;
        public string home, away;
        /// <summary>A final or the like: nobody has the ground.</summary>
        public bool neutral;
        /// <summary>Counts in a table: a league match, or one of a continental league phase.</summary>
        public bool table;
        public bool played;
        public int homeGoals, awayGoals;
        /// <summary>The side that went through on penalties, when a tie was level after its last match.</summary>
        public string penWinner;

        public bool Involves(string club) { return home == club || away == club; }
        public string Other(string club) { return home == club ? away : home; }
    }

    /// <summary>
    /// A knockout tie: one match, or two. <see cref="a"/> has the home of the first match.
    /// </summary>
    public class Tie
    {
        public string a, b;
        public Fixture first, second;
        public string winner;

        public bool Done { get { return first.played && (second == null || second.played); } }
    }

    /// <summary>A line of a table.</summary>
    public class TableRow
    {
        public string club;
        public int played, won, drawn, lost, goalsFor, goalsAgainst;

        public int Points { get { return won * 3 + drawn; } }
        public int GoalDifference { get { return goalsFor - goalsAgainst; } }
    }

    /// <summary>
    /// Results of matches nobody plays yet (user: the other matches get a result drawn from the
    /// clubs' strength, and so does the player's club until the match is connected).
    /// PROVISIONAL (PROJECT.md 10.13).
    /// </summary>
    public static class MatchSim
    {
        /// <summary>Goals expected when the sides are level (home a little more), and how fast strength moves them.</summary>
        public const double HomeGoals = 1.35, AwayGoals = 1.10, PerStrengthPoint = 0.02, HomeEdge = 0.10;

        public static void Play(Fixture f, Club home, Club away, System.Random rng)
        {
            double diff = home.Strength - away.Strength;
            double lh = HomeGoals * Math.Exp(PerStrengthPoint * diff + (f.neutral ? 0.0 : HomeEdge));
            double la = AwayGoals * Math.Exp(-PerStrengthPoint * diff);
            f.homeGoals = Poisson(Math.Min(4.0, Math.Max(0.2, lh)), rng);
            f.awayGoals = Poisson(Math.Min(4.0, Math.Max(0.2, la)), rng);
            f.played = true;
        }

        /// <summary>Who goes through a level tie: a toss weighted a little by strength.</summary>
        public static bool FirstWinsPenalties(Club a, Club b, System.Random rng)
        {
            double p = Math.Min(0.75, Math.Max(0.25, 0.5 + (a.Strength - b.Strength) / 400.0));
            return rng.NextDouble() < p;
        }

        static int Poisson(double lambda, System.Random rng)
        {
            double limit = Math.Exp(-lambda), p = 1.0;
            int k = 0;
            do { k++; p *= rng.NextDouble(); } while (p > limit);
            return k - 1;
        }
    }
}
