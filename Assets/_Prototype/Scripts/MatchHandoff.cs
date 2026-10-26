namespace Prototype
{
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
        public static void Request(string opponent, string competitionName, bool home, string returnTo)
        {
            requested = true;
            opponentName = opponent;
            competition = competitionName;
            isHome = home;
            returnScene = returnTo;
            clubGoals = opponentGoals = 0;
            finished = false;
            returnToCaller = null;
        }

        public static void Clear()
        {
            requested = finished = false;
            returnToCaller = null;
            opponentName = competition = returnScene = null;
            clubGoals = opponentGoals = 0;
            isHome = false;
        }
    }
}
