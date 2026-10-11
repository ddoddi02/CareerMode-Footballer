using System.Collections.Generic;
using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// The side in possession. Where <see cref="TeamDefence"/> assigns stations to deny
    /// space, this one assigns them to open a passing lane.
    ///
    /// Every off-ball player is scored against a spread of candidate spots inside his own
    /// zone, and takes the best one. Scoring rather than rules is what lets five
    /// principles that pull in different directions coexist: a run in behind and a
    /// triangle and staying in position are all just terms, and the winning spot is
    /// whichever satisfies most of them at once.
    ///
    ///   1  carrier / his nearest team-mate / you form a triangle with an open lane
    ///   2  front-line players hunt for space inside their own zone
    ///   3  when the lane is congested, mix lateral and vertical movement to break free
    ///   4  run in behind on a timer - without straying offside
    ///   5  if the carrier is outnumbered, one man ahead and one behind come and show
    ///
    /// The candidate spread is a golden-angle spiral, not Random.insideUnitCircle, because
    /// it covers the zone evenly where random samples clump and leave holes (PROJECT.md
    /// 3.16 - not for replays: a replay is a recorded highlight, not a re-simulation).
    /// </summary>
    public class TeamAttack : MonoBehaviour
    {
        [Header("Refs")]
        public Transform ball;
        [Tooltip("The defending side - used for lanes, space and the offside line.")]
        public Transform[] opponents;
        [Tooltip("Everyone of ours including the human, so the carrier can be found.")]
        public Transform[] mates;
        [Tooltip("The ones this actually moves. The human is not in here.")]
        public AttackerAI[] members;
        [Tooltip("The ball itself - this side plays it, not just runs off it.")]
        public Ball ballBody;

        [Tooltip("True if this side attacks the goal at +Z.")]
        public bool attacksPositiveZ = true;

        [Header("What it knows, and when")]
        public PitchIntel intel = new PitchIntel();
        [Tooltip("Who would reach each square of the pitch first. Rebuilt with every picture, from this side's own snapshot. See PitchControl.")]
        public PitchControl control = new PitchControl();
        [Tooltip("Top speed the control grid falls back to for a body with no brain on it - a goalkeeper, say. Everyone else races at his own sprinting speed.")]
        public float controlSpeed = 7.2f;

        [Header("Shape")]
        [Range(0f, 1f)] public float lateralFollow = 0.35f;
        [Range(0f, 1f)] public float depthFollow = 0.45f;
        public float maxLateralShift = 10f;
        public float maxDepthShift = 18f;

        [Header("1 - triangles and lanes")]
        [Tooltip("Shortest ball worth offering.")]
        public float passMin = 7f;
        [Tooltip("Longest ball worth offering.")]
        public float passMax = 24f;
        [Tooltip("Clearance either side of the pass that counts as a properly open lane.")]
        public float laneOpen = 2.4f;
        [Tooltip("The man pressing the carrier is not blocking any one lane. Ignore the first stretch of the pass.")]
        public float carrierShadow = 2.5f;
        [Tooltip("Stations are kept this far inside the touchline.")]
        public float pitchInset = 1.5f;
        [Tooltip("Angle at the carrier that makes the best triangle.")]
        public float idealTriangleAngle = 60f;

        [Header("3 - unsticking")]
        [Tooltip("Lane clearance below which he is considered shut down and starts moving to get free.")]
        public float squeezedClearance = 2.6f;

        [Header("4 - runs in behind")]
        [Tooltip("How far short of the offside line he times his run to arrive.")]
        public float onsideMargin = 0.7f;

        [Header("5 - support when outnumbered")]
        public float pressureRadius = 9f;
        [Tooltip("How close the two supporting players try to get.")]
        public float showDistance = 9f;

        [Header("Scoring weights")]
        public float wLane = 1.00f;
        public float wSpace = 0.70f;
        public float wTriangle = 0.55f;
        public float wRange = 0.60f;
        public float wZone = 0.85f;
        public float wRun = 1.20f;
        public float wShow = 1.30f;
        public float wUnstick = 0.65f;

        [Header("Keeping out of each other's way")]
        [Tooltip("How near a spot already taken by a team-mate counts as crowding him. Two men in one pocket is one man wasted and one marker who gets both.")]
        public float claimRadius = 7f;
        [Tooltip("The human's claim is bigger. He cannot be told to move, so everyone else works around him - and being crowded is far more annoying when it is your own space.")]
        public float humanClaimRadius = 10f;
        [Tooltip("How hard crowding is punished. This is what stops the side collapsing into one pocket.")]
        public float wSeparation = 1.30f;

        [Header("The back line while we have it")]
        [Tooltip("Centre-backs and full-backs push up to squeeze the pitch instead of standing on their kickoff slot all game.")]
        public bool pushBackLine = true;
        [Tooltip("How far goal-side of their deepest attacker the back line sits.")]
        public float backLineGap = 3f;
        [Tooltip("Furthest up the pitch the back line will go, measured toward the goal we attack. A back four does not cross halfway.")]
        public float backLineMaxAlong = 0f;
        [Tooltip("How much more room a full-back gets to roam while we have the ball. He is the width, and the width is support.")]
        public float fullBackSupport = 1.9f;
        [Tooltip("How much more room a centre-back gets. Small - he is still the last line.")]
        public float centreBackSupport = 1.35f;

        [Header("Depth - the shape spans our back line to their last line")]
        [Tooltip("Stand the midfield and the front three at a fraction of the way from our back line to their offside line, instead of on their slot slid with the ball. Off: slot + shift, which left the mezzalas level with their midfield and twenty metres behind the wingers.")]
        public bool stretchToLines = true;
        [Tooltip("The front three's depth, as a fraction from our back line (0) to their last line (1).")]
        [Range(0f, 1f)] public float frontDepth = 0.92f;
        [Tooltip("The mezzalas. Far enough up to stand between their midfield and their back four.")]
        [Range(0f, 1f)] public float midDepth = 0.65f;
        [Tooltip("The pivot, when a bot plays there.")]
        [Range(0f, 1f)] public float pivotDepth = 0.30f;
        [Tooltip("A full-back on the ball's side goes up the outside this far. The far one stays in the back line.")]
        [Range(0f, 1f)] public float fullBackDepth = 0.40f;
        [Tooltip("The front line stops this far short of their last line - the runs in behind take it the rest of the way.")]
        public float frontLineGap = 2f;
        [Tooltip("Longest the side will stretch from back line to front line. Past this the front holds and the ball has to come to it.")]
        public float maxTeamLength = 42f;
        [Tooltip("Shortest. When their line is right on top of ours the front still stands this far ahead of it.")]
        public float minTeamLength = 18f;

        [Header("Decoys")]
        [Tooltip("How far out of his zone a dummy run goes, as a multiple of his roam radius.")]
        public float decoyReach = 1.6f;

        [Header("Sampling")]
        [Range(6, 48)] public int samples = 24;

        [Header("On the ball")]
        [Tooltip("Who the ball can be played to, how far, and through what. See PassPlanner.")]
        public PassRules pass = new PassRules();
        [Tooltip("Keep the ball among the back four. Not a tactic - a rig: it holds the ball in the one place that makes the opposition's pressing sequence run, so the rotation can be watched instead of waited for. See PressDrill.")]
        public bool backFourOnly;
        [Tooltip("Only with playAtOnce off. How long a bot stands on the ball before releasing it. Not a delay for its own sake - it is the window the defence has to close the lane he was going to use.")]
        public float holdMin = 0.55f;
        public float holdMax = 1.40f;
        [Tooltip("He will not strike it more than this far off his own shoulder. Turning onto the ball first is why a pressed man is slower to release it.")]
        public float alignAngle = 40f;
        [Tooltip("Longest he will spend turning before he plays it anyway, badly.")]
        public float maxTurnWait = 0.9f;
        [Tooltip("How near the ball a bot has to be to take it under control.")]
        public float collectRadius = 1.1f;
        [Tooltip("How far the ball has to have run before anybody but the passer can claim it.")]
        public float passEscape = 1.4f;
        [Tooltip("A ball played to the human is his for this long before a bot goes and takes it off him. Without it a team-mate hoovers up every ball before he can reach it.")]
        public float humanFirstRefusal = 1.2f;
        [Tooltip("How far ahead the chaser reads the ball, in seconds.")]
        public float chaseLookAhead = 3f;
        [Tooltip("Distance at which an opponent counts as full pressure on the passer.")]
        public float pressTight = 1.2f;
        public float pressLoose = 6f;

        [Header("On the ball - play it or run with it, never stand on it")]
        [Tooltip("Never stand on it. Pressed: play the open ball at once, first time if that is the ball. Free: no ball straight back or backwards - run with it until a ball forward opens or somebody closes him down. Off: the old way - stand on it for holdMin..holdMax, then pass, over the top if nothing on the floor is open.")]
        public bool playAtOnce = true;
        [Tooltip("A defender this close (read live) means he is pressed: he plays it now, back to the man who gave it him if that is the ball. Further off, he is free and goes forward.")]
        public float pressRoom = 5f;
        [Tooltip("A free man plays a ball that brings it this much nearer the goal rather than keep running. Anything less, and a free man with ground in front of him takes it forward himself.")]
        public float progressiveGain = 8f;
        [Tooltip("A ball that ends up this much further from the goal counts as going backwards. A free man does not play one while he can run.")]
        public float backPass = 2f;
        [Tooltip("How often the man on the ball asks again: is a pass on yet, and which way is free.")]
        public float carryRethink = 0.2f;
        [Tooltip("How far ahead each carry is aimed. Far enough that he never arrives and eases off; near enough that a defender closing in changes his mind.")]
        public float carryLook = 6f;
        [Range(6, 24)] public int carryDirections = 12;
        [Tooltip("Carrying pace with a defender this close or closer. 3.5 m/s puts the ball 0.54 m out (Ball.TouchPoint) - inside the 0.7 m that invites a challenge on a heavy touch.")]
        public float carrySpeedTight = 3.5f;
        public float tightRoom = 2.5f;
        [Tooltip("Carrying pace with nobody within openRoom. 7 m/s puts the ball 1.2 m out - fine while he is right about being free, a gift if he is wrong.")]
        public float carrySpeedOpen = 7f;
        public float openRoom = 10f;
        [Tooltip("Defenders this close to him are read where they are NOW when he picks a way to run; the rest from the one-second picture. Pressure at his shoulder is felt, not remembered.")]
        public float feelRadius = 10f;
        [Tooltip("Seconds he has to be ahead of every defender, all along a route, for it to count as fully open.")]
        public float carryMargin = 0.6f;
        [Tooltip("Longest he runs with it with nothing on the floor before he hits it over the top anyway.")]
        public float maxCarry = 4f;
        [Tooltip("Carries stay this far inside the touchlines and goal lines. A carried ball is never checked for going out.")]
        public float carryInset = 3f;
        [Header("Shooting - asked in the same look as the pass and the carry")]
        [Tooltip("Furthest from the goal centre a shot is even considered.")]
        public float shotRange = 25f;
        [Tooltip("A shot this likely to be a goal is taken whatever else is on. Goal = between the posts and past the keeper (GoalkeeperAI.GoalChance) x how clear of bodies the line is. 0.35: about one in three, roughly where football calls it a big chance.")]
        [Range(0f, 1f)] public float shootGood = 0.35f;
        [Tooltip("A shot this likely is taken when the alternative is losing it: pressed, boxed in, or out of time on the ball. 0.12: about an average shot in real football - better than giving it away for nothing.")]
        [Range(0f, 1f)] public float shootMin = 0.12f;
        [Tooltip("Aim points tried along the goal line, this far apart. He takes whichever is likeliest to be a goal.")]
        public float aimStep = 0.5f;
        [Tooltip("The furthest aim point is this far inside the post.")]
        public float postInset = 0.5f;
        [Tooltip("How near the shot a defender has to get to have it. Mirrors DrillDirector.interceptRadius, which decides it for real.")]
        public float blockRadius = 0.7f;
        [Tooltip("Seconds a defender must be late to the line, at its tightest point, for the line to count as fully clear.")]
        public float blockMargin = 0.25f;
        [Tooltip("Write one line per bot shot - what he expected and what happened - to shotLogPath. For checking the goal chances against real outcomes.")]
        public bool logShots = true;
        [Tooltip("Relative to the project folder, OUTSIDE Assets/ - a file written under Assets/ is imported and can set off a recompile.")]
        public string shotLogPath = "Logs/shots.csv";

        public float wCarryRoom = 1.0f;
        public float wCarryProgress = 0.6f;
        [Tooltip("Keeping on the way he is already going. Turning with the ball costs a stride, and without this the route flickers every rethink.")]
        public float wCarryKeep = 0.3f;

        /// <summary>The bot with the ball at his feet, if one of ours has it.</summary>
        public AttackerAI BallCarrier { get; private set; }

        /// <summary>The one man sent to go and get a loose ball.</summary>
        public AttackerAI Chaser { get; private set; }

        /// <summary>The last pass this side played, and who it was meant for.</summary>
        public PassPlan LastPlan { get { return lastPlan; } }

        /// <summary>
        /// Every option the last decision looked at, scored. Kept so the debug view can
        /// draw the balls he did NOT play - "why that one" is only answerable next to
        /// the ones it beat.
        /// </summary>
        public IList<PassCandidate> Candidates { get { return candidates; } }
        public Transform IntendedReceiver { get; private set; }

        /// <summary>Bumped every time a pass leaves a foot, so a listener can spot a new one.</summary>
        public int PassSerial { get; private set; }

        /// <summary>Bumped every time a bot shoots. LastShot is what he thought of it.</summary>
        public int ShotSerial { get; private set; }
        public Shot LastShot { get; private set; }

        /// <summary>Bumped when the last shot's outcome is known. ShotOutcome says what it was.</summary>
        public int ShotsResolved { get; private set; }
        public string ShotOutcome { get; private set; }

        /// <summary>A shot he could take from here, and what he makes of it.</summary>
        public struct Shot
        {
            public bool valid;
            public Vector3 from;
            public Vector3 aim;        // on the goal line
            public float distance;
            public float angle;        // degrees between the posts, seen from `from`
            public float onTarget;     // share of his spread that goes between the posts
            public float pastKeeper;   // share that goes between the posts AND past the keeper
            public float clear;        // 0 a defender gets to the line first .. 1 nobody near it
            public Vector3 keeper;     // where their keeper stood when he looked
            public float keeping;      // their keeper's attribute, -1 if there was none
            public float Goal { get { return pastKeeper * clear; } }
        }

        /// <summary>The furthest line our attackers may stand on right now.</summary>
        public float OffsideLine { get; private set; }
        public Vector3 CarrierPos { get; private set; }
        public bool Outnumbered { get; private set; }

        Transform carrier;
        Transform support;
        int showAhead = -1, showBehind = -1;
        Vector3 shift;

        /// <summary>This picture's back line and front line, measured toward the goal we attack.</summary>
        float backAlong, frontAlong;

        /// <summary>Our back line and front line this picture, as z. For the debug view.</summary>
        public float BackLineZ { get { return backAlong * (attacksPositiveZ ? 1f : -1f); } }
        public float FrontLineZ { get { return frontAlong * (attacksPositiveZ ? 1f : -1f); } }

        readonly List<Vector3> claims = new List<Vector3>();
        readonly List<float> claimRadii = new List<float>();
        readonly List<int> pickOrder = new List<int>();
        Vector3 teamSpace;
        int pictures;
        readonly List<PassCandidate> candidates = new List<PassCandidate>();
        readonly List<PitchControl.Runner> ctrlOurs = new List<PitchControl.Runner>();
        readonly List<PitchControl.Runner> ctrlTheirs = new List<PitchControl.Runner>();
        PassPlan lastPlan;
        PassPlan pending;
        bool hasPending;
        float pendingSince;
        float holdUntil = -1f;
        float looseSince = -1f;

        AttackerAI onBall;              // who the on-ball timers below belong to
        float onBallSince;
        float nextThink = -1f;
        Vector3 carryHeading;
        Transform lastPasser;           // who played our last pass
        Transform cameFrom;             // who gave the man on the ball his ball
        float nearNow;                  // nearest defender to him, as the last carry read them
        bool shotPending;
        Shot shot;
        float shotSince;
        readonly List<PitchControl.Runner> chasers = new List<PitchControl.Runner>();
        readonly List<int> chaserIndex = new List<int>();     // which of `opponents` each chaser is

        /// <summary>One way to run with the ball, and how it scored.</summary>
        struct Carry
        {
            public bool valid;
            public Vector3 target;
            public Vector3 dir;
            public float pace;
            public float margin;     // seconds ahead of the quickest defender, worst point of the route
            public float progress;   // -1 straight back .. 1 straight at goal
        }

        void Update()
        {
            if (ball == null || members == null || members.Length == 0) return;

            // Possession is handled live. Everything else on this side runs off the
            // one-second picture, but a man standing on the ball cannot be working from a
            // one-second-old version of where his team-mates are - the ball is the one
            // thing he is directly engaged with.
            TickPossession();
            WatchShot();

            if (intel.Tick(Time.time, opponents, ball.position, BlockCentre()))
                Recompute();
        }

        // ------------------------------------------------------------ possession --

        void TickPossession()
        {
            if (ballBody == null) return;

            BallCarrier = ballBody.Carrier as AttackerAI;

            // Only OUR man on the ball is our carrier. With both sides running both brains,
            // the frame a turnover happens can have this coordinator still switched on
            // while the ball sits at an opponent's feet - and without this it would pick
            // a pass for him.
            if (BallCarrier != null && !IsMember(BallCarrier)) BallCarrier = null;

            if (BallCarrier == null)
            {
                hasPending = false;
                shotPending = false;
                holdUntil = -1f;
                onBall = null;
                TickChase();
                TryCollect();
                return;
            }

            looseSince = -1f;
            SendChaser(null);

            if (playAtOnce) { TickOnBall(BallCarrier); return; }

            if (holdUntil < 0f) holdUntil = Time.time + Random.Range(holdMin, holdMax);
            if (Time.time < holdUntil) return;

            PlayPass(BallCarrier);
        }

        /// <summary>
        /// A man on the ball never just stands on it. Every carryRethink he looks again, and
        /// what he does depends first on whether anybody is on him (pressRoom, read live):
        ///
        ///   PRESSED   open ball on the floor          play it now - first time, back to
        ///                                             the man who gave it him if that is it
        ///             none, somewhere to run          run with it, away from them
        ///             none, boxed in                  over the top
        ///
        ///   FREE      a ball that goes forward        play it (progressiveGain)
        ///             ground in front of him          run with it, and keep running until
        ///                                             one of the lines above changes
        ///             no ground in front              a ball to somebody new - not straight
        ///                                             back, not backwards - then anything
        ///
        /// Played literally, "never wait" was a one-touch ball straight back to whoever had
        /// just passed it: he is already facing that man, so there is no turn to make, and
        /// 60% of all passes went back where they came from (PROJECT.md 3.17b). A free man
        /// has time; the first-time return is what a PRESSED man does with no time.
        ///
        /// "None on the floor" is asked of the gates, not of whether Choose returns
        /// something: Choose falls back to a ball over the top whenever the floor is shut,
        /// so it nearly always has an answer.
        ///
        /// The turn onto the pass stays (alignAngle / maxTurnWait). That is not waiting,
        /// it is his body.
        /// </summary>
        void TickOnBall(AttackerAI who)
        {
            if (who != onBall)
            {
                onBall = who;
                onBallSince = Time.time;
                nextThink = -1f;
                carryHeading = Flat(who.transform.forward);
                cameFrom = lastPasser;
                lastPasser = null;
                who.ClearCarry();
            }

            if (hasPending) { PlayPass(who); return; }
            if (shotPending) { TakeShot(who); return; }
            if (Time.time < nextThink) return;
            nextThink = Time.time + carryRethink;

            Vector3 from = ballBody.transform.position;
            PassPlan any = PassPlanner.Choose(who.transform, from, PassableMates(), opponents,
                                              pass, attacksPositiveZ, ballBody, candidates, control);
            Carry run = BestCarry(who);     // also reads nearNow, and builds the chasers
            Shot chance = BestShot(who, from);

            bool onFloor = any.valid && any.kind == PassKind.Ground;
            bool overdue = Time.time - onBallSince > maxCarry;
            bool canRun = run.valid && run.margin > 0f;
            bool pressed = nearNow < pressRoom;

            // The shot comes first, in the same look. A good one is taken whatever else is
            // on; a fair one only when the alternative is losing the ball - otherwise he
            // keeps going, and the carry (which leans toward goal) makes it a better one.
            if (chance.valid && (chance.Goal >= shootGood
                                 || (chance.Goal >= shootMin && (pressed || !canRun || overdue))))
            {
                CommitShot(chance, who);
                return;
            }

            if (pressed)
            {
                if (onFloor) { Commit(any, who); return; }
                if (canRun && !overdue) { RunWith(who, run); return; }
                if (any.valid) { Commit(any, who); return; }
                who.ClearCarry();
                return;
            }

            // Free. Forward by foot unless a ball forward is on.
            PassPlan onward = BestOnward(from);
            if (onward.valid && Gain(from, onward.target) >= progressiveGain) { Commit(onward, who); return; }
            if (canRun && run.progress > 0f && !overdue) { RunWith(who, run); return; }
            if (onward.valid) { Commit(onward, who); return; }
            if (any.valid) { Commit(any, who); return; }
            who.ClearCarry();
        }

        void RunWith(AttackerAI who, Carry run)
        {
            who.SetCarry(run.target, run.pace);
            carryHeading = run.dir;
        }

        void Commit(PassPlan plan, AttackerAI who)
        {
            pending = plan;
            hasPending = true;
            pendingSince = Time.time;
            PlayPass(who);
        }

        void CommitShot(Shot s, AttackerAI who)
        {
            shot = s;
            shotPending = true;
            shotSince = Time.time;
            TakeShot(who);
        }

        /// <summary>
        /// Turn onto it, then hit it - the same turn a pass gets (alignAngle / maxTurnWait),
        /// and the same kick as the human's shot (DrillDirector.Shoot): BallModel.Resolve
        /// with Base.Shot. No verdict here either. The ball flies, and whether it crosses
        /// between the posts or into a body is decided by what it actually does
        /// (PROJECT.md 3.19c).
        /// </summary>
        void TakeShot(AttackerAI who)
        {
            Vector3 from = ballBody.transform.position;
            who.FaceTarget(shot.aim);

            Vector3 aim = Flat(shot.aim - from);
            float off = Vector2.Angle(who.CarrierForward, new Vector2(aim.x, aim.z));
            bool rushed = Time.time - shotSince > maxTurnWait;
            if (off > alignAngle && !rushed) return;

            float diff = BallModel.Difficulty(Pressure(from), off, rushed, false);
            BallModel.Strike st = BallModel.Resolve(from, shot.aim, BallModel.Base.Shot, 0f, who.Shooting, diff);
            ballBody.Launch(from, st.direction, st.speed);

            who.ReleaseBall();
            lastPasser = null;
            IntendedReceiver = null;
            shot.from = from;
            LastShot = shot;
            ShotSerial++;
            shotPending = false;
            OpenShot(who);
        }

        /// <summary>
        /// The shot on from here, if there is one: where he would aim, and how likely it is
        /// to be a goal.
        ///
        /// GOAL = between the posts AND past the keeper (GoalkeeperAI.GoalChance) x how
        /// clear of bodies the line is (ShotClear). The first part is the keeper's own
        /// judgement, asked in advance: GoalChance spreads the strike over the same
        /// sideways miss the kick rolls (AimSpread x Bell(), turned into an angle the way
        /// Resolve turns it) and asks GoalkeeperAI.Judge - the function that decides the
        /// real save - about each slice. No copy of either is kept here (PROJECT.md 3.20).
        ///
        /// THE AIM is whichever point along the goal line, aimStep apart and postInset
        /// inside the posts, is likeliest to be a goal. Nothing picks a corner for him:
        /// a tight spread finds the far side of the keeper because that is where the most
        /// goals are, and a wide one is pulled toward the middle because a corner aimed at
        /// with a two-metre spread is half a miss.
        /// </summary>
        Shot BestShot(AttackerAI who, Vector3 from)
        {
            Shot best = new Shot();
            Vector3 goal = TacticalPitch.GoalCentre(attacksPositiveZ);
            if (Flat(goal - from).magnitude > shotRange) return best;

            GoalkeeperAI gk = TheirKeeper();
            float stat = who.Shooting;
            float press = Pressure(from);
            float speed = BallModel.Speed(BallModel.Base.Shot, stat);
            float decel = ballBody != null ? ballBody.rollDecel : 5f;
            float edge = TacticalPitch.GoalHalfW - postInset;
            float bestGoal = -1f;

            for (float x = -edge; x <= edge + 1e-3f; x += Mathf.Max(aimStep, 0.05f))
            {
                Vector3 aim = new Vector3(goal.x + x, 0f, goal.z);
                Vector3 d = Flat(aim - from);
                float off = Mathf.Min(Vector2.Angle(who.CarrierForward, new Vector2(d.x, d.z)), alignAngle);
                float spread = BallModel.AimSpread(stat, d.magnitude, BallModel.Difficulty(press, off, false, false));

                float on;
                float g = gk != null
                    ? gk.GoalChance(from, aim, speed, decel, spread, out on)
                    // No keeper: nobody can reach anything, so past him is just on target.
                    : GoalkeeperAI.GoalChance(from, aim, speed, decel, spread, goal, attacksPositiveZ,
                                              default(GoalkeeperAI.Model), 0f, out on);
                if (g <= bestGoal) continue;

                bestGoal = g;
                best.valid = true;
                best.aim = aim;
                best.distance = d.magnitude;
                best.onTarget = on;
                best.pastKeeper = g;
            }
            if (!best.valid) return best;

            best.from = from;
            best.angle = Mathf.Abs(Mathf.DeltaAngle(
                Heading(new Vector3(goal.x - TacticalPitch.GoalHalfW, 0f, goal.z) - from),
                Heading(new Vector3(goal.x + TacticalPitch.GoalHalfW, 0f, goal.z) - from)));
            best.keeper = gk != null ? Flat(gk.transform.position) : goal;
            best.keeping = gk != null ? gk.keeping01 : -1f;
            best.clear = ShotClear(from, best.aim, stat);
            return best;
        }

        GoalkeeperAI theirKeeper;

        /// <summary>The keeper of the goal we attack: the one body among them with a GoalkeeperAI.</summary>
        GoalkeeperAI TheirKeeper()
        {
            if (theirKeeper != null) return theirKeeper;
            for (int k = 0; opponents != null && k < opponents.Length; k++)
            {
                if (opponents[k] == null) continue;
                GoalkeeperAI gk = opponents[k].GetComponent<GoalkeeperAI>();
                if (gk != null) { theirKeeper = gk; break; }
            }
            return theirKeeper;
        }

        static float Heading(Vector3 v) { return Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg; }

        /// <summary>
        /// How clear the line is: the same race the carry runs. At the tightest points of
        /// the line, does the ball get there before any defender can get within
        /// blockRadius of it? 1 if every one of them is blockMargin late or worse, 0 if
        /// one of them is there first. The first passEscape metres are skipped - nobody
        /// takes it off his boot (DrillDirector.passEscape).
        ///
        /// The keeper is left out on purpose: the save is already the other half of the
        /// chance (GoalkeeperAI.GoalChance), and counting him here as well would count
        /// him twice.
        /// </summary>
        float ShotClear(Vector3 from, Vector3 aim, float stat)
        {
            Vector3 a = Flat(from);
            Vector3 b = Flat(aim);
            Vector3 ab = b - a;
            float len = ab.magnitude;
            if (len < 1e-3f) return 1f;
            Vector3 u = ab / len;
            float v0 = BallModel.Speed(BallModel.Base.Shot, stat);
            float decel = ballBody != null ? ballBody.rollDecel : 5f;

            float worst = float.MaxValue;
            for (int c = 0; c < chasers.Count; c++)
            {
                Transform body = opponents[chaserIndex[c]];
                if (body != null && Formation.IsKeeper(Formation.RoleOf(body))) continue;

                PitchControl.Runner r = chasers[c];
                float along = Mathf.Clamp(Vector3.Dot(Flat(r.pos) - a, u), passEscape, len);
                for (int k = 0; k < 3; k++)
                {
                    float s = k == 0 ? along : Mathf.Lerp(passEscape, len, k / 2f);
                    Vector3 p = a + u * s;
                    float tBall = Ball.TravelTime(s, v0, decel);
                    float tDef = PitchControl.TimeToReach(ref r, p.x, p.z) - blockRadius / Mathf.Max(r.top, 0.1f);
                    worst = Mathf.Min(worst, tDef - tBall);
                }
            }
            if (worst == float.MaxValue) return 1f;
            return Mathf.Clamp01(worst / Mathf.Max(blockMargin, 0.01f));
        }

        // ------------------------------------------------------------ shot log --
        // One line per bot shot: what he expected, and what the ball then did. The
        // outcome is read off the world rather than reported by anybody, the same way the
        // goal itself is (PROJECT.md 3.19c): the keeper's report for a save, the ball over
        // a line for a goal or a miss, and this side losing the ball for a block.

        bool shotOpen;
        float shotAt;
        int keeperReportsAtShot;
        string shotRole;
        float shotStat;

        void OpenShot(AttackerAI who)
        {
            if (shotOpen) CloseShot("기타");
            shotOpen = true;
            shotAt = Time.time;
            shotRole = who.role.ToString();
            shotStat = who.Shooting;
            GoalkeeperAI gk = TheirKeeper();
            keeperReportsAtShot = gk != null ? gk.Reports : 0;
        }

        /// <summary>Has the last shot finished doing whatever it did?</summary>
        void WatchShot()
        {
            if (!shotOpen || ballBody == null) return;

            GoalkeeperAI gk = TheirKeeper();
            if (gk != null && gk.Reports != keeperReportsAtShot)
            {
                keeperReportsAtShot = gk.Reports;
                if (gk.Last.saved) { CloseShot(gk.Last.held ? "잡기" : "쳐내기"); return; }
            }

            Vector3 b = ballBody.transform.position;
            float over = attacksPositiveZ ? b.z - TacticalPitch.HalfL : -TacticalPitch.HalfL - b.z;
            if (over > 0f) { CloseShot(Mathf.Abs(b.x) < TacticalPitch.GoalHalfW ? "골" : "아웃"); return; }
            if (Mathf.Abs(b.x) > TacticalPitch.HalfW) { CloseShot("아웃"); return; }

            if (ballBody.Carried && BallCarrier != null) { CloseShot("기타"); return; }
            if (Time.time - shotAt > 6f) CloseShot("기타");
        }

        /// <summary>The ball has gone to them - off a body on the way, if it was not the keeper.</summary>
        void OnDisable() { if (shotOpen) CloseShot("블록"); }

        void CloseShot(string outcome)
        {
            shotOpen = false;
            ShotOutcome = outcome;
            ShotsResolved++;
            if (!logShots) return;

            try
            {
                Shot s = LastShot;
                string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), shotLogPath);
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                if (!System.IO.File.Exists(path))
                    System.IO.File.WriteAllText(path,
                        "time,side,role,shooting,from_x,from_z,distance,angle_deg,aim_x,keeper_x,keeper_z,keeping," +
                        "p_on_target,p_past_keeper,p_clear,p_goal,outcome\n");

                var inv = System.Globalization.CultureInfo.InvariantCulture;
                string line = string.Format(inv,
                    "{0:0.00},{1},{2},{3:0.00},{4:0.0},{5:0.0},{6:0.0},{7:0.0},{8:0.00},{9:0.00},{10:0.00},{11:0.00},{12:0.000},{13:0.000},{14:0.000},{15:0.000},{16}\n",
                    shotAt, attacksPositiveZ ? "home" : "away", shotRole, shotStat,
                    s.from.x, s.from.z, s.distance, s.angle, s.aim.x, s.keeper.x, s.keeper.z, s.keeping,
                    s.onTarget, s.pastKeeper, s.clear, s.Goal, outcome);
                System.IO.File.AppendAllText(path, line);
            }
            catch (System.Exception e) { Debug.LogWarning("Shot log: " + e.Message); }
        }

        /// <summary>How much nearer the goal a ball from `from` to `to` leaves it, in metres.</summary>
        float Gain(Vector3 from, Vector3 to)
        {
            Vector3 goal = TacticalPitch.GoalCentre(attacksPositiveZ);
            return Flat(goal - from).magnitude - Flat(goal - to).magnitude;
        }

        /// <summary>
        /// The best open ball on the floor that a free man would play: not straight back to
        /// whoever gave it him, and not backwards. Read off the candidates Choose has just
        /// scored, so the gates and the score are the ones every other pass uses.
        /// </summary>
        PassPlan BestOnward(Vector3 from)
        {
            PassPlan p = new PassPlan();
            p.from = from;
            int best = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                PassCandidate c = candidates[i];
                if (!c.open || c.receiver == cameFrom) continue;
                if (Gain(from, c.target) < -backPass) continue;
                if (best < 0 || c.total > candidates[best].total) best = i;
            }
            if (best < 0) return p;

            PassCandidate w = candidates[best];
            p.valid = true;
            p.receiver = w.receiver;
            p.target = w.target;
            p.distance = w.distance;
            p.clearance = w.clearance;
            p.cellDefenders = w.cellDefenders;
            p.kind = PassKind.Ground;
            p.winner = w;
            return p;
        }

        /// <summary>
        /// Which way to run with it. Fan out carryDirections routes carryLook long and keep
        /// the one he wins by most, toward goal, without turning more than he has to.
        ///
        /// "Wins" is the same race the control grid runs (PitchControl.TimeToReach):
        /// along the route, does he get to each point before any of them can get a foot
        /// to it? That is what makes a defender running at him different from one
        /// standing in the same spot, which a distance cannot say.
        ///
        /// Pace is set by how close the nearest of them is. Ball.TouchPoint does the rest:
        /// the faster he goes, the further the ball runs off his foot - so he slows down
        /// as somebody closes, and a man who keeps sprinting into pressure is a man who
        /// gets robbed (PROJECT.md 3.10).
        /// </summary>
        Carry BestCarry(AttackerAI who)
        {
            Carry best = new Carry();
            Vector3 pos = Flat(who.transform.position);

            BuildChasers(pos);
            float near = NearestLive(pos);
            nearNow = near;
            float pace = Mathf.Lerp(carrySpeedTight, carrySpeedOpen,
                                    Mathf.InverseLerp(tightRoom, openRoom, near));

            PitchControl.Runner me = PitchControl.Of(who.transform, pos, who.Velocity, pace);
            me.top = pace;      // he races at the pace he will actually carry it, not his sprint

            Vector3 toGoal = Flat(TacticalPitch.GoalCentre(attacksPositiveZ) - pos).normalized;
            float bestScore = float.NegativeInfinity;
            int n = Mathf.Max(carryDirections, 4);

            for (int k = 0; k < n; k++)
            {
                float a = k * (2f * Mathf.PI / n);
                Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));

                // A route into the touchline is a short route, and a short route is no
                // escape - the ball is never checked for going out while it is carried.
                Vector3 end = pos + d * carryLook;
                end.x = Mathf.Clamp(end.x, -TacticalPitch.HalfW + carryInset, TacticalPitch.HalfW - carryInset);
                end.z = Mathf.Clamp(end.z, -TacticalPitch.HalfL + carryInset, TacticalPitch.HalfL - carryInset);
                Vector3 run = end - pos;
                float len = run.magnitude;
                if (len < carryLook * 0.5f) continue;

                float margin = RouteMargin(ref me, pos, end);
                float progress = Vector3.Dot(run / len, toGoal) * (len / carryLook);
                float keep = Vector3.Dot(d, carryHeading);

                float s = wCarryRoom * Mathf.Clamp(margin / Mathf.Max(carryMargin, 0.01f), -1f, 1f)
                        + wCarryProgress * progress
                        + wCarryKeep * keep;
                if (s <= bestScore) continue;

                bestScore = s;
                best.valid = true;
                best.target = end;
                best.dir = d;
                best.pace = pace;
                best.margin = margin;
                best.progress = progress;
            }
            return best;
        }

        /// <summary>
        /// His worst lead along a route, in seconds: at each third of it, how much sooner
        /// he is there than the quickest of them can get within reach of the ball.
        /// Negative means somebody gets a foot in first.
        /// </summary>
        float RouteMargin(ref PitchControl.Runner me, Vector3 from, Vector3 to)
        {
            float worst = float.MaxValue;
            for (int s = 1; s <= 3; s++)
            {
                Vector3 p = Vector3.Lerp(from, to, s / 3f);
                float mine = PitchControl.TimeToReach(ref me, p.x, p.z);

                float theirs = 99f;
                for (int c = 0; c < chasers.Count; c++)
                {
                    PitchControl.Runner r = chasers[c];
                    float t = PitchControl.TimeToReach(ref r, p.x, p.z) - Tackle.Reach / Mathf.Max(r.top, 0.1f);
                    if (t < theirs) theirs = t;
                }
                worst = Mathf.Min(worst, theirs - mine);
            }
            return worst;
        }

        /// <summary>
        /// The defenders a carry has to beat. Anyone within feelRadius is read live; the
        /// rest come from the same one-second picture as everything else this side
        /// believes (PROJECT.md 3.15, 3.24).
        ///
        /// Why the near ones are live: the man pressing him is a body at his shoulder,
        /// felt rather than remembered, and avoiding him off a picture a second old means
        /// running straight into where he now is. Why only the near ones: the delay on
        /// the rest of the pitch is the difficulty knob this side still has, and a
        /// dribbler gets no more of a look at the far side than anybody else.
        /// </summary>
        void BuildChasers(Vector3 pos)
        {
            chasers.Clear();
            chaserIndex.Clear();
            for (int k = 0; opponents != null && k < opponents.Length; k++)
            {
                if (opponents[k] == null) continue;

                Vector3 p = Flat(opponents[k].position);
                Vector3 v;
                if (Flat(p - pos).magnitude <= feelRadius)
                {
                    DefenderAI d = opponents[k].GetComponent<DefenderAI>();
                    v = d != null && d.enabled ? d.Velocity : Vector3.zero;
                }
                else
                {
                    p = Flat(intel.Projected(k, Time.time));
                    v = k < intel.OpponentVel.Length ? intel.OpponentVel[k] : Vector3.zero;
                }
                chasers.Add(PitchControl.Of(opponents[k], p, v, controlSpeed));
                chaserIndex.Add(k);
            }
        }

        /// <summary>The nearest of them, as the carry reads them (live inside feelRadius).</summary>
        float NearestLive(Vector3 pos)
        {
            float best = 99f;
            for (int c = 0; c < chasers.Count; c++)
            {
                float d = Flat(chasers[c].pos - pos).magnitude;
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>
        /// A ball nobody has is nobody's until somebody goes for it.
        ///
        /// The pass arriving is not the same as the pass being received: it is weighted
        /// with an error in metres (PROJECT.md §3.8), so it lands NEAR him, not on him.
        /// Without this the ball rolled to a stop a couple of metres from the man it was
        /// played to and the possession simply stopped - and the receiver stood over it
        /// doing nothing, because to the rest of this class he looked like the carrier.
        ///
        /// One man goes, the same rule as the defence's interception (§3.18): a loose
        /// ball that drags the whole side toward it leaves the shape behind it in ruins.
        /// The man it was played to gets first go at his own ball; if he cannot have it,
        /// whoever is nearest does.
        /// </summary>
        void TickChase()
        {
            if (ballBody.Carried) { looseSince = -1f; SendChaser(null); return; }
            if (looseSince < 0f) looseSince = Time.time;

            // A ball played to the human is his. A bot barging in to collect it before he
            // can take his touch would quietly delete the thing this prototype measures.
            AttackerAI intended = IntendedReceiver != null
                ? IntendedReceiver.GetComponent<AttackerAI>() : null;
            if (IntendedReceiver != null && intended == null
                && Time.time - looseSince < humanFirstRefusal)
            {
                SendChaser(null);
                return;
            }

            Vector3 aim = ChasePoint();

            AttackerAI pick = intended != null && intended.active && intended.CanCollect
                            ? intended : null;
            if (pick == null)
            {
                float best = float.MaxValue;
                for (int i = 0; i < members.Length; i++)
                {
                    AttackerAI m = members[i];
                    if (m == null || !m.active || !m.CanCollect) continue;
                    float d = Flat(m.transform.position - aim).sqrMagnitude;
                    if (d < best) { best = d; pick = m; }
                }
            }

            SendChaser(pick, aim);
        }

        /// <summary>
        /// Where to run to. The earliest point on the ball's path he could actually be
        /// standing on, and failing that the point it is going to stop at - never the
        /// point it currently occupies.
        /// </summary>
        Vector3 ChasePoint()
        {
            Vector3 rest = ballBody.Predict(chaseLookAhead);
            rest.y = 0f;
            if (!ballBody.InFlight) return rest;

            for (float t = 0.1f; t <= chaseLookAhead; t += 0.1f)
            {
                Vector3 p = ballBody.Predict(t);
                p.y = 0f;
                for (int i = 0; i < members.Length; i++)
                {
                    AttackerAI m = members[i];
                    if (m == null || !m.active || !m.CanCollect) continue;
                    if (Flat(p - m.transform.position).magnitude / Mathf.Max(m.sprintSpeed, 0.1f) <= t)
                        return p;
                }
            }
            return rest;
        }

        void SendChaser(AttackerAI who) { SendChaser(who, Vector3.zero); }

        void SendChaser(AttackerAI who, Vector3 aim)
        {
            Chaser = who;
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] == null) continue;
                if (members[i] == who) members[i].SetChase(aim);
                else members[i].ClearChase();
            }
        }

        /// <summary>
        /// A loose ball on the floor near one of ours becomes his. The human is not in
        /// here - the director gives him the ball, because his touch is the thing being
        /// scored and it has to go through the receiving rules first.
        /// </summary>
        void TryCollect()
        {
            if (ballBody.Carried || ballBody.Airborne) return;
            // Same reason as the defence's version: for the first stride the ball is
            // still leaving the passer's foot and the man beside him has not received
            // anything.
            if (ballBody.InFlight && ballBody.Travelled < passEscape) return;

            Vector3 bp = Flat(ballBody.transform.position);
            AttackerAI best = null;
            float bd = collectRadius;

            for (int i = 0; i < members.Length; i++)
            {
                AttackerAI m = members[i];
                if (m == null || !m.active || !m.CanCollect) continue;
                float d = Flat(m.transform.position - bp).magnitude;
                if (d < bd) { bd = d; best = m; }
            }

            if (best == null) return;
            ballBody.Attach(best);
            best.TakeBall();
        }

        /// <summary>
        /// Turn onto the ball, then hit it. The plan is cached while he turns so he does
        /// not change his mind halfway through: re-planning every frame produced a passer
        /// who pirouetted on the spot while the best option flickered between two
        /// team-mates.
        /// </summary>
        void PlayPass(AttackerAI who)
        {
            Vector3 from = ballBody.transform.position;

            if (!hasPending)
            {
                pending = PassPlanner.Choose(who.transform, from, PassableMates(), opponents,
                                             pass, attacksPositiveZ, ballBody, candidates, control);
                if (!pending.valid) { holdUntil = Time.time + 0.4f; return; }
                hasPending = true;
                pendingSince = Time.time;
            }

            who.FaceTarget(pending.target);

            Vector3 aim = Flat(pending.target - from);
            float off = Vector2.Angle(who.CarrierForward, new Vector2(aim.x, aim.z));
            bool rushed = Time.time - pendingSince > maxTurnWait;
            if (off > alignAngle && !rushed) return;

            float diff = BallModel.Difficulty(Pressure(from), off, rushed, false);

            if (pending.kind == PassKind.Lofted)
            {
                // Resolve gives the sprayed direction and the weight error. For a ball in
                // the air the weight error is a LENGTH error rather than a speed one, so
                // it is folded into how far up the line the aim point sits; the strike
                // speed it returns is unused, because Loft solves that from the distance
                // and the apex.
                BallModel.Strike st = BallModel.Resolve(from, pending.target, 0f, 0f, who.passing, diff);
                float over = 1f + st.speedErrorPct * 0.01f;
                Vector3 landing = from + st.direction * (pending.distance * over);
                ballBody.Loft(from, landing, PassPlanner.LoftApex(pending.distance, pass));
                lastPlan = pending;
                lastPlan.target = landing;
            }
            else
            {
                float v0 = Ball.SpeedToReach(pending.distance, pass.arrivePace, ballBody.rollDecel);
                BallModel.Strike st = BallModel.Resolve(from, pending.target, 0f, v0, who.passing, diff);
                ballBody.Launch(from, st.direction, st.speed);
                lastPlan = pending;
            }

            who.ReleaseBall();
            lastPasser = who.transform;
            IntendedReceiver = pending.receiver;
            PassSerial++;
            hasPending = false;
            holdUntil = -1f;
        }

        /// <summary>
        /// Who may be passed to. Everyone, unless the back-four rig is on - and then only
        /// the back four, so the ball keeps being played across the line the opposition
        /// has to come out and press.
        ///
        /// It filters the OPTIONS rather than freezing anybody: the defence is fully
        /// live, chooses its own duties and runs at whoever it likes. The only thing
        /// being held still is where the ball is allowed to go.
        /// </summary>
        IList<Transform> PassableMates()
        {
            if (!backFourOnly || mates == null) return mates;

            backFour.Clear();
            for (int i = 0; i < mates.Length; i++)
            {
                if (mates[i] == null) continue;
                Role r = Formation.RoleOf(mates[i]);
                if (Formation.IsCentreBack(r) || Formation.IsFullBack(r)) backFour.Add(mates[i]);
            }
            // If the rig has left him nobody at all, let him look at the whole side again
            // rather than stand on the ball until the round dies.
            return backFour.Count > 0 ? (IList<Transform>)backFour : mates;
        }

        readonly List<Transform> backFour = new List<Transform>();

        bool IsMember(AttackerAI a)
        {
            for (int i = 0; members != null && i < members.Length; i++)
                if (members[i] == a) return true;
            return false;
        }

        /// <summary>How closed down the man on the ball is, 0..1.</summary>
        float Pressure(Vector3 p)
        {
            return Mathf.Clamp01(Mathf.InverseLerp(pressLoose, pressTight, NearestOpponent(p)));
        }

        /// <summary>Forget the ball - a restart.</summary>
        public void ResetPossession()
        {
            BallCarrier = null;
            IntendedReceiver = null;
            Chaser = null;
            hasPending = false;
            shotPending = false;
            holdUntil = -1f;
            looseSince = -1f;
            onBall = null;
            lastPasser = null;
            lastPlan = new PassPlan();
            intel.Clear();
            control.Clear();
        }

        // ---------------------------------------------------------------- picture --

        void Recompute()
        {
            Vector3 ballPos = intel.Ball;

            RebuildControl();

            OffsideLine = Offside.Line(attacksPositiveZ, opponents, ballPos.z);
            // Only a man who ACTUALLY has the ball is the carrier. Treating "nearest
            // team-mate to the ball" as one froze him on the spot - he was told to hold
            // his position because he looked like he was on the ball, while the ball sat
            // two metres away waiting for somebody to come and get it.
            carrier = BallCarrier != null ? BallCarrier.transform : null;
            CarrierPos = carrier != null ? carrier.position : ballPos;
            support = NearestMate(CarrierPos, carrier);

            shift = ShapeShift(ballPos);
            MeasureLines();
            AssessNumbers();
            pictures++;

            // The room the side is trying to play into. A decoy runs the other way.
            teamSpace = control.Ready
                ? control.BestSpaceNear(CarrierPos, 25f, attacksPositiveZ)
                : CarrierPos;

            OpenClaims();
            OrderPicks();

            for (int o = 0; o < pickOrder.Count; o++)
            {
                int i = pickOrder[o];
                var m = members[i];

                // The man on the ball is not looking for a spot to stand in.
                if (carrier != null && m.transform == carrier)
                {
                    m.SetStation(m.transform.position, false, false, ball);
                    continue;
                }

                bool squeezed = LaneClearance(CarrierPos, m.transform.position) < squeezedClearance;
                m.TickUnstick(squeezed);

                if (Formation.RunsInBehind(m.role) && Time.time >= m.nextRunAt) m.BeginRun();
                if (Time.time >= m.nextDecoyAt && !m.Running) m.BeginDecoy();

                bool showing = i == showAhead || i == showBehind;
                Vector3 spot = BestSpot(m, i, showing, squeezed);

                // Whatever he took is his. Everyone after him has to work around it.
                claims.Add(spot);
                claimRadii.Add(claimRadius);

                m.SetStation(spot, showing, squeezed, ball);
            }
        }

        /// <summary>
        /// Start the picture with the space that is already spoken for: the man on the
        /// ball, and the human.
        ///
        /// The human is the reason this exists. He cannot be told to move over, so a bot
        /// that scores his pocket highest simply walks into him - and the one player who
        /// notices every time is the one holding the mouse. He gets a bigger claim than
        /// anybody.
        /// </summary>
        void OpenClaims()
        {
            claims.Clear();
            claimRadii.Clear();

            for (int k = 0; mates != null && k < mates.Length; k++)
            {
                if (mates[k] == null) continue;
                if (IsMember(mates[k])) continue;      // bots claim as they pick, below

                claims.Add(mates[k].position);
                claimRadii.Add(humanClaimRadius);
            }

            if (carrier != null)
            {
                claims.Add(CarrierPos);
                claimRadii.Add(claimRadius);
            }
        }

        bool IsMember(Transform t)
        {
            for (int i = 0; i < members.Length; i++)
                if (members[i] != null && members[i].transform == t) return true;
            return false;
        }

        /// <summary>
        /// Who chooses first. Claims are sequential, so the order IS a priority: a man
        /// with a job right now - running in behind, coming to show - gets the pocket he
        /// wants, and the rest arrange themselves around him. Stable, so the same picture
        /// always produces the same order.
        /// </summary>
        void OrderPicks()
        {
            pickOrder.Clear();
            for (int i = 0; i < members.Length; i++)
                if (members[i] != null) pickOrder.Add(i);

            pickOrder.Sort(delegate (int a, int b)
            {
                int pa = Priority(a), pb = Priority(b);
                return pa != pb ? pa.CompareTo(pb) : a.CompareTo(b);
            });
        }

        int Priority(int i)
        {
            if (i == showAhead || i == showBehind) return 0;    // asked to come and help
            var m = members[i];
            if (m.Running) return 1;
            if (m.Decoying) return 2;
            return 3 + Formation.LineOf(m.role) * -1;           // front line before the back
        }

        /// <summary>
        /// Rebuild the control grid from what this side can see: its own men live, the
        /// opposition as of the last picture, carried forward by however they were
        /// moving. Same staleness as every other belief this side holds.
        /// </summary>
        void RebuildControl()
        {
            ctrlOurs.Clear();
            for (int i = 0; mates != null && i < mates.Length; i++)
            {
                if (mates[i] == null) continue;
                ctrlOurs.Add(PitchControl.Of(mates[i], mates[i].position,
                                             VelocityOf(mates[i]), controlSpeed));
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

        static Vector3 VelocityOf(Transform t)
        {
            AttackerAI a = t.GetComponent<AttackerAI>();
            if (a != null) return a.Velocity;
            FootballerController f = t.GetComponent<FootballerController>();
            if (f != null) return f.Velocity;
            return Vector3.zero;
        }

        Vector3 ShapeShift(Vector3 ballPos)
        {
            float dir = attacksPositiveZ ? 1f : -1f;
            float x = Mathf.Clamp(ballPos.x * lateralFollow, -maxLateralShift, maxLateralShift);
            float z = Mathf.Clamp(ballPos.z * dir * depthFollow, -maxDepthShift, maxDepthShift);
            return new Vector3(x, 0f, z * dir);
        }

        /// <summary>
        /// Principle 5. Count the bodies around the man on the ball. If they have more
        /// than we do, the nearest team-mate in front of him and the nearest behind stop
        /// whatever else they were doing and come and offer themselves.
        /// </summary>
        void AssessNumbers()
        {
            showAhead = showBehind = -1;
            Outnumbered = false;
            if (carrier == null) return;

            float dir = attacksPositiveZ ? 1f : -1f;
            int theirs = 0, ours = 0;

            for (int k = 0; opponents != null && k < opponents.Length; k++)
            {
                if (opponents[k] == null) continue;
                if (Flat(intel.Projected(k, Time.time) - CarrierPos).magnitude < pressureRadius) theirs++;
            }
            for (int k = 0; mates != null && k < mates.Length; k++)
            {
                if (mates[k] == null || mates[k] == carrier) continue;
                if (Flat(mates[k].position - CarrierPos).magnitude < pressureRadius) ours++;
            }

            Outnumbered = theirs > ours;
            if (!Outnumbered) return;

            float bestAhead = float.MaxValue, bestBehind = float.MaxValue;
            for (int i = 0; i < members.Length; i++)
            {
                if (members[i] == null || members[i].transform == carrier) continue;
                Vector3 d = Flat(members[i].transform.position - CarrierPos);
                float along = d.z * dir;
                float dist = d.magnitude;

                if (along >= 0f && dist < bestAhead) { bestAhead = dist; showAhead = i; }
                if (along < 0f && dist < bestBehind) { bestBehind = dist; showBehind = i; }
            }
        }

        // --------------------------------------------------------------- choosing --

        /// <summary>
        /// Score a spread of spots in his zone and take the best. The spiral is fixed, so
        /// the same situation always produces the same movement.
        /// </summary>
        Vector3 BestSpot(AttackerAI m, int index, bool showing, bool squeezed)
        {
            Vector3 anchor = Anchor(m);
            float radius = Formation.ZoneRadius(m.role);
            if (m.Running) radius *= 1.5f;      // a run is allowed out of the zone
            if (showing) radius *= 1.35f;
            if (Formation.IsFullBack(m.role)) radius *= fullBackSupport;
            else if (Formation.IsCentreBack(m.role)) radius *= centreBackSupport;

            // A dummy run is not a search for a good spot. It is a deliberately bad one.
            if (m.Decoying) return DecoySpot(m, anchor, radius);

            Vector3 here = m.transform.position;
            Vector3 unstickDir = m.UnstickDir();
            float dir = attacksPositiveZ ? 1f : -1f;

            Vector3 best = anchor;
            float bestScore = float.NegativeInfinity;

            for (int k = 0; k < samples + 2; k++)
            {
                Vector3 cand;
                if (k == 0) cand = anchor;
                else if (k == 1) cand = here;
                else
                {
                    // Golden-angle spiral: even coverage, no randomness. The phase turns
                    // with the man and with each picture, because a spiral that is
                    // identical every second offers the identical winning square every
                    // second - which is most of why the movement looked like a loop.
                    // Derived rather than random; that is enough to break the loop.
                    float a = (k - 2) * 2.39996323f + index * 1.7f + pictures * 0.61f;
                    float r = radius * Mathf.Sqrt((k - 1.5f) / samples);
                    cand = anchor + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                }

                if (m.Running)
                {
                    // Attack the line - but arrive on the right side of it.
                    cand.z += dir * radius * 0.8f;
                }
                cand = Offside.KeepOnside(cand, OffsideLine, attacksPositiveZ, onsideMargin);
                cand.x = Mathf.Clamp(cand.x, -TacticalPitch.HalfW + pitchInset, TacticalPitch.HalfW - pitchInset);
                cand.z = Mathf.Clamp(cand.z, -TacticalPitch.HalfL + pitchInset, TacticalPitch.HalfL - pitchInset);

                float s = Score(m, cand, anchor, radius, here, unstickDir, showing, squeezed, dir);
                if (s > bestScore) { bestScore = s; best = cand; }
            }

            return best;
        }

        /// <summary>
        /// Where this man is supposed to be standing for this picture, before he starts
        /// looking around.
        ///
        /// For everybody but the back four that is just the slot, slid with the ball. The
        /// back four also gets pushed UP: a defence that keeps its kickoff depth while its
        /// own side has the ball leaves sixty metres between its centre-backs and its
        /// centre-forwards, nobody can play through that, and the two of them spend the
        /// whole possession standing still watching it.
        /// </summary>
        Vector3 Anchor(AttackerAI m)
        {
            Vector3 anchor = m.homeSlot + shift;
            float dir = attacksPositiveZ ? 1f : -1f;

            float t = stretchToLines ? DepthOf(m.role, anchor.x) : -1f;
            if (t >= 0f)
            {
                anchor.z = Mathf.Lerp(backAlong, frontAlong, t) * dir;
                return anchor;
            }

            if (!pushBackLine) return anchor;
            if (!Formation.IsCentreBack(m.role) && !Formation.IsFullBack(m.role)) return anchor;

            // Only ever forward. He squeezes the pitch; he never drops off his own slot.
            anchor.z = Mathf.Max(anchor.z * dir, BackLineAlong()) * dir;
            return anchor;
        }

        /// <summary>
        /// How far up the shape this role stands, 0 our back line to 1 our front line - or
        /// -1 to leave him on his slot (the back four, a far-side full-back, the keeper).
        /// </summary>
        float DepthOf(Role r, float x)
        {
            if (Formation.IsFullBack(r))
                return x * intel.Ball.x > 0f ? fullBackDepth : -1f;     // ball on his side
            switch (Formation.LineOf(r))
            {
                case 2: return frontDepth;
                case 1: return r == Role.DM ? pivotDepth : midDepth;
                default: return -1f;
            }
        }

        /// <summary>
        /// Our back line and front line for this picture, both measured toward the goal we
        /// attack.
        ///
        /// The slot plus a shift with the ball put the mezzalas at about z=14 with the ball
        /// on halfway - level with their midfield and twenty metres short of the wingers on
        /// their back four. Nobody stood in between, so the wingers were alone up there and
        /// the human had to leave the pivot to fill the gap. Pinning the shape to the two
        /// lines that actually matter - ours, and their last one - puts the midfield in
        /// the space between their lines wherever the block happens to be.
        /// </summary>
        void MeasureLines()
        {
            float dir = attacksPositiveZ ? 1f : -1f;

            // Where our centre-backs stand: their slot slid with the ball, pushed up.
            Vector3 cb = Formation.WorldPos(SlotOf(Role.LCB), !attacksPositiveZ, TacticalPitch.HalfL) + shift;
            backAlong = cb.z * dir;
            if (pushBackLine) backAlong = Mathf.Max(backAlong, BackLineAlong());

            float line = OffsideLine * dir - frontLineGap;
            frontAlong = Mathf.Clamp(line, backAlong + minTeamLength, backAlong + maxTeamLength);
        }

        static FormationSlot SlotOf(Role r)
        {
            for (int i = 0; i < Formation.F4123.Length; i++)
                if (Formation.F4123[i].role == r) return Formation.F4123[i];
            return Formation.F4123[0];
        }

        /// <summary>
        /// Where the back line should sit, measured toward the goal we are attacking.
        /// Goal-side of their deepest attacker, and never past halfway.
        /// </summary>
        float BackLineAlong()
        {
            float dir = attacksPositiveZ ? 1f : -1f;
            float deepest = float.MaxValue;

            for (int k = 0; opponents != null && k < opponents.Length; k++)
            {
                if (opponents[k] == null) continue;
                if (Formation.IsKeeper(Formation.RoleOf(opponents[k]))) continue;

                float along = intel.Projected(k, Time.time).z * dir;
                if (along < deepest) deepest = along;
            }

            if (deepest == float.MaxValue) return backLineMaxAlong;
            return Mathf.Min(deepest - backLineGap, backLineMaxAlong);
        }

        /// <summary>
        /// A dummy run: away from the room the side is trying to use, fast enough that
        /// the man marking him has to come too.
        ///
        /// It is computed rather than scored, because every term in the score is a reason
        /// to go somewhere USEFUL and a decoy is the opposite of that. Scoring it would
        /// just produce another ordinary run with a different name on it.
        /// </summary>
        Vector3 DecoySpot(AttackerAI m, Vector3 anchor, float radius)
        {
            Vector3 away = Flat(m.transform.position - teamSpace);
            if (away.sqrMagnitude < 1f) away = Flat(anchor - CarrierPos);
            if (away.sqrMagnitude < 1f) away = Vector3.right;
            away.Normalize();

            Vector3 p = anchor + away * radius * decoyReach;
            p = Offside.KeepOnside(p, OffsideLine, attacksPositiveZ, onsideMargin);
            p.x = Mathf.Clamp(p.x, -TacticalPitch.HalfW + pitchInset, TacticalPitch.HalfW - pitchInset);
            p.z = Mathf.Clamp(p.z, -TacticalPitch.HalfL + pitchInset, TacticalPitch.HalfL - pitchInset);
            return p;
        }

        /// <summary>
        /// How much this spot treads on somebody else's. Summed, so standing between two
        /// team-mates is worse than standing near one.
        /// </summary>
        float Crowding(Vector3 cand)
        {
            float sum = 0f;
            for (int i = 0; i < claims.Count; i++)
            {
                float r = claimRadii[i];
                float d = Flat(cand - claims[i]).magnitude;
                if (d < r) sum += 1f - d / r;
            }
            return sum;
        }

        float Score(AttackerAI m, Vector3 cand, Vector3 anchor, float radius, Vector3 here,
                    Vector3 unstickDir, bool showing, bool squeezed, float dir)
        {
            float total = 0f;

            // 1 - can the ball actually reach him, and does it make a shape?
            float lane = Mathf.Min(LaneClearance(CarrierPos, cand), laneOpen * 2f) / (laneOpen * 2f);
            total += wLane * lane;

            float d = Flat(cand - CarrierPos).magnitude;
            float range = d < passMin ? d / Mathf.Max(passMin, 0.01f)
                        : d > passMax ? Mathf.Max(0f, 1f - (d - passMax) / 12f)
                        : 1f;
            total += wRange * range;

            if (support != null && carrier != null)
            {
                float ang = Vector3.Angle(Flat(support.position - CarrierPos), Flat(cand - CarrierPos));
                total += wTriangle * Mathf.Clamp01(1f - Mathf.Abs(ang - idealTriangleAngle) / 120f);
            }

            // 0 - and none of it counts if he is standing in somebody else's space.
            total -= wSeparation * Crowding(cand);

            // 2 - space, and staying in his own zone while he looks for it.
            total += wSpace * (control.Ready ? control.OursAt(cand)
                                             : Mathf.Min(NearestOpponent(cand), 8f) / 8f);
            total -= wZone * Mathf.Clamp01(Flat(cand - anchor).magnitude / Mathf.Max(radius, 0.01f));

            // 3 - when he is shut down, reward the direction he is currently working in.
            if (squeezed)
            {
                Vector3 step = Flat(cand - here);
                if (step.sqrMagnitude > 0.25f)
                    total += wUnstick * Vector3.Dot(step.normalized, unstickDir);
            }

            // 4 - on a run, the closer to the line the better. Offside was already clamped.
            if (m.Running)
                total += wRun * Mathf.Clamp01(1f - Mathf.Abs(Offside.Slack(cand, OffsideLine, attacksPositiveZ)) / 6f);

            // 5 - showing for an outnumbered team-mate means getting near him, on his side.
            if (showing)
                total += wShow * Mathf.Clamp01(1f - Mathf.Abs(d - showDistance) / showDistance);

            return total;
        }

        // ------------------------------------------------------------------ utils --

        /// <summary>
        /// How much room the pass has - distance from the nearest defender to the lane.
        ///
        /// The first couple of metres are skipped deliberately. A defender standing on the
        /// carrier's toes sits near the start of EVERY lane out of him, so measuring from
        /// the carrier's feet reports every single pass as blocked and the whole side
        /// reads as permanently shut down. He is pressure on the man, which is a separate
        /// problem; he is not covering any particular passing angle.
        /// </summary>
        public float LaneClearance(Vector3 from, Vector3 to)
        {
            Vector3 a = Flat(from);
            Vector3 b = Flat(to);
            Vector3 ab = b - a;
            float len = ab.magnitude;
            if (len > carrierShadow * 2f) a += ab * (carrierShadow / len);

            float best = float.MaxValue;
            for (int k = 0; opponents != null && k < opponents.Length; k++)
            {
                if (opponents[k] == null) continue;
                float d = PointToSegment(Flat(intel.Projected(k, Time.time)), a, b);
                if (d < best) best = d;
            }
            return best == float.MaxValue ? 99f : best;
        }

        float NearestOpponent(Vector3 p)
        {
            float best = float.MaxValue;
            for (int k = 0; opponents != null && k < opponents.Length; k++)
            {
                if (opponents[k] == null) continue;
                float d = Flat(intel.Projected(k, Time.time) - p).magnitude;
                if (d < best) best = d;
            }
            return best == float.MaxValue ? 99f : best;
        }

        Transform NearestMate(Vector3 p, Transform skip)
        {
            Transform best = null;
            float bd = float.MaxValue;
            for (int k = 0; mates != null && k < mates.Length; k++)
            {
                if (mates[k] == null || mates[k] == skip) continue;
                float d = Flat(mates[k].position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = mates[k]; }
            }
            return best;
        }

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

        static float PointToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-5f) return (p - a).magnitude;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
            return (p - (a + ab * t)).magnitude;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        void OnDrawGizmosSelected()
        {
            // The offside line, drawn across the pitch.
            Gizmos.color = new Color(1f, 0.85f, 0.2f);
            Gizmos.DrawLine(new Vector3(-34f, 0.1f, OffsideLine), new Vector3(34f, 0.1f, OffsideLine));

            if (members == null) return;
            for (int i = 0; i < members.Length; i++)
            {
                var m = members[i];
                if (m == null) continue;
                Gizmos.color = m.Running ? new Color(1f, 0.4f, 0.2f)
                             : m.Showing ? new Color(0.4f, 1f, 0.5f)
                             : new Color(0.4f, 0.7f, 1f);
                Gizmos.DrawWireSphere(m.Station + Vector3.up * 0.1f, 0.8f);
                Gizmos.DrawLine(m.transform.position, m.Station);

                // The way he is running with it.
                if (m.HasBall && m.Carrying)
                {
                    Gizmos.color = new Color(1f, 0.9f, 0.3f);
                    Gizmos.DrawLine(m.transform.position + Vector3.up * 0.2f, m.CarryTarget + Vector3.up * 0.2f);
                }

                // The lane the ball would have to travel.
                if (carrier != null && m.transform != carrier)
                {
                    float c = LaneClearance(CarrierPos, m.Station);
                    Gizmos.color = c >= laneOpen
                        ? new Color(0.3f, 1f, 0.4f, 0.5f)
                        : new Color(1f, 0.3f, 0.3f, 0.35f);
                    Gizmos.DrawLine(CarrierPos + Vector3.up * 0.2f, m.Station + Vector3.up * 0.2f);
                }
            }
        }
    }
}
