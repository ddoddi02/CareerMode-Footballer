using UnityEngine;

namespace Prototype
{
    /// <summary>How hard the side is going after the ball right now.</summary>
    public enum PressIntensity
    {
        High,   // they are playing out from the back and we are going after it
        Mid,    // the ball is in midfield
        Low     // the ball is on us
    }

    /// <summary>
    /// The defending side: every man follows one opposition slot, goal-side, and the man
    /// marking the ball goes in for it.
    ///
    /// That is the whole defence, and it is deliberately that small. It used to be a
    /// zonal block - slide with the ball, hold the line, compress, cover the holes, a
    /// pressing sequence on their back four, a time-to-arrival map of the pitch, a man
    /// sent to cut every pass out - all recomputed off a one-second-old picture. It was
    /// measured, documented, and it was not what felt right to play against. Man-marking
    /// was. So man-marking is the structure now, and the zonal machinery is gone rather
    /// than switched off (PROJECT.md 3.15).
    ///
    /// WHO MARKS WHOM is a fixed table, Formation.MarksRole: each slot takes the
    /// opposition slot that faces it - full-back on winger, mezzala on mezzala, our pivot
    /// on their striker and their pivot under our striker. The centre-backs take nobody
    /// and hold, unless the side is in a high press (allowHighPress), when the one nearer
    /// their striker steps onto him and our pivot is released.
    ///
    /// WHERE he stands is read off the opponent's REAL position every frame, not a
    /// snapshot. A marker a second behind his man is not marking him.
    ///
    /// GOING FOR THE BALL is the tackle (TickTackle), and nothing else. A marker already
    /// stands goal-side at tackling distance, so when his man receives, the man in range
    /// is usually his own marker - which is what makes the tackle fit this structure
    /// instead of fighting it. Running onto a pass in flight was removed with the rest:
    /// it takes a man off his mark, and the mark is the structure.
    /// </summary>
    public class TeamDefence : MonoBehaviour
    {
        [Header("Refs")]
        public Transform ball;
        [Tooltip("The ball itself - needed to tell who is on it and to knock it loose.")]
        public Ball ballBody;
        public Transform[] opponents;
        public DefenderAI[] members;

        [Tooltip("True if this side defends the goal at +Z.")]
        public bool defendsPositiveZ = true;

        [Header("Marking")]
        [Tooltip("How far goal-side of his man a marker stands. Inside tackling range on purpose: when his man receives, he is the one close enough to go in.")]
        public float markGap = 1.7f;
        [Tooltip("A second man on the same opponent stands this much further goal-side than the first, so the two are not stacked on one spot.")]
        public float doubleMarkGap = 2.5f;
        [Tooltip("How tight a marker gets once HIS man has the ball. At markGap the ball, which sits in front of the man, is 2.1-2.3 m away - outside tightRange, so the turn-in trigger could never fire and the tackle was decoration. Set it equal to markGap to switch this off.")]
        public float onBallGap = 1.0f;

        [Header("Press")]
        [Tooltip("Ball this far from our goal or beyond is their build-up: a high press, if allowed.")]
        public float highPressBeyond = 68f;
        [Tooltip("Ball inside this of our goal is a low block. Read by the labels only - the marking does not change with it.")]
        public float lowPressWithin = 34f;
        [Tooltip("Let the side go into a high press. In one, the centre-back nearer their striker steps onto him and our pivot is released. Off, a ball in their build-up is treated as Mid.")]
        public bool allowHighPress = false;

        /// <summary>The man marking whoever is on the ball, if they are one of ours to mark.</summary>
        public DefenderAI Presser { get; private set; }

        /// <summary>Challenges this side has made on a bot carrier, and how many won the ball.</summary>
        public int TacklesTried { get; private set; }
        public int TacklesWon { get; private set; }

        /// <summary>How hard the side is going after it right now.</summary>
        public PressIntensity Press { get; private set; }

        /// <summary>What the ball's position alone asks for, before allowHighPress caps it.</summary>
        public PressIntensity PressWanted { get; private set; }

        /// <summary>Who member i is marking, as an index into `opponents`, or -1.</summary>
        public int DutyOf(int i)
        {
            return (targets != null && i >= 0 && i < targets.Length) ? targets[i] : -1;
        }

        int[] targets = new int[0];

        /// <summary>The centre-back who stepped onto the striker in a high press.</summary>
        DefenderAI steppedCB;

        void Update()
        {
            if (ball == null || members == null || members.Length == 0) return;
            Mark();
            TickTackle();
        }

        // --------------------------------------------------------------- marking --

        /// <summary>
        /// Every frame, off the opponents' real positions: find the man whose slot this
        /// one is told to mark, and stand goal-side of him.
        /// </summary>
        void Mark()
        {
            int n = members.Length;
            if (targets.Length != n) targets = new int[n];

            Vector3 goal = TacticalPitch.GoalCentre(defendsPositiveZ);
            int m = opponents != null ? opponents.Length : 0;

            PressWanted = IntensityFor(ball.position, defendsPositiveZ, highPressBeyond, lowPressWithin);
            Press = PressWanted == PressIntensity.High && !allowHighPress ? PressIntensity.Mid : PressWanted;

            // High press: ONE centre-back takes their striker, and the pivot lets him go.
            // Chosen once, when the press starts - picking the nearer man every frame
            // would swap them back and forth whenever the striker drifts across.
            if (Press != PressIntensity.High) steppedCB = null;
            else if (steppedCB == null)
            {
                Transform st = null;
                for (int k = 0; k < m; k++)
                    if (opponents[k] != null && Formation.RoleOf(opponents[k]) == Role.ST) { st = opponents[k]; break; }
                float best = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    if (members[i] == null || !Formation.IsCentreBack(members[i].role)) continue;
                    float d = st != null ? Flat(members[i].transform.position - st.position).sqrMagnitude : i;
                    if (d < best) { best = d; steppedCB = members[i]; }
                }
            }

            int carrier = CarrierIndex();
            Presser = null;

            for (int i = 0; i < n; i++)
            {
                DefenderAI me = members[i];
                targets[i] = -1;
                if (me == null) continue;

                // He reads which goal is ours when he judges a turn (IsTurningIn).
                me.defendsPositiveZ = defendsPositiveZ;

                Role? want = Formation.MarksRole(me.role);
                if (steppedCB != null)
                {
                    if (me == steppedCB) want = Role.ST;
                    else if (me.role == Role.DM) want = null;
                }
                if (want.HasValue)
                    for (int k = 0; k < m; k++)
                        if (opponents[k] != null && Formation.RoleOf(opponents[k]) == want.Value) { targets[i] = k; break; }

                if (targets[i] < 0)
                {
                    me.markTarget = null;
                    me.SetStation(me.homeSlot);
                    continue;
                }

                if (targets[i] == carrier && Presser == null) Presser = me;

                // Somebody earlier in the list already on him? Stand behind that man.
                int ahead = 0;
                for (int j = 0; j < i; j++) if (targets[j] == targets[i]) ahead++;

                // His man has the ball: step tight, so a turn is something he can punish.
                // Only the first man on him - a second marker stays where he was.
                float gap = targets[i] == carrier && ahead == 0 ? onBallGap : markGap;

                Vector3 man = Flat(opponents[targets[i]].position);
                Vector3 toGoal = Flat(goal - man).normalized;
                me.markTarget = man;
                me.SetStation(man + toGoal * (gap + ahead * doubleMarkGap));
            }
        }

        /// <summary>Which of `opponents` is on the ball right now, or -1.</summary>
        int CarrierIndex()
        {
            if (ballBody == null || !ballBody.Carried) return -1;
            Transform c = ballBody.CarrierTransform;
            for (int k = 0; opponents != null && k < opponents.Length; k++)
                if (opponents[k] == c) return k;
            return -1;
        }

        // ---------------------------------------------------------------- tackle --

        /// <summary>
        /// Challenge a BOT on the ball. The human is left to the director, which reads his
        /// shielding input and scores the touch; tackling him from here as well would
        /// challenge him twice.
        ///
        /// WINNING THE TACKLE WINS THE BALL. It used to knock it a metre loose, on the
        /// theory that whoever reached it first should have it. Under man-marking nobody
        /// on the defending side has a loose ball as his job - every man is on a man - so
        /// the side that had just lost the tackle collected it every time: five tackles,
        /// two won, no turnovers. The tackler keeps it, and Possession sees who is on it
        /// now and turns the pitch over.
        /// </summary>
        void TickTackle()
        {
            if (ballBody == null || !ballBody.Carried) return;

            AttackerAI carrier = ballBody.Carrier as AttackerAI;
            if (carrier == null || !IsOpponent(carrier.transform)) return;

            Vector3 bp = ballBody.transform.position;
            DefenderAI d = Challenger(bp);
            if (d == null) return;
            if (!d.WantsTackle(bp, ballBody.Exposure, carrier)) return;

            d.BeganTackle();
            TacklesTried++;

            Tackle.Input ti;
            ti.defenderPos = d.transform.position;
            ti.ballPos = bp;
            ti.attackerPos = carrier.transform.position;
            ti.attackerFacing = carrier.CarrierForward;
            // A bot standing it up is not fighting for it; one on the move is.
            ti.attackerResisting = carrier.Velocity.sqrMagnitude > 1f;
            ti.tackling01 = d.tackling01;
            ti.strength01 = 0.5f;
            ti.reach = Tackle.Reach;

            string why;
            bool shielded;
            TackleResult r = Tackle.Resolve(ti, out why, out shielded);

            if (r == TackleResult.Won)
            {
                TacklesWon++;
                carrier.ReleaseBall();
                // Onto the same body's attacking brain - it is switched off now, and
                // Possession switches it on the moment it reads who has the ball.
                AttackerAI mine = d.GetComponent<AttackerAI>();
                if (mine != null) { ballBody.Attach(mine); mine.TakeBall(); }
                return;
            }

            // A foul is a free kick, and the side that was fouled keeps the ball - so for a
            // bot it changes nothing about possession. He just loses his footing.
            d.MissedTackle();
        }

        /// <summary>
        /// Who gets to put a foot in: whoever is ALREADY within lunging range of the ball,
        /// the man marking the carrier first if he is one of them.
        ///
        /// Nobody is sent anywhere to do this - only a man already there gets a go - so it
        /// cannot pull a second marker off his own man onto the ball.
        /// </summary>
        DefenderAI Challenger(Vector3 ballPos)
        {
            ballPos.y = 0f;
            DefenderAI best = null;
            float bd = float.MaxValue;

            for (int i = 0; members != null && i < members.Length; i++)
            {
                DefenderAI d = members[i];
                if (d == null || !d.enabled || !d.active || d.Recovering) continue;

                Vector3 p = d.transform.position; p.y = 0f;
                float dist = (p - ballPos).magnitude;
                if (dist > d.lungeRange) continue;

                if (d == Presser) return d;          // his own marker, if he is close enough
                if (dist < bd) { bd = dist; best = d; }
            }
            return best;
        }

        bool IsOpponent(Transform t)
        {
            for (int k = 0; opponents != null && k < opponents.Length; k++)
                if (opponents[k] == t) return true;
            return false;
        }

        // ----------------------------------------------------------------- press --

        /// <summary>
        /// How hard to go, from where the ball is. Their build-up area is a press trigger;
        /// our own third is not, because a high line with the ball on the edge of our box
        /// is just a bigger goal to defend.
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
