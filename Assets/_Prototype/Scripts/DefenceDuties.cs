using UnityEngine;

namespace Prototype
{
    /// <summary>How hard the side is going after the ball right now.</summary>
    public enum PressIntensity
    {
        High,   // they are playing out from the back and we are going after it
        Mid,    // the ball is in midfield; hold the block, press what comes near
        Low     // the ball is on us; drop off and defend the box
    }

    /// <summary>Everything a duty needs to know, gathered once per picture.</summary>
    public struct DutyPicture
    {
        public Vector3[] opp;            // where the opposition were, projected forward
        public Role[] oppRole;
        public bool[] oppTaken;          // already picked up by somebody
        public int carrier;              // index of the man on the ball, -1 if loose
        public bool defendsPositiveZ;
        public bool onBreak;
        public PressIntensity press;
        public float maxTravel;          // how far a man will go to pick somebody up
    }

    /// <summary>
    /// WHO each man is responsible for.
    ///
    /// The shared principles in <see cref="TeamDefence"/> say when to go tight, how far
    /// apart to stand and how deep the line sits. They deliberately do not say who to
    /// mark, because that is the one part of defending that is not shared: a full-back
    /// and a centre-back standing in the same picture are looking at different people.
    ///
    ///   striker     high press - hunt whoever is playing it out: keeper, centre-backs,
    ///               full-backs. Dropped off - come back in and defend alongside the
    ///               midfield instead
    ///   winger      the full-back on his side, in step with the striker: if the striker
    ///               has dropped, so has he, or the press has a hole where he was
    ///   midfield    the nearest opposing midfielder inside his own area
    ///   centre-back the forwards. One takes the striker; the OTHER one is then not
    ///               allowed to take a second forward - he watches the box, because the
    ///               man who hurts you is the one arriving into it, not the one already
    ///               marked
    ///   full-back   the winger on his side
    ///
    /// Nobody is picked twice: an opponent taken by one duty is off the board for the
    /// rest. Without that the whole back line marks the same striker, which is what
    /// "each man picks his nearest" always degenerates into.
    /// </summary>
    public static class DefenceDuties
    {
        /// <summary>
        /// Assign every man his target. `targets` comes back holding an opponent index
        /// per member, or -1 for "nobody - hold your station".
        ///
        /// Order matters here in one place only: the centre-backs are resolved as a PAIR
        /// and before everyone else, because the second one's job is defined by what the
        /// first one did.
        /// </summary>
        public static void Assign(Role[] mine, Vector3[] stations, ref DutyPicture p,
                                  int[] targets, float[] depthCap)
        {
            int n = mine.Length;
            for (int i = 0; i < n; i++) { targets[i] = -1; depthCap[i] = float.NaN; }
            for (int k = 0; k < p.oppTaken.Length; k++) p.oppTaken[k] = false;

            CentreBacks(mine, stations, ref p, targets);

            // The chain is a BUILD-UP press. It only means anything while they are
            // actually playing out from the back, which is what High says.
            if (p.press == PressIntensity.High)
                PressChain(mine, stations, ref p, targets, depthCap);

            Positional(mine, stations, ref p, targets);
        }

        // ------------------------------------------------------------ positional --

        /// <summary>
        /// Everybody still without a man gets his opposite number, by ROLE and by SIDE.
        ///
        /// This is the part that was missing, and the symptom was not double-marking - it
        /// was the reverse. In a mid block the opposing back four sits about 28 m away,
        /// every duty here was gated on maxTravel (26 m), so nobody could be given
        /// anybody: eight of ten defenders came back with no man at all. Nothing then
        /// anchored them, the block's ball-side shift slid them all toward the ball, and
        /// the winger ended up seventeen metres infield. Ten men converging on the middle
        /// is what "no duty" looks like from the outside, not what "too much duty" looks
        /// like.
        ///
        /// So there is NO RANGE GATE on being given a man. Distance already has its say
        /// downstream and it says it properly: TeamDefence goes tight only when the man
        /// is inside your zone, and leans the rest of the time. Being given a man is not
        /// the same as being sent after him - which the code has claimed all along while
        /// quietly refusing to give anyone a man he could not reach.
        ///
        /// Sides are judged by world x, for the same reason as the chain: the two teams
        /// mirror, so a role label is not a side.
        ///
        ///   winger      the full-back on his side          full-back   the winger on his
        ///   midfielder  the midfielder on his side         striker     their pivot
        ///
        /// The striker taking the pivot is what a front three does when it is not
        /// pressing the back four: stand in front of the man they want to play through.
        /// His station sits at x = 0, so asking for the central midfielder nearest his
        /// own x picks the pivot without naming it.
        /// </summary>
        static void Positional(Role[] mine, Vector3[] stations, ref DutyPicture p, int[] targets)
        {
            // Wingers and full-backs first: they own the two widest men on each side, and
            // resolving them before midfield stops a drifting midfielder claiming a
            // winger and dragging the middle apart.
            // Dropped off, a winger does NOT keep a full-back. His station has already
            // been pulled 9 m back (3.15) and leaning toward a full-back who is now high
            // up the pitch would drag him straight back out of the block - cancelling the
            // drop with the duty. Deep, he defends alongside the midfield instead.
            Filter wide = p.press == PressIntensity.Low ? Filter.Midfield : Filter.FullBack;

            Claim(mine, stations, ref p, targets, Formation.IsWinger, wide);
            Claim(mine, stations, ref p, targets, Formation.IsFullBack, Filter.Winger);

            // The striker claims BEFORE the midfield, and that order is the whole shape
            // of a mid block. Four of ours want one of three central opponents, so
            // somebody ends up without a man - and it must not be the striker. His job is
            // to stand in front of the pivot; a striker with nobody drifts wherever the
            // block drifts, which is how the front three ended up in the middle.
            Claim(mine, stations, ref p, targets, Formation.IsStriker, Filter.Midfield);
            Claim(mine, stations, ref p, targets, IsCentreMid, Filter.Midfield);

            // Our own pivot goes last, so HE is the one left over. That is not a gap: a
            // six with nobody to mark is screening the space in front of the back four,
            // which is what he is for.
            Claim(mine, stations, ref p, targets, Formation.IsPivot, Filter.Midfield);

            // Anyone still empty - a spare centre-back with nobody dangerous, or a man
            // whose counterpart has already been taken - holds his station. That is a
            // real answer, not a gap: see CentreBacks.
        }

        static bool IsCentreMid(Role r) { return r == Role.LCM || r == Role.RCM; }

        static void Claim(Role[] mine, Vector3[] stations, ref DutyPicture p, int[] targets,
                          System.Func<Role, bool> isMine, Filter want)
        {
            for (int i = 0; i < mine.Length; i++)
            {
                if (targets[i] >= 0 || !isMine(mine[i])) continue;
                int k = CounterpartByX(ref p, want, stations[i].x);
                if (k < 0) continue;
                targets[i] = k;
                p.oppTaken[k] = true;
            }
        }

        /// <summary>
        /// The unclaimed opponent of this kind standing nearest this x.
        ///
        /// Across the pitch, not across the ground: the man on your side is your man
        /// whether he is ten metres away or forty. Judging it by true distance is what
        /// lets a deep block hand the same central opponent to three different people
        /// while the touchlines go unwatched.
        /// </summary>
        static int CounterpartByX(ref DutyPicture p, Filter f, float x)
        {
            int best = -1;
            float bd = float.MaxValue;
            for (int k = 0; k < p.opp.Length; k++)
            {
                if (p.oppTaken[k] || !Matches(f, p.oppRole[k])) continue;
                float d = Mathf.Abs(p.opp[k].x - x);
                if (d < bd) { bd = d; best = k; }
            }
            return best;
        }

        // ----------------------------------------------------------- press chain --

        /// <summary>
        /// The front three and the full-backs, resolved as ONE decision instead of five.
        ///
        /// This is the 4-3-3 against 4-3-3 sequence, and the reason it cannot be five
        /// independent "pick your nearest" rules is that every step is defined by the
        /// step before it:
        ///
        ///   1  the striker goes to a CENTRE-BACK - the one on the ball for preference,
        ///      because pressing the man about to pass is the only press that does
        ///      anything
        ///   2  that leaves the other centre-back free, and the winger on THAT side comes
        ///      inside to take him. The far one, not the near one: the near one is
        ///      already the reason the ball went the other way
        ///   3  that winger has left an opposing full-back unattended, so OUR full-back
        ///      on that side steps up to take him
        ///   4  and stops there. The man he walked away from is the opposing winger, and
        ///      a full-back who goes past him has swapped one free man for another. He
        ///      stands level with him - that is what depthCap carries.
        ///
        /// The full-back who did not have to rotate stays with his winger, and "stays
        /// with" means watching rather than pressing: TeamDefence only goes tight when
        /// the man is inside his zone, so a winger held at arm's length produces a lean
        /// and not a duel.
        ///
        /// Dropped off (Low) none of it runs. The front three are coming back to defend
        /// and there is no press left to sequence.
        /// </summary>
        static void PressChain(Role[] mine, Vector3[] stations, ref DutyPicture p,
                               int[] targets, float[] depthCap)
        {
            if (p.press == PressIntensity.Low) return;

            int st = FindStriker(mine, targets);
            if (st < 0) return;

            // --- 1. the striker takes a centre-back --------------------------------
            int pressed = -1;
            if (p.carrier >= 0 && !p.oppTaken[p.carrier]
                && Formation.IsCentreBack(p.oppRole[p.carrier]))
                pressed = p.carrier;
            if (pressed < 0)
                pressed = NearestOpponent(stations[st], ref p, Filter.CentreBack, p.maxTravel);

            // No centre-back within reach - the press is not on. Leave him, and leave the
            // rest of the chain alone too: it used to return here, which silently
            // cancelled the winger and full-back steps as well and sent everybody into
            // the positional fallback with no man. Positional() picks them all up now.
            if (pressed < 0) return;

            targets[st] = pressed;
            Claim(ref p, pressed);

            // --- 2. the far winger takes the other centre-back ---------------------
            int free = NearestOpponent(p.opp[pressed], ref p, Filter.CentreBack, 999f);
            int stepIn = -1;
            if (free >= 0)
            {
                // "The winger on that side", decided by where the free man actually is
                // rather than by role name. The two sides mirror between the teams and
                // matching on the label gets it backwards half the time.
                stepIn = NearestWingerByX(mine, stations, targets, p.opp[free].x);
                if (stepIn >= 0) { targets[stepIn] = free; Claim(ref p, free); }
            }

            // --- 3 and 4. the full-backs -------------------------------------------
            for (int i = 0; i < mine.Length; i++)
            {
                if (targets[i] >= 0 || !Formation.IsFullBack(mine[i])) continue;

                int myWinger = NearestWingerByX(mine, stations, null, stations[i].x);
                bool rotated = myWinger >= 0 && myWinger == stepIn;

                if (rotated)
                {
                    int theirs = NearestOpponent(stations[i], ref p, Filter.FullBack, 999f);
                    if (theirs >= 0)
                    {
                        targets[i] = theirs;
                        Claim(ref p, theirs);
                        depthCap[i] = WingerLine(ref p, stations[i].x);
                        continue;
                    }
                }

                // Nobody rotated onto him: he keeps the winger on his side in view.
                int w = NearestOpponent(stations[i], ref p, Filter.Winger, 999f);
                if (w >= 0) { targets[i] = w; Claim(ref p, w); }
            }
        }

        static void Claim(ref DutyPicture p, int k)
        {
            if (k >= 0) p.oppTaken[k] = true;
        }

        static int FindStriker(Role[] mine, int[] targets)
        {
            for (int i = 0; i < mine.Length; i++)
                if (targets[i] < 0 && Formation.IsStriker(mine[i])) return i;
            return -1;
        }

        /// <summary>
        /// Our winger standing nearest this x. Pass `targets` to skip men already given a
        /// job, or null to ask purely "whose side is this".
        /// </summary>
        static int NearestWingerByX(Role[] mine, Vector3[] stations, int[] targets, float x)
        {
            int best = -1;
            float bd = float.MaxValue;
            for (int i = 0; i < mine.Length; i++)
            {
                if (!Formation.IsWinger(mine[i])) continue;
                if (targets != null && targets[i] >= 0) continue;
                float d = Mathf.Abs(stations[i].x - x);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// How far up the opposing winger on this side is standing. A full-back who has
        /// rotated inside may not go beyond this line: past it the winger he left is
        /// simply free, and he has swapped one loose man for another.
        ///
        /// NaN when there is no winger on that side to stand level with - which means no
        /// cap at all, not a cap at zero.
        /// </summary>
        static float WingerLine(ref DutyPicture p, float x)
        {
            float bd = float.MaxValue;
            float z = float.NaN;
            for (int k = 0; k < p.opp.Length; k++)
            {
                if (!Formation.IsWinger(p.oppRole[k])) continue;
                float d = Mathf.Abs(p.opp[k].x - x);
                if (d < bd) { bd = d; z = p.opp[k].z; }
            }
            return z;
        }

        // --------------------------------------------------------- centre-backs --

        /// <summary>
        /// One goes to the striker. The other explicitly does NOT go to another forward -
        /// he holds and watches for a runner arriving into the box.
        ///
        /// Both chasing forwards is how a back four gets stretched into two duels and a
        /// hole in between, and the runner nobody was watching arrives into that hole.
        /// </summary>
        static void CentreBacks(Role[] mine, Vector3[] stations, ref DutyPicture p, int[] targets)
        {
            int a = -1, b = -1;
            for (int i = 0; i < mine.Length; i++)
            {
                if (!Formation.IsCentreBack(mine[i])) continue;
                if (a < 0) a = i; else if (b < 0) b = i;
            }
            if (a < 0) return;

            // Whoever is nearest the opposing striker takes him.
            int st = NearestOpponent(stations[a], ref p, Filter.Striker, p.maxTravel);
            int stB = b >= 0 ? NearestOpponent(stations[b], ref p, Filter.Striker, p.maxTravel) : -1;

            int taker = a, spare = b;
            if (st >= 0 && stB >= 0 && st == stB)
            {
                float da = Flat(p.opp[st] - stations[a]).sqrMagnitude;
                float db = Flat(p.opp[stB] - stations[b]).sqrMagnitude;
                if (db < da) { taker = b; spare = a; }
            }
            else if (st < 0 && stB >= 0) { taker = b; spare = a; st = stB; }

            if (st >= 0)
            {
                targets[taker] = st;
                p.oppTaken[st] = true;
            }

            if (spare < 0) return;

            // The spare one watches the box - the man arriving, not the man already held.
            int runner = BoxRunner(ref p, p.maxTravel, stations[spare]);
            if (runner >= 0)
            {
                targets[spare] = runner;
                p.oppTaken[runner] = true;
            }
        }

        // ------------------------------------------------------------- searching --

        enum Filter { Any, Striker, Winger, FullBack, CentreBack, Midfield, BuildUp, Forward }

        static bool Matches(Filter f, Role r)
        {
            switch (f)
            {
                case Filter.Striker: return Formation.IsStriker(r);
                case Filter.Winger: return Formation.IsWinger(r);
                case Filter.FullBack: return Formation.IsFullBack(r);
                case Filter.CentreBack: return Formation.IsCentreBack(r);
                case Filter.Midfield: return Formation.IsMidfield(r);
                case Filter.BuildUp: return Formation.IsBuildUp(r);
                case Filter.Forward: return Formation.IsForward(r);
                default: return !Formation.IsKeeper(r);
            }
        }

        static int NearestOpponent(Vector3 from, ref DutyPicture p, Filter f, float maxRange)
        {
            int best = -1;
            float bd = maxRange * maxRange;
            for (int k = 0; k < p.opp.Length; k++)
            {
                if (p.oppTaken[k] || !Matches(f, p.oppRole[k])) continue;
                float d = Flat(p.opp[k] - from).sqrMagnitude;
                if (d < bd) { bd = d; best = k; }
            }
            return best;
        }

        /// <summary>
        /// The unmarked man who would hurt us most from where he is - the spare
        /// centre-back's job.
        ///
        /// Ranked by Danger() rather than by raw distance to the goal, and gated by it
        /// too. Distance alone sends him to whoever is furthest forward, and the man
        /// furthest forward is often a winger standing on the touchline: the centre-back
        /// then leaves the middle to go and stand in a channel that the full-back is
        /// already covering, which is the exact hole the runner arrives into.
        ///
        /// If nobody is dangerous he takes NOBODY. A spare centre-back with no runner to
        /// watch is supposed to be spare.
        /// </summary>
        static int BoxRunner(ref DutyPicture p, float maxTravel, Vector3 from)
        {
            int best = -1;
            float bestDanger = MinimumDanger;

            for (int k = 0; k < p.opp.Length; k++)
            {
                if (p.oppTaken[k] || Formation.IsKeeper(p.oppRole[k])) continue;
                if (Flat(p.opp[k] - from).magnitude > maxTravel) continue;

                float danger = TacticalPitch.Danger(p.opp[k], p.defendsPositiveZ);
                if (danger > bestDanger) { bestDanger = danger; best = k; }
            }
            return best;
        }

        /// <summary>Below this an opponent is not worth breaking the back line for.</summary>
        public const float MinimumDanger = 0.30f;

        // ---------------------------------------------------------------- press ---

        /// <summary>
        /// How hard to go, from where the ball is. Their build-up area is a press
        /// trigger; our own third is not, because a high line with the ball on the edge
        /// of our box is just a bigger goal to defend.
        /// </summary>
        public static PressIntensity IntensityFor(Vector3 ballPos, bool defendsPositiveZ,
                                                  float highBeyond, float lowWithin)
        {
            Vector3 goal = TacticalPitch.GoalCentre(defendsPositiveZ);
            float d = Mathf.Abs(goal.z - ballPos.z);
            if (d > highBeyond) return PressIntensity.High;
            if (d < lowWithin) return PressIntensity.Low;
            return PressIntensity.Mid;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
