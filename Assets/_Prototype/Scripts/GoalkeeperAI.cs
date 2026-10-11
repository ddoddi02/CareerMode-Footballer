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
    ///   reach(t)  = bodyReach + diveSpeed x (t - reaction), at most maxReach
    ///   miss      = how far that point is from where he stands
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
    /// he watches the ball, not who struck it.
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

        [Header("Reach")]
        [Tooltip("Seconds after the strike before he moves at all.")]
        public float reaction = 0.25f;
        [Tooltip("Off his body he can get a hand or a leg to without diving.")]
        public float bodyReach = 0.7f;
        [Tooltip("How fast the dive carries his hands sideways, m/s.")]
        public float diveSpeed = 4.5f;
        [Tooltip("Furthest his fingertips get from where he was standing - a full-length dive.")]
        public float maxReach = 2.8f;
        [Tooltip("Highest he can get a hand to.")]
        public float reachHeight = 2.5f;

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
        float planAlong, planStruck, planSpeed;
        Report planReport;

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
            downUntil = -99f;
            if (ball != null) seenOrigin = ball.LaunchOrigin;
        }

        void Update()
        {
            if (ball == null) return;
            float dt = Time.deltaTime;
            bool live = director != null && director.isActiveAndEnabled && director.InPlay;

            if (live && Defending) WatchTheBall();
            else going = false;

            if (going) { Dive(dt); return; }
            if (Time.time < downUntil) return;

            if (live && Defending && Claim()) return;
            Position(dt);
        }

        bool Defending { get { return possession == null || possession.InPossession != MySide; } }

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
            planSpeed = Mathf.Max(line.speed - line.decel * v.time, 0f);
            planReport = r;
            going = true;
        }

        // --------------------------------------------- the judgement, as a function --
        // Pure: everything comes in as arguments, nothing is read off the scene and no
        // dice are thrown. The save above is decided with exactly this, so whoever asks
        // it in advance - the attack, deciding whether to shoot - gets the answer the
        // keeper will actually use (PROJECT.md 3.19b, 3.20).

        /// <summary>His reach and his odds.</summary>
        public struct Model
        {
            public float reaction, bodyReach, diveSpeed, maxReach;
            public Vector2 saveStraight, saveFullStretch;
            public float keeping01;
        }

        /// <summary>This keeper's numbers.</summary>
        public Model Params
        {
            get
            {
                Model m;
                m.reaction = reaction; m.bodyReach = bodyReach; m.diveSpeed = diveSpeed; m.maxReach = maxReach;
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

            // Candidate 1: the closest the ball comes to him. Candidate 2: the line.
            float near = Mathf.Clamp(Vector3.Dot(k - b, u), 0f, end);
            float spareNear, spareLine = float.NegativeInfinity;
            float tNear = Spare(b, u, v0, decel, near, k, m, delay, out spareNear);
            float tLine = Mathf.Infinity;
            if (reachesLine) tLine = Spare(b, u, v0, decel, toLine, k, m, delay, out spareLine);

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
        static float Spare(Vector3 b, Vector3 u, float v0, float decel, float along, Vector3 k, Model m, float delay, out float spare)
        {
            float t = Ball.TravelTime(along, v0, decel);
            float reach = Mathf.Min(m.bodyReach + m.diveSpeed * Mathf.Max(0f, t - m.reaction - delay), m.maxReach);
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

            Vector3 to = Flat(planPoint - transform.position);
            if (Time.time >= planStruck + reaction && to.magnitude > bodyReach * 0.5f)
                transform.position += to.normalized * Mathf.Min(diveSpeed * dt, to.magnitude - bodyReach * 0.5f);

            // The ball is there when it has come far enough down its path - or will have by
            // the end of this frame. Waiting until it is past would let it cross the point
            // and the goal line in the same step, and if the director looks first that is
            // a goal: the line save is the save point ON the line.
            float came = Vector3.Dot(Flat(ball.transform.position) - planFrom, planDir);
            if (came + ball.SpeedNow * dt * 1.5f < planAlong) return;

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
