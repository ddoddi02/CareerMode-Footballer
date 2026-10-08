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
    /// WHEN he marks is the shape (holdShape, HoldShape). The side stands in three lines -
    /// back four, midfield, front three - the back line set off the ball and each of the
    /// others a fixed distance in front of it (holdLines). One man goes out to the
    /// carrier - flat out, and only while the carrier is within his zone - and jogs back
    /// when the ball moves on. Which man: the nearest of the line whose band the carrier
    /// is in (pressByLine), or, off, the carrier's man from the table. The back four also
    /// pick up whoever walks into their zones and follow a run in behind. Off, every man
    /// is on his man all the time, which is the defence as it was. A high press ignores
    /// the shape - a press is a decision to go after them.
    ///
    /// NOBODY GOES BEHIND THE KEEPER. Every order passes through Place, which holds it
    /// keeperClearance in front of him whatever path produced it.
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

        [Header("Hold the shape")]
        [Tooltip("Stand in the shape and send only the man on the ball's marker out to him; the back four also pick up runners in their zones. Off: every man follows his man everywhere, the defence as it was.")]
        [UnityEngine.Serialization.FormerlySerializedAs("useZones")]
        public bool holdShape = true;
        [Tooltip("How far from his slot a man goes out to the ball, and - for the back four - the area they pick runners up in. Back four.")]
        public float backZone = 10f;
        [Tooltip("The same, for the pivot and the mezzalas. Wider than the flanks: at 12 m the middle of the pitch was barely pressed at all.")]
        public float midZone = 16f;
        [Tooltip("The striker, who is out to their pivot. Wide for the same reason as the midfield - it is the centre of the pitch.")]
        public float strikerZone = 16f;
        [Tooltip("The wingers. This is what keeps a winger off a full-back who has the ball deep in his own half.")]
        public float frontZone = 12f;
        [Tooltip("He lets go only this much further out than where he went in. Without it a man on the edge switches him on and off every step.")]
        public float releaseMargin = 4f;
        [Tooltip("How far past a back-four man toward our goal a runner must be to count as in behind, which keeps him on the runner outside his zone.")]
        public float pastMargin = 0.5f;

        [Header("Shape")]
        [Tooltip("How much of the ball's sideways position the waiting shape slides with.")]
        public float lateralFollow = 0.5f;
        public float maxLateralShift = 12f;
        [Tooltip("Slots on the far side from the ball come in to this fraction of their width, so the side does not leave its far flank as wide as ever while the near one fills up.")]
        [Range(0.3f, 1f)] public float farSideTuck = 0.8f;
        [Tooltip("holdLines off only. How much of the ball's position up and down the pitch the kickoff slots slide with.")]
        public float depthFollow = 0.25f;
        public float maxDepthShift = 10f;
        [Tooltip("Keep slots at least this far inside the touchlines and goal lines.")]
        public float pitchInset = 3f;

        [Header("Lines - back four, midfield, front three")]
        [Tooltip("Stand the waiting shape in three lines set off the ball, each a fixed distance in front of the one behind. Off: every man on his kickoff slot slid with the ball, which is a different shape for each side and lets the lines run into each other.")]
        public bool holdLines = true;
        [Tooltip("The back line stands at this fraction of the ball's distance from our goal line.")]
        [Range(0.2f, 1f)] public float backLineFollow = 0.6f;
        [Tooltip("The highest the back line goes, in metres off our goal line. 40 is twelve and a half inside our own half: a mid block, which is all the side plays with allowHighPress off.")]
        public float backLineMax = 40f;
        [Tooltip("Back line to midfield line.")]
        public float backToMid = 12f;
        [Tooltip("Midfield line to front line.")]
        public float midToFront = 12f;
        [Tooltip("The pivot stands this far behind the two eights - still the midfield line, screening in front of the back four, never in it.")]
        public float pivotDrop = 3f;

        [Header("Floor - nobody behind the keeper")]
        [Tooltip("The deepest the back line waits, in metres off our goal line. The whole shape stops here as one piece.")]
        public float lineFloor = 8f;
        [Tooltip("The deepest any outfielder goes for any reason - holding, marking, tracking a run, pressing - in metres in front of the keeper. Only the man going to the ball may go deeper, and only as deep as the ball is.")]
        public float keeperClearance = 1.5f;

        [Header("Press by line")]
        [Tooltip("The line whose band the ball-carrier is standing in sends its nearest man to him. Off: the man the table gives the carrier to (Formation.MarksRole), as before.")]
        public bool pressByLine = true;
        [Tooltip("The line already out to the carrier keeps him until he is this far past the split into the next band, so a carrier on the boundary is not handed back and forth.")]
        public float bandMargin = 3f;

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

        /// <summary>
        /// Member i's zone: his slot as it stands now, and how far from it he will go.
        /// False if he has none - the keeper, or the side is not holding its shape.
        /// </summary>
        public bool ZoneOf(int i, out Vector3 centre, out float radius)
        {
            centre = Vector3.zero; radius = 0f;
            if (!HoldingShape || members == null || i < 0 || i >= members.Length || members[i] == null) return false;
            if (Formation.IsKeeper(members[i].role)) return false;
            centre = Anchor(members[i]);
            radius = ZoneRadius(members[i].role);
            return true;
        }

        /// <summary>Is the side standing in its shape right now, rather than man-marking.</summary>
        public bool HoldingShape { get { return holdShape && Press != PressIntensity.High; } }

        /// <summary>
        /// Where a line stands this frame, as z. By Formation.LineOf: 0 the back four,
        /// 1 the midfield, 2 the front three. With holdLines off it is where the slots put
        /// that line on average.
        /// </summary>
        public float LineZ(int line) { return ZAt(lineDepth[Mathf.Clamp(line, 0, 2)]); }

        /// <summary>The deepest any outfielder of ours may stand, as z (keeperClearance).</summary>
        public float FloorZ { get { return ZAt(HardFloor); } }

        /// <summary>Which line sent the man on the ball (Formation.LineOf), or -1. Press by line only.</summary>
        public int PressLine { get; private set; }

        int[] targets = new int[0];

        /// <summary>Who each member is out on - pressing or tracking - or -1. Kept between frames: that is the hysteresis.</summary>
        int[] engaged = new int[0];

        /// <summary>Which opponents somebody is already on this frame.</summary>
        bool[] taken = new bool[0];

        /// <summary>How far the waiting shape has slid with the ball this frame, and which side the ball is on.</summary>
        Vector3 shift;
        float ballSide;

        /// <summary>Each line's depth this frame, metres off our goal line, by Formation.LineOf.</summary>
        readonly float[] lineDepth = new float[3];

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
            if (engaged.Length != n) { engaged = new int[n]; for (int i = 0; i < n; i++) engaged[i] = -1; }

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
            shift = ShapeShift(ball.position);
            ballSide = ball.position.x;
            MeasureLines();
            Presser = null;
            PressLine = -1;

            for (int i = 0; i < n; i++)
                if (members[i] != null) members[i].defendsPositiveZ = defendsPositiveZ;   // read when he judges a turn

            if (HoldingShape) HoldShape(carrier, goal);
            else ManMark(carrier, goal);
        }

        /// <summary>
        /// Everybody on his man, everywhere: the defence before holdShape, and the one a
        /// high press still uses.
        /// </summary>
        void ManMark(int carrier, Vector3 goal)
        {
            int n = members.Length;
            int m = opponents != null ? opponents.Length : 0;

            for (int i = 0; i < n; i++)
            {
                DefenderAI me = members[i];
                targets[i] = -1;
                engaged[i] = -1;
                if (me == null) continue;
                me.pace = DefendPace.Mark;

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
                    Place(me, me.homeSlot, false);
                    continue;
                }

                if (targets[i] == carrier && Presser == null) Presser = me;

                // Somebody earlier in the list already on him? Stand behind that man.
                int ahead = 0;
                for (int j = 0; j < i; j++) if (targets[j] == targets[i]) ahead++;

                // His man has the ball: step tight, so a turn is something he can punish.
                // Only the first man on him - a second marker stays where he was.
                bool onBall = targets[i] == carrier && ahead == 0;
                float gap = onBall ? onBallGap : markGap;

                Vector3 man = Flat(opponents[targets[i]].position);
                Vector3 toGoal = Flat(goal - man).normalized;
                me.markTarget = man;
                Place(me, man + toGoal * (gap + ahead * doubleMarkGap), onBall);
            }
        }

        // ------------------------------------------------------------ the shape --

        /// <summary>
        /// The side stands in its shape, slid toward the ball, and exactly two kinds of
        /// man leave it:
        ///
        ///   THE PRESSER - the nearest man of the line whose band the carrier is in
        ///   (LinePresser), or with pressByLine off the man the table gives him to. Flat
        ///   out, but only while the carrier is within his zone. A winger does not chase a
        ///   full-back who has it deep in his own half; he goes when the full-back brings
        ///   it to him. The ball moves on, he jogs back.
        ///
        ///   THE BACK FOUR - each picks up whoever walks into his zone, whoever's man he
        ///   is, and stays with him if he gets past toward our goal. Nobody else tracks
        ///   anybody, so without this a run in behind would be free.
        ///
        /// Everybody else holds. That is what keeps a shape: the only long runs are the
        /// press, and it is one man at a time and capped by his zone.
        /// </summary>
        void HoldShape(int carrier, Vector3 goal)
        {
            int n = members.Length;
            int m = opponents != null ? opponents.Length : 0;
            if (taken.Length != m) taken = new bool[m];
            for (int k = 0; k < m; k++) taken[k] = false;
            for (int i = 0; i < n; i++) targets[i] = -1;

            // The presser: the nearest man of the line whose band the carrier is in, or
            // the carrier's man from the table - either way only if the carrier is in his zone.
            if (carrier >= 0 && opponents[carrier] != null && pressByLine)
            {
                int p = LinePresser(carrier);
                if (p >= 0)
                {
                    targets[p] = carrier; taken[carrier] = true; Presser = members[p];
                    PressLine = Formation.LineOf(members[p].role);
                }
            }
            else if (carrier >= 0 && opponents[carrier] != null)
            {
                Role theirs = Formation.RoleOf(opponents[carrier]);
                for (int i = 0; i < n; i++)
                {
                    DefenderAI me = members[i];
                    if (me == null || Formation.MarksRole(me.role) != theirs) continue;

                    float d = Flat(opponents[carrier].position - Anchor(me)).magnitude;
                    float r = ZoneRadius(me.role) + (engaged[i] == carrier ? releaseMargin : 0f);
                    if (d <= r) { targets[i] = carrier; taken[carrier] = true; Presser = me; }
                    break;
                }
            }

            // The back four keep the runners they already have...
            for (int i = 0; i < n; i++)
            {
                if (targets[i] >= 0) continue;
                int k = engaged[i];
                engaged[i] = -1;
                if (k < 0 || k >= m || taken[k] || !IsBackFour(i)) continue;
                if (k == carrier && Presser != null) continue;        // somebody is out to him

                Vector3 man = Flat(opponents[k].position);
                bool inZone = Flat(man - Anchor(members[i])).magnitude <= ZoneRadius(members[i].role) + releaseMargin;
                if (inZone || GotPast(members[i], man)) { targets[i] = k; taken[k] = true; }
            }

            // ...and pick up the nearest new one in their zone. On the ball too, if the
            // man whose job he is could not go out to him.
            for (int i = 0; i < n; i++)
            {
                if (targets[i] >= 0 || !IsBackFour(i)) continue;
                Vector3 anchor = Anchor(members[i]);
                float best = ZoneRadius(members[i].role);
                for (int k = 0; k < m; k++)
                {
                    if (opponents[k] == null || taken[k]) continue;
                    float d = Flat(opponents[k].position - anchor).magnitude;
                    if (d <= best) { best = d; targets[i] = k; }
                }
                if (targets[i] >= 0) taken[targets[i]] = true;
            }

            for (int i = 0; i < n; i++)
            {
                DefenderAI me = members[i];
                if (me == null) continue;
                engaged[i] = targets[i];

                if (targets[i] < 0)
                {
                    me.markTarget = null;
                    me.pace = DefendPace.Hold;
                    Place(me, Anchor(me), false);
                    continue;
                }

                // On the ball: tight and flat out. Anyone else: goal-side, at a run.
                bool onBall = targets[i] == carrier;
                if (onBall && Presser == null) Presser = me;

                Vector3 man = Flat(opponents[targets[i]].position);
                Vector3 toGoal = Flat(goal - man).normalized;
                me.markTarget = man;
                me.pace = onBall ? DefendPace.Press : DefendPace.Mark;
                Place(me, man + toGoal * (onBall ? onBallGap : markGap), onBall);
            }
        }

        /// <summary>
        /// Press by line. The carrier's depth says which line's band he is in, and that
        /// line sends whichever man's post is nearest him - if he is within that man's
        /// zone. -1 if nobody from that line can reach him.
        ///
        /// The bands split halfway between the lines. A carrier in front of our front
        /// line is the front line's, which is what puts somebody onto their centre-backs:
        /// the table gives them to nobody, and their CBs stand just in front of our front
        /// three because their back line is set off our most advanced man.
        ///
        /// The man already out keeps him while the carrier stays within bandMargin of his
        /// line's band and within his zone plus releaseMargin - the same hysteresis the
        /// table press uses, so a carrier walking along a boundary is not passed back and
        /// forth between two lines.
        /// </summary>
        int LinePresser(int carrier)
        {
            int n = members.Length;
            Vector3 c = Flat(opponents[carrier].position);
            float depth = DepthOf(c.z);

            int prev = -1;
            for (int i = 0; i < n; i++)
                if (engaged[i] == carrier && members[i] != null && !Formation.IsKeeper(members[i].role)) { prev = i; break; }

            int line = BandOf(depth, prev >= 0 ? Formation.LineOf(members[prev].role) : -1);

            if (prev >= 0 && Formation.LineOf(members[prev].role) == line &&
                Flat(c - Anchor(members[prev])).magnitude <= ZoneRadius(members[prev].role) + releaseMargin)
                return prev;

            int best = -1;
            float bd = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                DefenderAI me = members[i];
                if (me == null || Formation.IsKeeper(me.role) || Formation.LineOf(me.role) != line) continue;
                float d = Flat(c - Anchor(me)).magnitude;
                if (d <= ZoneRadius(me.role) && d < bd) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// Which line's band a depth (metres off our goal line) falls in - split halfway
        /// between lines. `keep` is the line already out to him: he stays that line's
        /// until he is bandMargin past the split.
        /// </summary>
        int BandOf(float depth, int keep)
        {
            float backMid = (lineDepth[0] + lineDepth[1]) * 0.5f;
            float midFront = (lineDepth[1] + lineDepth[2]) * 0.5f;

            if (keep == 0 && depth < backMid + bandMargin) return 0;
            if (keep == 1 && depth >= backMid - bandMargin && depth < midFront + bandMargin) return 1;
            if (keep == 2 && depth >= midFront - bandMargin) return 2;

            if (depth >= midFront) return 2;
            if (depth >= backMid) return 1;
            return 0;
        }

        bool IsBackFour(int i)
        {
            return members[i] != null && Formation.LineOf(members[i].role) == 0;
        }

        /// <summary>Is the man nearer our goal line than his marker - has he gone past him?</summary>
        bool GotPast(DefenderAI me, Vector3 man)
        {
            float goalZ = TacticalPitch.GoalCentre(defendsPositiveZ).z;
            float mine = Mathf.Abs(goalZ - me.transform.position.z);
            float his = Mathf.Abs(goalZ - man.z);
            return his < mine - pastMargin;
        }

        float ZoneRadius(Role r)
        {
            if (r == Role.ST) return strikerZone;
            switch (Formation.LineOf(r))
            {
                case 0: return backZone;
                case 1: return midZone;
                default: return frontZone;
            }
        }

        /// <summary>
        /// Where he waits, and the middle of his zone. Across the pitch it is his slot slid
        /// with the ball, pulled in toward the middle if it is on the far side. Up and
        /// down it is his line (holdLines) - or, off, his kickoff slot slid with the ball.
        /// </summary>
        Vector3 Anchor(DefenderAI me)
        {
            if (Formation.IsKeeper(me.role)) return me.homeSlot;

            Vector3 slot = me.homeSlot;
            if (slot.x * ballSide < 0f) slot.x *= farSideTuck;

            Vector3 a = slot + shift;
            if (holdLines)
                a.z = ZAt(lineDepth[Formation.LineOf(me.role)] - (me.role == Role.DM ? pivotDrop : 0f));
            a.y = 0f;
            a.x = Mathf.Clamp(a.x, -TacticalPitch.HalfW + pitchInset, TacticalPitch.HalfW - pitchInset);
            a.z = Mathf.Clamp(a.z, -TacticalPitch.HalfL + pitchInset, TacticalPitch.HalfL - pitchInset);
            return a;
        }

        /// <summary>
        /// The whole shape slides part of the way toward the ball, across and up or down
        /// the pitch. Without this the waiting men stand on their kickoff spots while the
        /// ball is on the far touchline, and their zones stand there with them.
        ///
        /// The slide down the pitch is used only with holdLines off, and it stops where it
        /// would put the deepest back-four slot on lineFloor - the whole shape stops as one
        /// piece, rather than each man being clamped where he stands and the midfield
        /// folding down onto the back four.
        /// </summary>
        Vector3 ShapeShift(Vector3 ballPos)
        {
            float x = Mathf.Clamp(ballPos.x * lateralFollow, -maxLateralShift, maxLateralShift);
            float z = Mathf.Clamp(ballPos.z * depthFollow, -maxDepthShift, maxDepthShift);

            float deepest = float.MaxValue;
            for (int i = 0; i < members.Length; i++)
                if (IsBackFour(i)) deepest = Mathf.Min(deepest, DepthOf(members[i].homeSlot.z));
            if (deepest < float.MaxValue)
            {
                float side = defendsPositiveZ ? 1f : -1f;
                z = Mathf.Min(z * side, deepest - LineFloor) * side;
            }
            return new Vector3(x, 0f, z);
        }

        /// <summary>
        /// The three lines, in metres off our goal line.
        ///
        /// The back line is set off the ball - backLineFollow of the ball's distance from
        /// our goal, between lineFloor and backLineMax - and the other two stand a fixed
        /// distance in front of it. That is the whole shape: the three lines cannot run
        /// into each other because each one is placed off the one behind, and the back
        /// line cannot go behind the keeper because it has a floor.
        ///
        /// It replaces the kickoff slots as the depth of the shape because those are not a
        /// defensive shape. The away side's are mirrored round its drill position and
        /// clamped out of its own box, so its back four, pivot and eights started 6 m
        /// apart and its whole block was 30 m deep; the home side's never came within
        /// 16 m of its own goal. Same code, two different defences.
        ///
        /// With holdLines off the lines are only measured, as each line's average depth,
        /// so press by line still has bands to read.
        /// </summary>
        void MeasureLines()
        {
            if (holdLines)
            {
                float back = Mathf.Clamp(DepthOf(ball.position.z) * backLineFollow,
                                         LineFloor, Mathf.Max(LineFloor, backLineMax));
                lineDepth[0] = back;
                lineDepth[1] = back + backToMid;
                lineDepth[2] = back + backToMid + midToFront;
                return;
            }

            for (int line = 0; line < 3; line++)
            {
                float sum = 0f;
                int c = 0;
                for (int i = 0; i < members.Length; i++)
                {
                    if (members[i] == null || Formation.LineOf(members[i].role) != line) continue;
                    sum += DepthOf(Anchor(members[i]).z);
                    c++;
                }
                lineDepth[line] = c > 0 ? sum / c : (line > 0 ? lineDepth[line - 1] : LineFloor);
            }
        }

        // ----------------------------------------------------------------- floor --

        /// <summary>
        /// Every order goes out through here, so no path - waiting, marking, tracking a
        /// run, the press, the high-press man-marking - can put a man behind the keeper.
        ///
        /// There was no floor at all. The zonal defence had one (the back line stopped
        /// 8 m off the goal line) and it went with the zonal code; the slide that came
        /// back with holdShape kept the old field names, so the scene's old values -
        /// slide 0.6 of the ball, up to 22 m - came back to life with nothing under them.
        /// The away back four slid to the goal-line inset, 49.5, two metres behind their
        /// keeper on 47.5, and every marking station was unclamped on top of that. The
        /// offside line followed them down - it is the second-deepest of us - so the
        /// men they marked stood deeper too, and the markers went deeper again.
        ///
        /// The man going to the ball may follow it below the floor, as deep as the ball
        /// and no further: a carrier taking it to the byline still has somebody on him.
        /// </summary>
        void Place(DefenderAI me, Vector3 p, bool onBall)
        {
            float floor = HardFloor;
            if (onBall) floor = Mathf.Min(floor, Mathf.Max(0f, DepthOf(ball.position.z)));
            if (DepthOf(p.z) < floor) p.z = ZAt(floor);
            me.SetStation(p);
        }

        /// <summary>
        /// The deepest an outfielder may stand, in metres off our goal line: the keeper
        /// plus keeperClearance. The keeper has no brain and stands on Formation.KeeperDepth;
        /// if he ever moves, this should read where he actually is.
        /// </summary>
        float HardFloor { get { return Formation.KeeperDepth + keeperClearance; } }

        /// <summary>The deepest the back line waits - never below the hard floor.</summary>
        float LineFloor { get { return Mathf.Max(lineFloor, HardFloor); } }

        /// <summary>How far a z is from our goal line, up the pitch.</summary>
        float DepthOf(float z)
        {
            float goalZ = TacticalPitch.GoalCentre(defendsPositiveZ).z;
            return defendsPositiveZ ? goalZ - z : z - goalZ;
        }

        /// <summary>The z that is this far off our goal line.</summary>
        float ZAt(float depth)
        {
            float goalZ = TacticalPitch.GoalCentre(defendsPositiveZ).z;
            return defendsPositiveZ ? goalZ - depth : goalZ + depth;
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
