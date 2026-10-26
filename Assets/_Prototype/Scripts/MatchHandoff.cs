using System.Collections.Generic;

namespace Prototype
{
    /// <summary>One player as the career side hands him over: who plays where and what he can do.</summary>
    public class PlayerSheet
    {
        /// <summary>The <see cref="Formation.F4123"/> label he plays (the human is the DM).</summary>
        public string slot;
        public string name;
        /// <summary>Career.CreationRules.Stats ids -> 1..100, body bonus already added.</summary>
        public Dictionary<string, int> stats;
        /// <summary>Personal form, 60..120.</summary>
        public int formPercent = 100;

        public int Stat(string id, int fallback)
        {
            int v;
            return stats != null && stats.TryGetValue(id, out v) ? v : fallback;
        }
    }

    /// <summary>A chemistry change the match earned: both slots, and how much (+1 call answered, -1 ignored, +2 goal together).</summary>
    public class ChemistryDelta
    {
        public string slotA, slotB;
        public int delta;
    }

    /// <summary>
    /// What the career side and the match scene pass each other (manager, 2026-10-26): data
    /// only, nothing in it runs. The daily map fills the first fields and opens the match scene
    /// ("play it yourself"); the match scene plays the match, writes the result and goes back to
    /// <see cref="returnScene"/>, where the career side reads it. Static, so it lives across
    /// the scene change.
    /// </summary>
    public static class MatchHandoff
    {
        /// <summary>The career side asked for this match and has not had the result yet.</summary>
        public static bool requested;
        public static string opponentName;
        /// <summary>The competition's name, for the scoreboard.</summary>
        public static string competition;
        /// <summary>The player's club has the home of it.</summary>
        public static bool isHome;
        /// <summary>The scene to go back to when the match is over (path or name).</summary>
        public static string returnScene;

        /// <summary>The career side fills these at <see cref="Request"/>: 11 sheets each, slot = Formation.F4123 label. The match only reads them; empty = the drill's own defaults.</summary>
        public static PlayerSheet[] club, opponent;
        /// <summary>The club's chemistry between two slots, 0..100 (the career side's Squad). Null = 50 everywhere.</summary>
        public static System.Func<string, string, int> chemistry;

        /// <summary>The match scene fills this with the chemistry it earned; the career side applies it.</summary>
        public static readonly List<ChemistryDelta> chemistryDeltas = new List<ChemistryDelta>();

        /// <summary>The match scene sets these when the match is over, from the player's club's side.</summary>
        public static int clubGoals, opponentGoals;
        /// <summary>The match is over and the goals above are its result.</summary>
        public static bool finished;

        /// <summary>
        /// How the match scene goes back: the caller puts it in at <see cref="Request"/> so the
        /// match side never calls the career code. Null = nowhere to go (the scene was opened by hand).
        /// </summary>
        public static System.Action returnToCaller;

        /// <summary>A new request: nothing finished, no goals.</summary>
        public static void Request(string opponentClubName, string competitionName, bool home, string returnTo)
        {
            requested = true;
            opponentName = opponentClubName;
            competition = competitionName;
            isHome = home;
            returnScene = returnTo;
            clubGoals = opponentGoals = 0;
            finished = false;
            returnToCaller = null;
            club = opponent = null;
            chemistry = null;
            chemistryDeltas.Clear();
        }

        public static void Clear()
        {
            requested = finished = false;
            returnToCaller = null;
            club = opponent = null;
            chemistry = null;
            chemistryDeltas.Clear();
            opponentName = competition = returnScene = null;
            clubGoals = opponentGoals = 0;
            isHome = false;
        }
    }
}
