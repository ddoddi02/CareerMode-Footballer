using UnityEngine;

namespace Prototype
{
    public enum Side { Home, Away }

    /// <summary>
    /// Which side has the ball, and therefore which brain every player is running.
    ///
    /// Until this existed the two teams were not really two teams. Home was an attack
    /// and away was a defence, permanently: home had only attacking brains, away had
    /// only defending ones, and winning the ball back ended the round, because there was
    /// nobody on the away side capable of doing anything with it. That is a drill with
    /// eleven people in it, not a match.
    ///
    /// Now every outfield player carries BOTH brains - an AttackerAI and a DefenderAI on
    /// the same body - and each side has both coordinators, a TeamAttack and a
    /// TeamDefence. This decides which half of each is switched on. Whoever has the ball
    /// attacks; the other side defends; win it back and the whole pitch turns over.
    ///
    /// Reusing both brains unchanged, rather than merging them into one, is deliberate.
    /// Each one is a pile of measured, documented behaviour - the press chain, the
    /// control grid, the off-ball scoring - and a merge would re-derive all of it. Two
    /// components that are never on at the same time cost one CharacterController and
    /// nothing else.
    ///
    /// Two independent switches exist on each body and they must not be confused:
    ///
    ///   enabled   WHICH brain is running    - owned here
    ///   active    whether it is FROZEN      - owned by the benches (PassLab, PressDrill)
    ///
    /// Using `active` for both would have the benches and the turnover fighting over one
    /// flag, and whichever wrote last would win.
    /// </summary>
    public class Possession : MonoBehaviour
    {
        [Header("The two sides")]
        public TeamAttack homeAttack;
        public TeamDefence homeDefence;
        public TeamAttack awayAttack;
        public TeamDefence awayDefence;

        [Header("Kick-off")]
        [Tooltip("Who has it when play starts. The possession starts with home's centre-back (PROJECT.md 6), so home.")]
        public Side startsWith = Side.Home;

        /// <summary>The side with the ball, which is the side attacking.</summary>
        public Side InPossession { get; private set; }

        /// <summary>The coordinator currently running the attack, whichever side that is.</summary>
        public TeamAttack Attacking { get { return InPossession == Side.Home ? homeAttack : awayAttack; } }

        /// <summary>The coordinator currently running the defence.</summary>
        public TeamDefence Defending { get { return InPossession == Side.Home ? awayDefence : homeDefence; } }

        /// <summary>Bumped on every turnover, so a listener can tell one happened.</summary>
        public int Turnovers { get; private set; }

        void Start()
        {
            Apply(startsWith);
        }

        /// <summary>
        /// Hand the whole pitch to one side. Idempotent - asking for the side that already
        /// has it re-applies the switches and changes nothing else, which is what a
        /// restart wants.
        /// </summary>
        public void Apply(Side side)
        {
            if (side != InPossession) Turnovers++;
            InPossession = side;

            bool home = side == Side.Home;

            SetCoordinator(homeAttack, home);
            SetCoordinator(awayDefence, home);
            SetCoordinator(awayAttack, !home);
            SetCoordinator(homeDefence, !home);

            // Every body on the attacking side runs its AttackerAI; every body on the
            // defending side runs its DefenderAI. The attacking side is read off the
            // coordinator's own member list, so a body is never asked which team it is
            // on - it is on whichever team lists it.
            SetBrains(homeAttack, homeDefence, home);
            SetBrains(awayAttack, awayDefence, !home);
        }

        static void SetCoordinator(Behaviour c, bool on)
        {
            if (c != null) c.enabled = on;
        }

        static void SetBrains(TeamAttack atk, TeamDefence def, bool attacking)
        {
            if (atk != null && atk.members != null)
                for (int i = 0; i < atk.members.Length; i++)
                    if (atk.members[i] != null) atk.members[i].enabled = attacking;

            if (def != null && def.members != null)
                for (int i = 0; i < def.members.Length; i++)
                    if (def.members[i] != null) def.members[i].enabled = !attacking;
        }

        // ------------------------------------------------------------- lookups --

        /// <summary>
        /// Home's attack - the side the human plays on. For tools that used to ask for
        /// "the" TeamAttack: with one per side that question has two answers, and
        /// FindAnyObjectByType would pick between them at random.
        /// </summary>
        public static TeamAttack FindHomeAttack()
        {
            var all = FindObjectsByType<TeamAttack>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++) if (all[i].attacksPositiveZ) return all[i];
            return all.Length > 0 ? all[0] : null;
        }

        /// <summary>The defence the human attacks against - away's, protecting +Z.</summary>
        public static TeamDefence FindAwayDefence()
        {
            var all = FindObjectsByType<TeamDefence>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++) if (all[i].defendsPositiveZ) return all[i];
            return all.Length > 0 ? all[0] : null;
        }
    }
}
