using System.Collections.Generic;
using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// The defending side's shared picture and the standing orders that come out of it.
    /// One of these per team; the individual defenders only execute what it hands them.
    ///
    /// It recomputes only when <see cref="PitchIntel"/> takes a new picture - about
    /// once a second - so the block moves in steps, the way a real line does when
    /// everyone glances up, shuffles across, and settles again.
    ///
    /// SHARED principles here; who each man actually marks is per-role and lives in
    /// <see cref="DefenceDuties"/>. The split is the point: every defender stands the
    /// same distance from his neighbour and drops to the same line, and none of them is
    /// looking at the same opponent.
    ///
    /// Applied in this order, and order is priority - a later one overrides an earlier.
    ///
    ///   1  take the slot the formation gives you, slid toward the ball
    ///   2  stay within touching distance of your neighbours
    ///   3  THE BACK LINE IS THE CENTRE-BACKS. Once they are as deep as they are allowed
    ///      to go, the block stops dropping - it does not compress into the six-yard box
    ///   4  pick up the man your ROLE is responsible for (DefenceDuties), and go tight
    ///      once he is inside your area
    ///   5  on the break, do NOT go to him until he reaches the box. Block the middle,
    ///      sit on the lane to the man behind you, and narrow the angle instead
    ///   6  cover the zone a team-mate vacated to do 4
    ///   7  AND STAY IN YOUR POSITION. Whatever 4, 5 and 6 came up with is clamped back
    ///      inside a radius of your own slot before it is issued as an order
    ///
    /// 7 is last because last is highest. Everything above it is a reason to leave your
    /// position, and every one of them is allowed to - but only so far. A defence that
    /// follows its men anywhere does not have a shape, it has ten separate chases, and
    /// the space that opens up behind them costs more than the duels are worth.
    ///
    /// Note what 7 does NOT touch: cutting out a ball that is already travelling
    /// (§3.18). That is a live reaction to a moving object rather than a standing
    /// position, it is only ever given to one man, and it is already bounded by whether
    /// he can physically get there.
    ///
    /// Why 3 is its own principle and not a clamp inside 1: clamping each man
    /// individually squeezes the block, because the front players keep dropping after
    /// the back four has stopped. The line has to stop the WHOLE shape, which means the
    /// clamp has to come out of the shared shift rather than out of each station.
    /// </summary>
    public class TeamDefence : MonoBehaviour
    {
        [Header("Refs")]
        public Transform ball;
        [Tooltip("The ball itself. Needed to read its path - a defender cuts a pass out by going where it will be, not where it is.")]
        public Ball ballBody;
        public Transform[] opponents;
        public DefenderAI[] members;

        [Tooltip("True if this side defends the goal at +Z.")]
        public bool defendsPositiveZ = true;

        [Header("What it knows, and when")]
        public PitchIntel intel = new PitchIntel();
        [Tooltip("Who would reach each square first, from THIS side's point of view. Built from this side's own snapshot, never shared with the attack - a grid both teams read would let each of them see through the other's delay, and that delay is the difficulty (PROJECT.md 3.15, 3.24).")]
        public PitchControl control = new PitchControl();
        [Tooltip("Top speed the control grid falls back to for a body with no brain on it - a goalkeeper, say. Everyone else races at his own sprinting speed.")]
        public float controlSpeed = 7.2f;

        [Header("1 - shape")]
        [Tooltip("How much of the ball's lateral offset the whole block slides across.")]
        [Range(0f, 1f)] public float lateralFollow = 0.55f;
        [Tooltip("How much the block drops as the ball advances on it.")]
        [Range(0f, 1f)] public float depthFollow = 0.60f;
        public float maxLateralShift = 13f;
        public float maxDepthShift = 22f;

        [Header("2 - compactness")]
        [Tooltip("Biggest gap allowed between two players standing next to each other in the same line. This is the number: a ball played into the gap has to be pressable before it is controlled.")]
        public float maxNeighbourGap = 12f;
        [Tooltip("Biggest gap allowed between one line and the next - back four to midfield, midfield to the front. Keeps the block one unit rather than three.")]
        public float maxLineGap = 14f;
        [Tooltip("How many relaxation passes per picture. A line is a chain, so fixing one pair re-opens its neighbour - it takes iterations to settle. Four passes leaves a 12 m target sitting at 14.5 m; sixteen lands on it.")]
        [Range(1, 24)] public int compactPasses = 16;
        [Tooltip("How much of each pass's correction is applied. Damping only - the relaxation is monotone, so this never overshoots.")]
        [Range(0.1f, 1f)] public float squeeze = 0.8f;

        [Header("3 - the back line")]
        [Tooltip("How far off our own goal line the centre-backs are allowed to drop. The whole block stops here: once they are on it, dropping further would only squeeze everyone else onto them.")]
        public float backLineFloor = 8f;
        [Tooltip("How far off their own goal line the centre-backs are allowed to push when the ball is at the far end.")]
        public float backLineCeiling = 62f;

        [Header("4 - duties and marking")]
        [Tooltip("How far goal-side of his man a marker stands.")]
        public float markGap = 1.7f;
        [Tooltip("A marker will not travel further than this to pick a man up. The leash on principle 4 - without it the shape unravels into ten duels.")]
        public float maxMarkTravel = 26f;
        [Tooltip("How far he leans toward his man while the man is still OUTSIDE his area. He is watching, not marking.")]
        [Range(0f, 1f)] public float watchBlend = 0.35f;
        [Tooltip("Ball this far from our goal or beyond makes it a high press - they are playing out and we go after it.")]
        public float highPressBeyond = 68f;
        [Tooltip("Ball inside this of our goal drops the block off: the striker comes back in as an extra midfielder and the wingers come with him.")]
        public float lowPressWithin = 34f;
        [Tooltip("How far the front three come BACK when the side has dropped off. Switching their marking target is not enough on its own - a striker who defends midfielders from the halfway line is still not defending anything.")]
        public float forwardDrop = 9f;
        [Tooltip("How far the front three push UP to press their build-up.")]
        public float forwardPush = 5f;

        [Header("5 - the break")]
        [Tooltip("Ball closing on our goal faster than this counts as a break.")]
        public float breakSpeed = 5.0f;
        [Tooltip("How far off the carrier the man in front of him holds while delaying. Nothing like tackling distance - the job is to slow him down, not to win it.")]
        public float containGap = 3.2f;
        [Tooltip("How hard he shades to the middle to stop the carrier coming inside.")]
        public float containInsideShade = 2.0f;
        [Tooltip("How much he slides onto the lane to the man goal-side of him rather than onto the carrier.")]
        [Range(0f, 1f)] public float laneBlockShare = 0.35f;

        [Header("6 - cover")]
        [Range(0f, 1f)] public float coverBlend = 0.55f;

        [Header("7 - hold your position (highest priority)")]
        [Tooltip("Scales every role's leash (Formation.PositionLeash). 1 = as written - centre-backs 9 m, strikers 16 m. Below 1 the side holds its shape harder and marks less; above 1 it chases more and leaves more behind.")]
        [Range(0.2f, 2.5f)] public float leashScale = 1f;
        [Tooltip("Turn off to let marking and pressing drag men as far as they like - the behaviour before this principle existed. Useful for seeing what it is buying.")]
        public bool holdPosition = true;

        [Header("Cutting the pass out")]
        [Tooltip("Seconds before he reacts to a ball being struck. This is the whole difference between a defender and a magnet: he has to read it first.")]
        public float reaction = 0.28f;
        [Tooltip("How far ahead of the ball he is willing to think, in seconds.")]
        public float lookAhead = 2.6f;
        [Tooltip("Steps the path is sampled at. Finer costs nothing at these distances.")]
        public float step = 0.08f;
        [Tooltip("Highest a ball can be and still be cut out. Above his head is what a lofted pass is for.")]
        public float interceptHeight = 1.1f;

        /// <summary>The man currently closing the ball down. Everyone else holds shape.</summary>
        public DefenderAI Presser { get; private set; }

        /// <summary>The one man sent to cut the ball out, if anybody can reach it.</summary>
        public DefenderAI Interceptor { get; private set; }

        /// <summary>Where he was sent, and when the ball gets there.</summary>
        public Vector3 InterceptAt { get; private set; }
        public float InterceptIn { get; private set; }

        /// <summary>Opponent in possession, as far as the defence knows.</summary>
        public Vector3 CarrierPos { get; private set; }
        public bool OnTheBreak { get; private set; }

        /// <summary>How hard the side is going after it right now.</summary>
        public PressIntensity Press { get; private set; }

        /// <summary>True while the back four is pinned on its floor and the block has stopped dropping.</summary>
        public bool LineOnFloor { get; private set; }

        /// <summary>Who each man has been given, as an index into `opponents`. -1 = nobody.</summary>
        public int[] Duties { get { return targets; } }

        readonly List<int> row = new List<int>();
        Vector3[] slotNow = new Vector3[0];
        Vector3[] anchors = new Vector3[0];
        bool[] pulled = new bool[0];
        Role[] myRoles = new Role[0];
        Role[] oppRoles = new Role[0];
        Vector3[] oppPos = new Vector3[0];
        bool[] oppTaken = new bool[0];
        int[] targets = new int[0];
        Vector3 prevBall;
        float prevBallAt;

        // Scratch for the control grid, kept so a rebuild every picture allocates nothing.
        readonly List<PitchControl.Runner> ctrlOurs = new List<PitchControl.Runner>();
        readonly List<PitchControl.Runner> ctrlTheirs = new List<PitchControl.Runner>();

        void Update()
        {
            if (ball == null || members == null || members.Length == 0) return;

            // Live, every frame, unlike everything else here. A ball already travelling
            // is the one thing a defender is directly engaged with, and PitchIntel exists
            // to make the REST of the picture stale - not this. Acting on a one-second-old
            // snapshot of a pass means never reaching one.
            TickIntercept();

            if (intel.Tick(Time.time, opponents, ball.position, BlockCentre()))
                Recompute();
        }

        // ---------------------------------------------------------- interception --

        /// <summary>
        /// Walk forward along the ball's own closed-form path and ask, at each step,
        /// whether anybody could be standing there by the time it arrives. The first man
        /// for whom the answer is yes gets sent; nobody else moves.
        ///
        /// Sending only one is the same rule as principle 5: a loose ball that drags the
        /// whole block toward it leaves the pitch behind it wide open, and one man
        /// arriving is all it takes anyway.
        ///
        /// A ball over their heads is unreachable by construction - the sample is skipped
        /// while it is above interceptHeight - so a lofted pass beats the press for
        /// exactly as long as it is in the air, and no rule had to be written for that.
        /// </summary>
        void TickIntercept()
        {
            Interceptor = null;
            InterceptIn = 0f;

            bool loose = ballBody != null && ballBody.InFlight && !ballBody.Carried;
            if (!loose)
            {
                for (int i = 0; i < members.Length; i++)
                    if (members[i] != null) members[i].ClearIntercept();
                return;
            }

            int pick = -1;
            float bestT = float.MaxValue;
            Vector3 bestP = Vector3.zero;

            for (float t = step; t <= lookAhead; t += step)
            {
                Vector3 p = ballBody.Predict(t);
                if (p.y > interceptHeight) continue;

                for (int i = 0; i < members.Length; i++)
                {
                    var m = members[i];
                    if (m == null || !m.active || m.Recovering) continue;

                    float travel = Flat(p - m.transform.position).magnitude;
                    float need = travel / Mathf.Max(m.interceptSpeed, 0.1f) + reaction;
                    if (need > t) continue;

                    if (t < bestT) { bestT = t; bestP = p; pick = i; }
                }

                if (pick >= 0) break;      // walking forwards, so the first hit is the earliest
            }

            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] == null) continue;
                if (i == pick) members[i].SetIntercept(bestP);
                else members[i].ClearIntercept();
            }

            if (pick >= 0)
            {
                Interceptor = members[pick];
                InterceptAt = bestP;
                InterceptIn = bestT;
            }
        }

        // ---------------------------------------------------------------- shape --

        /// <summary>
        /// Rebuild the grid from what THIS side can see: its own men live, the attack as
        /// of the last picture and carried forward by however it was moving. The same
        /// staleness as every other belief on this side (PROJECT.md 3.15).
        ///
        /// "Ours" is `members`, which is the outfield block - the keeper has no brain and
        /// was never in it. That happens to be the right list anyway: crediting a
        /// goalkeeper with winning a race to the halfway line would hand this side ground
        /// it does not have.
        /// </summary>
        void RebuildControl()
        {
            ctrlOurs.Clear();
            for (int i = 0; members != null && i < members.Length; i++)
            {
                // A frozen man is still a body standing in the way, so `active` is not
                // consulted here - it stops him deciding things, not existing.
                if (members[i] == null) continue;
                ctrlOurs.Add(PitchControl.Of(members[i].transform, members[i].transform.position,
                                             members[i].Velocity, controlSpeed));
            }

            ctrlTheirs.Clear();
            for (int k = 0; opponents != null && k < opponents.Length; k++)
            {
                if (opponents[k] == null) continue;
                Vector3 v = k < intel.OpponentVel.Length ? intel.OpponentVel[k] : Vector3.zero;
                ctrlTheirs.Add(PitchControl.Of(opponents[k], intel.Projected(k, Time.time),
                                               v, controlSpeed));
            }

            control.Rebuild(ctrlOurs, ctrlTheirs);
        }

        /// <summary>
        /// What the map MEANS to a defence, which is not what it means to an attack.
        ///
        /// The attack reads control straight - high is somewhere to play into. A defence
        /// that asks "where do we control" gets a useless answer, because the ground it
        /// owns least is usually the corner flag and losing that costs nothing. The real
        /// question is "where do THEY own ground that would hurt us", so the loss is
        /// priced by how much it costs to concede from there - the same Danger() the
        /// marking priorities already run on.
        ///
        /// 0 = not our problem. 1 = they would win the race to the ground in front of
        /// our own goal.
        /// </summary>
        public float ExposureAt(Vector3 p)
        {
            if (!control.Ready) return 0f;
            float theirs = 1f - control.OursAt(p);
            return theirs * TacticalPitch.Danger(p, defendsPositiveZ);
        }

        void Recompute()
        {
            RebuildControl();

            int n = members.Length;
            if (slotNow.Length != n)
            {
                slotNow = new Vector3[n];
                anchors = new Vector3[n];
                pulled = new bool[n];
                myRoles = new Role[n];
                targets = new int[n];
            }

            Vector3 ballPos = intel.Ball;
            MeasureBreak(ballPos);
            CarrierPos = NearestOpponentTo(ballPos, ballPos);
            Press = DefenceDuties.IntensityFor(ballPos, defendsPositiveZ, highPressBeyond, lowPressWithin);

            for (int i = 0; i < n; i++)
                myRoles[i] = members[i] == null ? Role.DM : members[i].role;

            // 1 and 3 - the slot slid toward the ball, with the slide cut short if it
            // would take the centre-backs past their floor.
            Vector3 shift = ShapeShift(ballPos);
            float side = defendsPositiveZ ? 1f : -1f;      // +1 = toward our own goal

            for (int i = 0; i < n; i++)
            {
                pulled[i] = false;
                if (members[i] == null) { slotNow[i] = Vector3.zero; continue; }

                members[i].covering = false;
                slotNow[i] = members[i].homeSlot + shift;

                // The front three move with the press. Giving the striker a midfielder to
                // mark while leaving him on the halfway line just gives him somebody to
                // watch from a distance; dropping off has to mean actually dropping.
                Role r = members[i].role;
                if (Formation.IsStriker(r) || Formation.IsWinger(r))
                {
                    if (Press == PressIntensity.Low) slotNow[i].z += side * forwardDrop;
                    else if (Press == PressIntensity.High) slotNow[i].z -= side * forwardPush;
                }

                // Where he is SUPPOSED to be for this picture. Principle 7 measures
                // everything against this, including the drop above - clamping to the raw
                // slot instead would haul the striker straight back up the pitch and undo
                // the very thing the low block just asked him to do.
                anchors[i] = slotNow[i];
            }

            // 2 - squeeze anyone who has drifted away from his neighbours.
            Compact(n);

            // 4 - each man picks up whoever his ROLE makes him responsible for.
            AssignDuties(n);

            // 7, first pass. It has to run BEFORE cover, not just at the end: cover works
            // off `pulled`, and a man the leash hauled back never actually left his slot.
            // Covering the hole he did not make sends a second man out of position to
            // stand where somebody is already standing.
            HoldPosition(n);

            // 6 - fill the holes that 4 really did make.
            Cover(n, shift);

            // 5 - and decide who goes to the ball, and whether he engages or delays.
            AssignPresser(n, ballPos);

            // 7 again, the last word - this time over cover and the containment station.
            HoldPosition(n);

            for (int i = 0; i < n; i++)
                if (members[i] != null) members[i].SetStation(slotNow[i]);
        }

        /// <summary>
        /// How far the whole block slides to follow the ball - principle 1, with
        /// principle 3 written into it.
        ///
        /// THE BACK LINE IS THE CENTRE-BACKS. The depth of the shift is decided by where
        /// it would put them, and if that is past the floor the shift is cut back until
        /// they sit exactly on it. Everyone else moves by the reduced amount, so the
        /// block stops as one piece.
        ///
        /// Clamping each man where he stands instead would keep the front players
        /// dropping after the back four had stopped, and the block would fold up on the
        /// edge of its own box - twenty metres of shape squeezed into eight.
        /// </summary>
        Vector3 ShapeShift(Vector3 ballPos)
        {
            float side = defendsPositiveZ ? 1f : -1f;
            float x = Mathf.Clamp(ballPos.x * lateralFollow, -maxLateralShift, maxLateralShift);
            float z = Mathf.Clamp(ballPos.z * side * depthFollow, -maxDepthShift, maxDepthShift);

            LineOnFloor = false;

            // Where the back line would end up, measured toward our own goal.
            float cbHome;
            if (CentreBackDepth(out cbHome))
            {
                float goalZ = defendsPositiveZ ? TacticalPitch.HalfL : -TacticalPitch.HalfL;
                float wanted = cbHome + z * side;

                float deepest = goalZ - side * backLineFloor;      // nearest our own goal
                float highest = goalZ - side * backLineCeiling;    // furthest up the pitch

                float clamped = defendsPositiveZ
                    ? Mathf.Clamp(wanted, highest, deepest)
                    : Mathf.Clamp(wanted, deepest, highest);

                if (Mathf.Abs(clamped - wanted) > 0.01f)
                {
                    LineOnFloor = true;
                    z = (clamped - cbHome) * side;
                }
            }

            return new Vector3(x, 0f, z * side);
        }

        /// <summary>
        /// Principle 7, and the last word. Every order the other principles produced is
        /// pulled back inside the man's own leash.
        ///
        /// A clamp rather than a veto: he still goes to his man, still steps out to
        /// cover, still delays the break - he simply runs out of rope, and stops at the
        /// edge of his position facing the right way. That is what a defender who cannot
        /// reach his man actually does, and it is why the shape survives while the duties
        /// still do something.
        ///
        /// `pulled` is cleared for anyone the clamp caught: he is no longer marking, so
        /// the cover pass must not keep treating his slot as vacated.
        /// </summary>
        void HoldPosition(int n)
        {
            if (!holdPosition) return;

            for (int i = 0; i < n; i++)
            {
                if (members[i] == null) continue;

                float leash = Formation.PositionLeash(members[i].role) * leashScale;
                Vector3 off = Flat(slotNow[i] - anchors[i]);
                float d = off.magnitude;
                if (d <= leash) continue;

                slotNow[i] = anchors[i] + off * (leash / d);
                pulled[i] = false;
            }
        }

        /// <summary>Average depth of the centre-backs' own slots. The line the block hangs off.</summary>
        bool CentreBackDepth(out float z)
        {
            float sum = 0f;
            int c = 0;
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] == null || !Formation.IsCentreBack(members[i].role)) continue;
                sum += members[i].homeSlot.z;
                c++;
            }
            z = c > 0 ? sum / c : 0f;
            return c > 0;
        }

        /// <summary>
        /// Principle 2 - the one a defensive coach actually cares about. Keep the players
        /// close enough together that a ball played into the space between two of them can
        /// be pressed before it is controlled.
        ///
        /// Spacing is measured ALONG A LINE, not against whoever happens to be nearest.
        /// The first version compared each man to his closest team-mate of any kind, and
        /// the back four never squeezed at all: a centre-back's nearest body is the pivot
        /// standing 9 m in front of him, so the 12 m rule read as satisfied while the two
        /// centre-backs sat 15 m apart with a hole between them.
        ///
        /// Relaxed over several passes rather than corrected in one go. A single pass only
        /// removes part of the excess, so the block settles wherever that partial step
        /// lands instead of at the number you asked for.
        /// </summary>
        void Compact(int n)
        {
            for (int pass = 0; pass < compactPasses; pass++)
            {
                SqueezeAlongLines(n);
                SqueezeBetweenLines(n);
            }
        }

        /// <summary>Neighbours in the same line, ordered across the pitch.</summary>
        void SqueezeAlongLines(int n)
        {
            for (int line = 0; line < Formation.LineCount; line++)
            {
                row.Clear();
                for (int i = 0; i < n; i++)
                    if (members[i] != null && Formation.LineOf(members[i].role) == line) row.Add(i);
                if (row.Count < 2) continue;

                row.Sort(delegate (int a, int b) { return slotNow[a].x.CompareTo(slotNow[b].x); });

                for (int k = 0; k + 1 < row.Count; k++)
                {
                    int i = row[k], j = row[k + 1];
                    Vector3 d = Flat(slotNow[j] - slotNow[i]);
                    float gap = d.magnitude;
                    if (gap <= maxNeighbourGap || gap < 1e-3f) continue;

                    Vector3 corr = d / gap * (gap - maxNeighbourGap) * 0.5f * squeeze;
                    slotNow[i] += corr;
                    slotNow[j] -= corr;
                }
            }
        }

        /// <summary>And the lines themselves, so the block does not stretch front to back.</summary>
        void SqueezeBetweenLines(int n)
        {
            for (int line = 0; line + 1 < Formation.LineCount; line++)
            {
                float za, zb;
                int ca = LineDepth(n, line, out za);
                int cb = LineDepth(n, line + 1, out zb);
                if (ca == 0 || cb == 0) continue;

                float gap = Mathf.Abs(za - zb);
                if (gap <= maxLineGap) continue;

                float pull = (gap - maxLineGap) * 0.5f * squeeze * Mathf.Sign(za - zb);
                for (int i = 0; i < n; i++)
                {
                    if (members[i] == null) continue;
                    int L = Formation.LineOf(members[i].role);
                    if (L == line) slotNow[i].z -= pull;
                    else if (L == line + 1) slotNow[i].z += pull;
                }
            }
        }

        int LineDepth(int n, int line, out float z)
        {
            float sum = 0f;
            int c = 0;
            for (int i = 0; i < n; i++)
            {
                if (members[i] == null || Formation.LineOf(members[i].role) != line) continue;
                sum += slotNow[i].z;
                c++;
            }
            z = c > 0 ? sum / c : 0f;
            return c;
        }

        // --------------------------------------------------------------- duties --

        /// <summary>
        /// Principle 4. Each man picks up whoever his role makes him responsible for -
        /// the choosing is in <see cref="DefenceDuties"/> - and then this decides how
        /// close he actually gets.
        ///
        /// IN his area, he marks: goal-side of his man, tight. OUTSIDE it, he only
        /// leans - he holds his station and shades toward him. That distinction is the
        /// whole difference between a block and ten separate duels, and it is why the
        /// duty can safely be assigned to everybody at once: being given a man is not
        /// the same as being sent after him.
        ///
        /// The travel leash (maxMarkTravel) is what stops a full-back following his
        /// winger across the pitch and taking the shape with him.
        /// </summary>
        void AssignDuties(int n)
        {
            int m = opponents != null ? opponents.Length : 0;
            if (oppPos.Length != m)
            {
                oppPos = new Vector3[m];
                oppRoles = new Role[m];
                oppTaken = new bool[m];
            }

            int carrier = -1;
            float bestCarrier = float.MaxValue;
            for (int k = 0; k < m; k++)
            {
                oppPos[k] = intel.Projected(k, Time.time);
                oppRoles[k] = Formation.RoleOf(opponents[k]);
                float d = Flat(oppPos[k] - intel.Ball).sqrMagnitude;
                if (d < bestCarrier) { bestCarrier = d; carrier = k; }
            }

            DutyPicture pic;
            pic.opp = oppPos;
            pic.oppRole = oppRoles;
            pic.oppTaken = oppTaken;
            pic.carrier = carrier;
            pic.defendsPositiveZ = defendsPositiveZ;
            pic.onBreak = OnTheBreak;
            pic.press = Press;
            pic.maxTravel = maxMarkTravel;

            DefenceDuties.Assign(myRoles, slotNow, ref pic, targets);

            Vector3 goal = TacticalPitch.GoalCentre(defendsPositiveZ);

            for (int i = 0; i < n; i++)
            {
                if (members[i] == null) continue;

                int t = targets[i];
                if (t < 0) { members[i].markTarget = null; continue; }

                Vector3 man = oppPos[t];
                members[i].markTarget = man;

                // Goal-side of him, between him and the shot he wants.
                Vector3 toGoal = Flat(goal - man).normalized;
                Vector3 markPos = man + toGoal * markGap;

                // Principle 3 of the common set: is he actually in my area? On the break
                // nobody goes tight - principle 5 owns that case.
                bool inMyZone = Flat(man - slotNow[i]).magnitude
                                <= Formation.DefensiveZone(members[i].role);
                bool tooFar = Flat(markPos - slotNow[i]).magnitude > maxMarkTravel;

                if (inMyZone && !OnTheBreak && !tooFar)
                {
                    slotNow[i] = markPos;
                    pulled[i] = true;
                }
                else
                {
                    // Watching, not marking. He keeps his shape and shades that way.
                    slotNow[i] = Vector3.Lerp(slotNow[i], markPos, watchBlend);
                }
            }
        }

        /// <summary>
        /// Principle 5. Each hole a marker left gets ONE man leaning into it - the nearest
        /// one still holding shape.
        ///
        /// Iterating the other way round, and letting everybody lean toward the nearest
        /// hole, collapses the whole side into a column: one centre-back steps out and all
        /// nine team-mates drift after him. Cover is one man's job, not the team's.
        /// </summary>
        void Cover(int n, Vector3 shift)
        {
            for (int hole = 0; hole < n; hole++)
            {
                if (!pulled[hole] || members[hole] == null) continue;
                Vector3 vacated = members[hole].homeSlot + shift;

                int pick = -1;
                float best = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    if (members[i] == null || pulled[i] || members[i].covering) continue;
                    float d = Flat(slotNow[i] - vacated).sqrMagnitude;
                    if (d < best) { best = d; pick = i; }
                }
                if (pick < 0) continue;

                slotNow[pick] = Vector3.Lerp(slotNow[pick], vacated, coverBlend);
                members[pick].covering = true;
            }
        }

        // -------------------------------------------------------------- pressing --

        void MeasureBreak(Vector3 ballPos)
        {
            float dt = Time.time - prevBallAt;
            if (dt > 0.001f && prevBallAt > 0f)
            {
                Vector3 goal = TacticalPitch.GoalCentre(defendsPositiveZ);
                float was = Flat(goal - prevBall).magnitude;
                float now = Flat(goal - ballPos).magnitude;
                OnTheBreak = (was - now) / dt > breakSpeed;
            }
            prevBall = ballPos;
            prevBallAt = Time.time;
        }

        /// <summary>
        /// Whoever is closest to the man on the ball goes to him. Everyone else keeps
        /// their station - the whole point is that only one man leaves.
        ///
        /// WHAT HE DOES WHEN HE GETS THERE depends on principle 5. Normally he engages:
        /// goal-side, tight, shading the inside shoulder so the only ball left is
        /// backwards or down the line.
        ///
        /// On the break he does not engage at all until the carrier reaches the box. A
        /// man running at a back four that is still getting back does not want to be
        /// tackled, he wants to be committed to - going to him is how the last line gets
        /// beaten by one touch. So the job becomes delay: stand off, block the way
        /// inside, sit on the lane to whoever is arriving behind you, and let the rest
        /// of the shape catch up. That IS the defending; nobody has to win the ball.
        /// </summary>
        void AssignPresser(int n, Vector3 ballPos)
        {
            int pick = -1;
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (members[i] == null) continue;
                float d = Flat(members[i].transform.position - ballPos).magnitude;
                if (d < best) { best = d; pick = i; }
            }

            Presser = pick >= 0 ? members[pick] : null;

            // Once he is in the box, delaying has run out of pitch - go and engage.
            bool contain = OnTheBreak && !TacticalPitch.InPenaltyBox(CarrierPos, defendsPositiveZ);

            for (int i = 0; i < n; i++)
            {
                if (members[i] == null) continue;
                bool isPresser = i == pick;

                members[i].SetPressing(isPresser,
                                       isPresser && contain ? PressMode.Contain : PressMode.Deny,
                                       CarrierPos, defendsPositiveZ, OnTheBreak);

                if (!isPresser) continue;
                members[i].covering = false;
                if (contain) slotNow[i] = ContainStation(i, n);
            }
        }

        /// <summary>
        /// Where the man delaying a break stands.
        ///
        /// Three things at once, and they are the same point: goal-side of the carrier so
        /// the angle to the goal is narrowed, shaded to the middle so the way inside is
        /// shut, and slid onto the lane to whoever is arriving goal-side of him. Standing
        /// off is what makes all three possible - at tackling distance he can only do the
        /// last thing he committed to.
        /// </summary>
        Vector3 ContainStation(int i, int n)
        {
            Vector3 goal = TacticalPitch.GoalCentre(defendsPositiveZ);
            Vector3 toGoal = Flat(goal - CarrierPos);
            if (toGoal.sqrMagnitude < 1e-4f) toGoal = Vector3.forward;
            toGoal.Normalize();

            Vector3 stand = CarrierPos + toGoal * containGap;

            // Shut the middle: shade toward the centre of the pitch, not the touchline.
            stand += TacticalPitch.InsideDir(CarrierPos) * containInsideShade;

            // And sit on the ball he wants to play - to the man who is already past us.
            int runner = RunnerBeyond(stand);
            if (runner >= 0)
            {
                Vector3 lane = Vector3.Lerp(CarrierPos, oppPos[runner], 0.45f);
                stand = Vector3.Lerp(stand, lane, laneBlockShare);
            }

            stand.y = 0f;
            return stand;
        }

        /// <summary>The most advanced opponent who is already goal-side of this point.</summary>
        int RunnerBeyond(Vector3 from)
        {
            float side = defendsPositiveZ ? 1f : -1f;
            int best = -1;
            float deepest = from.z * side;

            for (int k = 0; k < oppPos.Length; k++)
            {
                if (Formation.IsKeeper(oppRoles[k])) continue;
                float along = oppPos[k].z * side;
                if (along > deepest) { deepest = along; best = k; }
            }
            return best;
        }

        // ----------------------------------------------------------------- utils --

        Vector3 NearestOpponentTo(Vector3 p, Vector3 fallback)
        {
            if (opponents == null) return fallback;
            Vector3 best = fallback;
            float bd = float.MaxValue;
            for (int k = 0; k < opponents.Length; k++)
            {
                if (opponents[k] == null) continue;
                Vector3 q = intel.Projected(k, Time.time);
                float d = Flat(q - p).sqrMagnitude;
                if (d < bd) { bd = d; best = q; }
            }
            return best;
        }

        /// <summary>Where the block is standing, used to judge whether the ball is on us.</summary>
        Vector3 BlockCentre()
        {
            Vector3 sum = Vector3.zero;
            int c = 0;
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] == null) continue;
                sum += members[i].transform.position;
                c++;
            }
            return c > 0 ? sum / c : transform.position;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        void OnDrawGizmosSelected()
        {
            if (members == null) return;
            for (int i = 0; i < members.Length && i < slotNow.Length; i++)
            {
                if (members[i] == null) continue;
                Gizmos.color = pulled.Length > i && pulled[i]
                    ? new Color(1f, 0.4f, 0.2f)
                    : new Color(0.3f, 0.8f, 1f);
                Gizmos.DrawWireSphere(slotNow[i] + Vector3.up * 0.1f, 0.9f);
                Gizmos.DrawLine(members[i].transform.position, slotNow[i]);
            }
        }
    }
}
