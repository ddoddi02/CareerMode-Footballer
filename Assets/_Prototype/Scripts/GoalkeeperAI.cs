using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// The goalkeeper: where he stands, and whether a ball coming at his goal gets past him.
    ///
    /// Until this existed he was a body with no brain on his kickoff spot, and the ball
    /// went straight through him - every shot that crossed between the posts was a goal.
    ///
    /// WHERE HE STANDS is on the line from the ball to the middle of his goal, off his
    /// line by an amount that grows with how far away the ball is: right up on it, a
    /// metre out; for a ball 20 m away, three; never more than KeeperDepth (5 m),
    /// so the outfield floor (TeamDefence.keeperClearance) is always in front of him.
    /// Standing on that line is what narrows the angle - the posts are the same width
    /// either side of him.
    ///
    /// THE SAVE IS DECIDED ONCE, THE MOMENT THE BALL IS STRUCK, and in closed form - the
    /// same way the pass maths are (PROJECT.md 3.6, 3.7). The ball's path is a ray with
    /// rolling friction, so for any point on it Ball.TravelTime says when the ball gets
    /// there. Two points are tried: the closest the ball comes to him, and where it
    /// crosses his goal line. For each:
    ///
    ///   reach(t)  = bodyReach + diveSpeed x (t - ReactionDelay), at most MaxReach
    ///   miss      = how far that point is from where he stands
    ///
    /// MaxReach comes from his height - a 190 cm keeper on his line in the middle of the
    /// goal dives to within 1.5 balls of the bottom corner - and the dive gets there in
    /// 0.55 s (the user's yardstick, PROJECT.md 3.27). He always knows where the shot is
    /// going; what holds him back is ReactionDelay, a forced wait that shrinks with his
    /// reflexes and grows the closer the striker is.
    ///
    /// The one with more to spare is where he goes. Out of reach is a goal (if it was on
    /// target). In reach, his odds fall with how far he has to stretch - and the keeper's
    /// stat decides the odds, not the reach: nobody dives faster for being better, but
    /// the better one gets more of the balls he gets to (PROJECT.md 3.8).
    ///
    /// AFTER IT. Close to him he holds it; at full stretch he can only push it away. A
    /// ball held ends the round and his side restarts from its left centre-back
    /// (DrillDirector.KeeperCaught) - he has no attacking brain to play it with. A ball
    /// pushed away near the post goes round it; anywhere else it goes back into play,
    /// loose, and whoever is first to it has it.
    ///
    /// He acts only while his side is defending, for both a bot's shot and the human's -
    /// he watches the ball, not who struck it. With alwaysLive he acts on every ball at his
    /// goal, with no director or possession around him - the shot drill.
    ///
    /// The dive is drawn - he goes over and lies there until he gets up - but the drawing
    /// never decides anything.
    /// </summary>
    public class GoalkeeperAI : MonoBehaviour
    {
        [Header("Who he is")]
        [Tooltip("True if he keeps the goal at +Z.")]
        public bool defendsPositiveZ = true;
        [Tooltip("His slot - where he stands at kickoff.")]
        public Vector3 homeSlot;
        [Tooltip("Goalkeeping, 0..1. Decides his odds once he gets to a ball, not how far he can get.")]
        [Range(0f, 1f)] public float keeping01 = 0.65f;

        [Header("Refs - found automatically if left empty")]
        public Ball ball;
        public Possession possession;
        public DrillDirector director;

        [Header("Where he stands")]
        [Tooltip("Off his line when the ball is right on top of him.")]
        public float minDepth = 1f;
        [Tooltip("How much further off his line per metre the ball is away. Capped at Formation.KeeperDepth: 2 m for a ball 10 m out, 3 m at 20 m, 5 m from 40 m.")]
        public float depthPerMetre = 0.1f;
        [Tooltip("Getting set.")]
        public float speed = 4.5f;
        public MotionModel motion = new MotionModel();

        [Header("Reaction - a delay the system forces on him (ReactionDelay)")]
        [Tooltip("Reflexes, 1..100 (the attribute scale). Higher is a shorter forced delay.")]
        [Range(1f, 100f)] public float reflexes = 60f;
        [Tooltip("The delay at reflexes 1 and at 100, seconds, with the striker delayRefDistance away.")]
        public Vector2 delayAt1And100 = new Vector2(0.32f, 0.12f);
        [Tooltip("Striker-to-keeper distance at which the delay is just the value above. Closer is longer and further is shorter, in proportion: x refDistance / distance. 15 m is an 18 m shot against a keeper on his set point, so reflexes 60 there is 0.2 s.")]
        public float delayRefDistance = 15f;
        [Tooltip("Bounds on that proportion: no shorter than x0.4 (37.5 m and beyond), no longer than x2 (7.5 m and closer).")]
        public Vector2 delayFactorRange = new Vector2(0.4f, 2f);

        [Header("Reach")]
        [Tooltip("Off his body he can get a hand or a leg to without diving, measured to the ball's centre.")]
        public float bodyReach = 0.7f;
        [Tooltip("How fast the dive carries his hands sideways, m/s. 5: from bodyReach out to a full-length dive (3.44 m) in 0.55 s.")]
        public float diveSpeed = 5f;
        [Tooltip("His height, cm. Sets how far a full-length dive gets him (diveLengthPerHeight).")]
        public float heightCm = 190f;
        [Tooltip("Fingertips from the middle of his body on a full-length dive to the bottom corner, as a multiple of his height. 1.75 x 1.90 m = 3.33 m: standing on his line in the middle of the goal, that leaves 1.5 balls (0.33 m) to the post.")]
        public float diveLengthPerHeight = 1.75f;
        [Tooltip("Highest he can get a hand to.")]
        public float reachHeight = 2.5f;

        [Header("Without a match around him")]
        [Tooltip("Go for every ball coming at his goal whether or not a DrillDirector says the ball is live, and whoever had it. For rigs with no match around them - the shot drill.")]
        public bool alwaysLive;

        [Header("How the dive looks - never what it decides")]
        [Tooltip("How far over he goes on a full-length dive, degrees off upright.")]
        public float lieAngle = 80f;
        [Tooltip("Length of the body that is drawn, m - how far his hands are from his feet once he is down.")]
        public float drawnLength = 1.8f;

        [Header("Odds - keeping01 picks between the two ends")]
        [Tooltip("Chance of keeping out a ball straight at him, at keeping 0 and 1.")]
        public Vector2 saveStraight = new Vector2(0.85f, 0.99f);
        [Tooltip("Chance of keeping out a ball at the very end of his reach, at keeping 0 and 1.")]
        public Vector2 saveFullStretch = new Vector2(0.05f, 0.55f);
        [Tooltip("Inside this share of his reach he can hold it; beyond it he can only push it away.")]
        [Range(0f, 1f)] public float holdWithin = 0.5f;
        [Tooltip("Chance of holding a ball he could hold, at keeping 0 and 1. The rest he spills.")]
        public Vector2 hold = new Vector2(0.5f, 0.9f);

        [Header("After a save")]
        [Tooltip("Share of its pace a pushed-away ball keeps.")]
        [Range(0f, 1f)] public float parryKeep = 0.35f;
        [Tooltip("A ball heading this far or further from the middle of the goal is pushed round the post, out of play, rather than back in front of him.")]
        public float roundThePostFrom = 2.6f;
        [Tooltip("He gathers a loose ball inside his box this close to him.")]
        public float claimRadius = 1.1f;
        [Tooltip("Seconds on the floor after a dive, before he can claim or move.")]
        public float getUp = 0.8f;

        /// <summary>What happened to the last ball he faced. For the HUD and for measuring.</summary>
        public struct Report
        {
            public float distance;      // from the strike to the middle of his goal
            public bool onTarget;       // would have crossed between the posts
            public bool reachable;
            public bool saved;
            public bool held;
            public bool roundThePost;
        }

        public Report Last { get; private set; }

        /// <summary>Bumped every time a ball he went for is resolved, saved or not.</summary>
        public int Reports { get; private set; }

        /// <summary>The side he plays for.</summary>
        public Side MySide { get { return defendsPositiveZ ? Side.Away : Side.Home; } }

        Vector3 vel;
        Vector3 seenOrigin = new Vector3(float.NaN, 0f, 0f);
        float downUntil = -99f;

        // the ball he is going for
        bool going;
        bool willSave, willHold, roundPost;
        Vector3 planFrom, planDir, planPoint, planKeeper;
        float planAlong, planStruck, planSpeed, planArrive, planWait;
        Report planReport;

        // the dive as it is drawn
        bool lying;
        float diveYaw;

        void Awake()
        {
            if (ball == null) ball = FindAnyObjectByType<Ball>();
            if (possession == null) possession = FindAnyObjectByType<Possession>();
            if (director == null) director = FindAnyObjectByType<DrillDirector>();
        }

        /// <summary>Back on his spot, standing, with nothing in hand.</summary>
        public void Warp(Vector3 pos)
        {
            pos.y = 0f;
            transform.position = pos;
            transform.rotation = Formation.Facing(defendsPositiveZ);
            vel = Vector3.zero;
            going = false;
            lying = false;
            downUntil = -99f;
            if (ball != null) seenOrigin = ball.LaunchOrigin;
        }

        void Update()
        {
            if (ball == null) return;
            float dt = Time.deltaTime;
            bool live = alwaysLive || (director != null && director.isActiveAndEnabled && director.InPlay);

            if (live && Defending) WatchTheBall();
            else going = false;

            if (going) { Dive(dt); return; }
            if (Time.time < downUntil) return;
            if (lying) GetUp();

            if (live && Defending && Claim()) return;
            Position(dt);
        }

        bool Defending { get { return alwaysLive || possession == null || possession.InPossession != MySide; } }

        // ---------------------------------------------------------- where to be --

        /// <summary>On the line from the ball to the middle of his goal, off his line by distance.</summary>
        public Vector3 SetPoint()
        {
            Vector3 goal = TacticalPitch.GoalCentre(defendsPositiveZ);
            Vector3 b = ball.transform.position; b.y = 0f;
            Vector3 toBall = b - goal;
            float up = defendsPositiveZ ? -toBall.z : toBall.z;            // how far up the pitch the ball is
            float depth = Mathf.Clamp(minDepth + depthPerMetre * toBall.magnitude, minDepth, Formation.KeeperDepth);

            // Across: where the goal-to-ball line is, `depth` up the pitch from his line. A
            // ball out by the corner flag would put that beyond the post; the clamp leaves
            // him on his near post instead. Up the pitch: `depth`, always - never on the line.
            float x = goal.x + toBall.x * (depth / Mathf.Max(up, depth));
            x = Mathf.Clamp(x, goal.x - TacticalPitch.GoalHalfW, goal.x + TacticalPitch.GoalHalfW);
            return new Vector3(x, 0f, defendsPositiveZ ? goal.z - depth : goal.z + depth);
        }

        void Position(float dt)
        {
            float yaw = transform.eulerAngles.y;
            vel = PlayerMotion.Step(vel, transform.position, SetPoint(), speed, yaw, motion, dt);
            Vector3 p = transform.position + vel * dt;
            p.y = 0f;
            transform.position = p;

            Vector3 look = ball.transform.position - transform.position;
            transform.rotation = Quaternion.Euler(0f, PlayerMotion.Turn(yaw, look, motion, dt), 0f);
        }

        // ------------------------------------------------------------ the save --

        /// <summary>
        /// A new strike coming his way? Decide it now - where he will get to, and whether
        /// he keeps it out - and then let the ball arrive.
        /// </summary>
        void WatchTheBall()
        {
            if (ball.Carried || !ball.InFlight) { going = false; return; }

            // Somebody touched it after he made up his mind - think again.
            if (ball.LaunchOrigin == seenOrigin) return;
            seenOrigin = ball.LaunchOrigin;
            going = false;

            Vector3 goal = TacticalPitch.GoalCentre(defendsPositiveZ);
            Line line;
            line.from = ball.transform.position;
            line.dir = ball.Velocity;
            line.speed = ball.SpeedNow;
            line.decel = ball.rollDecel;
            Verdict v = Judge(line, transform.position, defendsPositiveZ, Params, Mathf.Max(0f, downUntil - Time.time));
            if (!v.his) return;

            // A ball over his head is not his either. Ground shots never are; this is
            // here for the day a shot is lofted.
            if (ball.Airborne && ball.Predict(v.time).y > reachHeight) { v.reachable = false; v.save = 0f; }

            Report r = new Report();
            r.distance = Flat(ball.LaunchOrigin - goal).magnitude;
            r.onTarget = v.onTarget;
            r.reachable = v.reachable;

            willSave = v.reachable && Random.value < v.save;
            willHold = willSave && v.stretch <= holdWithin && Random.value < Lerp(hold);
            roundPost = willSave && !willHold && v.onTarget && Mathf.Abs(v.lineX - goal.x) >= roundThePostFrom;
            r.saved = willSave;
            r.held = willHold;
            r.roundThePost = roundPost;

            planFrom = Flat(line.from);
            planDir = Flat(line.dir).normalized;
            planAlong = v.along;
            planPoint = v.point;
            planKeeper = Flat(transform.position);
            planStruck = Time.time;
            planWait = v.wait;
            planArrive = Time.time + v.time;
            planSpeed = Mathf.Max(line.speed - line.decel * v.time, 0f);
            planReport = r;
            diveYaw = transform.eulerAngles.y;
            going = true;
        }

        // --------------------------------------------- the judgement, as a function --
        // Pure: everything comes in as arguments, nothing is read off the scene and no
        // dice are thrown. The save above is decided with exactly this, so whoever asks
        // it in advance - the attack, deciding whether to shoot - gets the answer the
        // keeper will actually use (PROJECT.md 3.19b, 3.20).

        /// <summary>His reaction, his reach and his odds.</summary>
        public struct Model
        {
            public float reflexes01, delayRefDistance;
            public Vector2 delayAt1And100, delayFactorRange;
            public float bodyReach, diveSpeed, maxReach;
            public Vector2 saveStraight, saveFullStretch;
            public float keeping01;
        }

        /// <summary>
        /// Reflexes on 0..1. The same mapping as Career.StatScale.To01 - (v - 1) / 99 - kept
        /// here as a COPY so that _Prototype does not reference the Career namespace. If that
        /// one ever changes, change this with it.
        /// </summary>
        public float Reflexes01 { get { return Mathf.Clamp01((reflexes - 1f) / 99f); } }

        /// <summary>
        /// The forced delay before he leaves his feet - the reaction he is SEEN to have.
        ///
        /// He knows where every shot is going the moment it is struck (user, 2026-10-15) -
        /// the closed-form save is decided right then - and this is what keeps that from
        /// making him unbeatable. It falls with his reflexes and rises as the striker gets
        /// closer:
        ///
        ///   delay = lerp(delayAt1And100, reflexes01) x clamp(refDistance / distance, range)
        ///
        /// Inversely with distance because that is how fast the ball crosses his field of
        /// view: the same shot from half as far sweeps across his eyes twice as fast. Where
        /// he is going (set or still moving) does not come into it - the user's rule.
        /// `distance` is from where the ball is struck to him.
        /// </summary>
        public static float ReactionDelay(Model m, float distance)
        {
            float plain = Mathf.Lerp(m.delayAt1And100.x, m.delayAt1And100.y, m.reflexes01);
            float f = Mathf.Clamp(m.delayRefDistance / Mathf.Max(distance, 0.1f),
                                  m.delayFactorRange.x, m.delayFactorRange.y);
            return plain * f;
        }

        /// <summary>
        /// Furthest he gets to a ball's centre on a full-length dive to the bottom corner:
        /// his fingertips (height x diveLengthPerHeight) plus the ball's radius, because a
        /// fingertip on the edge of the ball is a touch. For the 190 cm keeper, 3.33 + 0.11
        /// = 3.44 m - so from the middle of the goal on his line, a ball whose centre is
        /// more than 3.44 m across, in the last 0.11 m before it touches the post, goes in
        /// untouched: the 1.5 balls of room (0.33 m) between his fingertips and the post.
        ///
        /// This is the LOW corner, the one that matters while every shot is on the ground.
        /// Higher up he gets less far across; how much less is left open.
        /// </summary>
        public float MaxReach
        {
            get { return heightCm * 0.01f * diveLengthPerHeight + (ball != null ? ball.radius : 0.11f); }
        }

        /// <summary>This keeper's numbers.</summary>
        public Model Params
        {
            get
            {
                Model m;
                m.reflexes01 = Reflexes01; m.delayAt1And100 = delayAt1And100;
                m.delayRefDistance = delayRefDistance; m.delayFactorRange = delayFactorRange;
                m.bodyReach = bodyReach; m.diveSpeed = diveSpeed; m.maxReach = MaxReach;
                m.saveStraight = saveStraight; m.saveFullStretch = saveFullStretch; m.keeping01 = keeping01;
                return m;
            }
        }

        /// <summary>A ball on the ground: where it is, which way, how fast, how fast it slows.</summary>
        public struct Line
        {
            public Vector3 from, dir;
            public float speed, decel;
        }

        public struct Verdict
        {
            public bool his;          // he goes for it: on target, or coming straight at him
            public bool onTarget;     // crosses between the posts
            public bool reachable;
            public float save;        // P(he keeps it out), 0 if out of reach
            public float stretch;     // 0 straight at him .. 1 the very end of his reach
            public float along, time; // where on the line he meets it, and when
            public float wait;        // seconds after the strike before he leaves his feet: ReactionDelay + delay
            public Vector3 point;
            public float lineX;       // where it would cross his goal line
        }

        /// <summary>
        /// Does he get to this ball, and if he does, how likely is he to keep it out.
        /// `delay` is seconds before he can move at all on top of his reaction - still on
        /// the floor from the last dive, say.
        /// </summary>
        public static Verdict Judge(Line ball, Vector3 keeper, bool defendsPositiveZ, Model m, float delay)
        {
            Verdict v = new Verdict();
            Vector3 goal = TacticalPitch.GoalCentre(defendsPositiveZ);
            Vector3 b = Flat(ball.from);
            Vector3 u = Flat(ball.dir);
            float v0 = ball.speed;
            if (v0 < 0.5f || u.sqrMagnitude < 1e-6f) return v;
            u.Normalize();

            float towardGoal = defendsPositiveZ ? u.z : -u.z;
            if (towardGoal <= 0.05f) return v;                                 // going away

            float decel = ball.decel;
            float stops = v0 * v0 / (2f * Mathf.Max(decel, 0.01f));
            float toLine = Mathf.Abs(goal.z - b.z) / towardGoal;
            v.lineX = b.x + u.x * toLine;
            bool reachesLine = stops > toLine;
            v.onTarget = reachesLine && Mathf.Abs(v.lineX - goal.x) < TacticalPitch.GoalHalfW;

            Vector3 k = Flat(keeper);
            float end = Mathf.Min(toLine, stops);
            v.wait = ReactionDelay(m, (k - b).magnitude) + delay;

            // Candidate 1: the closest the ball comes to him. Candidate 2: the line.
            float near = Mathf.Clamp(Vector3.Dot(k - b, u), 0f, end);
            float spareNear, spareLine = float.NegativeInfinity;
            float tNear = Spare(b, u, v0, decel, near, k, m, v.wait, out spareNear);
            float tLine = Mathf.Infinity;
            if (reachesLine) tLine = Spare(b, u, v0, decel, toLine, k, m, v.wait, out spareLine);

            bool lineBetter = v.onTarget && spareLine > spareNear;
            v.along = lineBetter ? toLine : near;
            v.time = lineBetter ? tLine : tNear;
            float spare = lineBetter ? spareLine : spareNear;
            v.point = b + u * v.along;

            float miss = Flat(v.point - k).magnitude;
            v.his = v.onTarget || miss <= m.bodyReach;
            if (!v.his) return v;

            v.reachable = spare >= 0f;
            if (v.reachable)
            {
                float reach = miss + spare;
                v.stretch = Mathf.Clamp01(miss / Mathf.Max(reach, 0.01f));
                v.save = Mathf.Lerp(Mathf.Lerp(m.saveStraight.x, m.saveStraight.y, m.keeping01),
                                    Mathf.Lerp(m.saveFullStretch.x, m.saveFullStretch.y, m.keeping01),
                                    v.stretch * v.stretch);
            }
            return v;
        }

        /// <summary>
        /// The chance a shot aimed at `aim` beats him: between the posts AND past him.
        ///
        /// Integrated over the same sideways miss the kick rolls - BallModel.AimSpread x
        /// Bell(), turned into an angle the way BallModel.Resolve turns it - using
        /// BallModel.BellCdf, the Irwin-Hall (n = 3) distribution TeamAttack already uses
        /// for its post-to-post share. Midpoint rule over `bins` slices of [-1, 1]. Speed
        /// error is left out: it moves the arrival time by a few hundredths.
        /// `onTarget` comes back as the post-to-post share on its own.
        /// </summary>
        public static float GoalChance(Vector3 from, Vector3 aim, float speed, float decel, float spread,
                                       Vector3 keeper, bool defendsPositiveZ, Model m, float delay,
                                       out float onTarget, int bins = 24)
        {
            Vector3 d = Flat(aim - from);
            float dist = d.magnitude;
            Vector3 dir = dist > 1e-4f ? d / dist : Vector3.forward;
            float goal = 0f;
            onTarget = 0f;

            for (int i = 0; i < bins; i++)
            {
                float lo = -1f + 2f * i / bins, hi = lo + 2f / bins;
                float w = BallModel.BellCdf(hi) - BallModel.BellCdf(lo);
                float aimM = spread * 0.5f * (lo + hi);
                float deg = Mathf.Atan2(aimM, Mathf.Max(dist, 0.5f)) * Mathf.Rad2Deg;

                Line l;
                l.from = from;
                l.dir = Quaternion.Euler(0f, deg, 0f) * dir;
                l.speed = speed;
                l.decel = decel;
                Verdict v = Judge(l, keeper, defendsPositiveZ, m, delay);
                if (!v.onTarget) continue;
                onTarget += w;
                goal += w * (1f - v.save);
            }
            return goal;
        }

        /// <summary>The same, against this keeper where he stands now.</summary>
        public float GoalChance(Vector3 from, Vector3 aim, float speed, float decel, float spread, out float onTarget)
        {
            return GoalChance(from, aim, speed, decel, spread, transform.position, defendsPositiveZ, Params,
                              Mathf.Max(0f, downUntil - Time.time), out onTarget);
        }

        /// <summary>
        /// How long the ball takes to get `along` metres down its path, and - out through
        /// `spare` - how much reach he would have left over there. Negative is out of reach.
        /// </summary>
        static float Spare(Vector3 b, Vector3 u, float v0, float decel, float along, Vector3 k, Model m, float wait, out float spare)
        {
            float t = Ball.TravelTime(along, v0, decel);
            float reach = Mathf.Min(m.bodyReach + m.diveSpeed * Mathf.Max(0f, t - wait), m.maxReach);
            spare = reach - Flat(b + u * along - k).magnitude;
            return t;
        }

        /// <summary>
        /// Go for it - after his reaction, hands toward the point, at dive speed - and when
        /// the ball gets there, do what was decided at the strike.
        /// </summary>
        void Dive(float dt)
        {
            if (ball.Carried || !ball.InFlight || ball.LaunchOrigin != seenOrigin) { going = false; return; }

            float start = planStruck + planWait;
            bool moving = Time.time >= start;
            if (moving) Pose(Mathf.Clamp01((Time.time - start) / Mathf.Max(planArrive - start, 0.05f)));

            // The ball is there when it has come far enough down its path - or will have by
            // the end of this frame. Waiting until it is past would let it cross the point
            // and the goal line in the same step, and if the director looks first that is
            // a goal: the line save is the save point ON the line.
            float came = Vector3.Dot(Flat(ball.transform.position) - planFrom, planDir);
            if (came + ball.SpeedNow * dt * 1.5f < planAlong) return;

            if (moving) Pose(1f);
            going = false;
            Reports++;
            Last = planReport;
            downUntil = Time.time + getUp;
            if (!willSave) return;

            if (willHold)
            {
                ball.Hold(planPoint);
                ball.Stop();
                if (director != null) director.KeeperCaught(this);
                return;
            }

            // Pushed away, to the side he went - measured from where he stood, since by now
            // he has gone most of the way to the point.
            float side = Mathf.Sign(planPoint.x - planKeeper.x);
            if (Mathf.Abs(planPoint.x - planKeeper.x) < 0.05f) side = Random.value < 0.5f ? -1f : 1f;
            float up = defendsPositiveZ ? -1f : 1f;                     // back up the pitch
            Vector3 dir = roundPost
                ? new Vector3(side, 0f, -up * 0.5f)                     // round the post, over the line
                : Quaternion.Euler(0f, Random.Range(-20f, 20f), 0f) * new Vector3(side * 0.9f, 0f, up);
            ball.Launch(planPoint, dir, Mathf.Max(planSpeed, 6f) * parryKeep);
            seenOrigin = ball.LaunchOrigin;                             // his own touch is not a shot
            if (director != null) director.KeeperParried(this, roundPost);
        }

        /// <summary>
        /// Draw the dive at progress p - 0 when his reaction is over, 1 when the ball gets
        /// there. The body pivots over at his feet toward the point, as far as it takes for
        /// his hands to reach it (up to lieAngle), and the feet slide across only for what
        /// the lying body still leaves short. He stays down like that until getUp is over.
        ///
        /// Nothing here feeds back into the save. That was settled at the strike, in closed
        /// form (Judge); this only shows it.
        /// </summary>
        void Pose(float p)
        {
            Vector3 to = Flat(planPoint - planKeeper);
            float need = Mathf.Min(to.magnitude, MaxReach);
            if (need < 1e-3f) return;
            Vector3 dir = to / to.magnitude;

            if (need <= bodyReach)
            {
                transform.position = planKeeper + dir * (need * 0.5f * p);     // a step, not a dive
                return;
            }

            float lean = Mathf.Min(Mathf.Asin(Mathf.Clamp01(need / drawnLength)) * Mathf.Rad2Deg, lieAngle);
            float slide = Mathf.Max(0f, need - drawnLength * Mathf.Sin(lean * Mathf.Deg2Rad));
            transform.position = planKeeper + dir * (slide * p);
            transform.rotation = Quaternion.AngleAxis(lean * p, Vector3.Cross(Vector3.up, dir))
                               * Quaternion.Euler(0f, diveYaw, 0f);
            lying = true;
        }

        void GetUp()
        {
            transform.rotation = Quaternion.Euler(0f, diveYaw, 0f);
            lying = false;
        }

        /// <summary>A loose ball in his box, close enough to fall on.</summary>
        bool Claim()
        {
            if (ball.Carried || ball.Airborne) return false;
            Vector3 b = Flat(ball.transform.position);
            if (!TacticalPitch.InPenaltyBox(b, defendsPositiveZ)) return false;
            if (Flat(b - transform.position).magnitude > claimRadius) return false;

            ball.Hold(b);
            ball.Stop();
            Reports++;
            Report r = new Report();
            r.distance = Flat(ball.LaunchOrigin - TacticalPitch.GoalCentre(defendsPositiveZ)).magnitude;
            r.saved = true; r.held = true; r.reachable = true;
            Last = r;
            if (director != null) director.KeeperCaught(this);
            return true;
        }

        float Lerp(Vector2 ends) { return Mathf.Lerp(ends.x, ends.y, keeping01); }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
