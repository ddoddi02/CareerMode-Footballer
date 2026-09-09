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
    /// The candidate spread is a deterministic spiral, not Random.insideUnitCircle, so a
    /// replayed drill produces the same movement (PROJECT.md §3.6).
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
        [Tooltip("Top speed the control grid assumes everybody runs at.")]
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

        [Header("Sampling")]
        [Range(6, 48)] public int samples = 24;

        [Header("On the ball")]
        [Tooltip("Who the ball can be played to, how far, and through what. See PassPlanner.")]
        public PassRules pass = new PassRules();
        [Tooltip("How long a bot stands on the ball before releasing it. Not a delay for its own sake - it is the window the defence has to close the lane he was going to use.")]
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

        /// <summary>The furthest line our attackers may stand on right now.</summary>
        public float OffsideLine { get; private set; }
        public Vector3 CarrierPos { get; private set; }
        public bool Outnumbered { get; private set; }

        Transform carrier;
        Transform support;
        int showAhead = -1, showBehind = -1;
        Vector3 shift;

        readonly List<PassCandidate> candidates = new List<PassCandidate>();
        readonly List<Vector3> ctrlOurs = new List<Vector3>();
        readonly List<Vector3> ctrlOursVel = new List<Vector3>();
        readonly List<Vector3> ctrlTheirs = new List<Vector3>();
        readonly List<Vector3> ctrlTheirsVel = new List<Vector3>();
        PassPlan lastPlan;
        PassPlan pending;
        bool hasPending;
        float pendingSince;
        float holdUntil = -1f;
        float looseSince = -1f;

        void Update()
        {
            if (ball == null || members == null || members.Length == 0) return;

            // Possession is handled live. Everything else on this side runs off the
            // one-second picture, but a man standing on the ball cannot be working from a
            // one-second-old version of where his team-mates are - the ball is the one
            // thing he is directly engaged with.
            TickPossession();

            if (intel.Tick(Time.time, opponents, ball.position, BlockCentre()))
                Recompute();
        }

        // ------------------------------------------------------------ possession --

        void TickPossession()
        {
            if (ballBody == null) return;

            BallCarrier = ballBody.Carrier as AttackerAI;

            if (BallCarrier == null)
            {
                hasPending = false;
                holdUntil = -1f;
                TickChase();
                TryCollect();
                return;
            }

            looseSince = -1f;
            SendChaser(null);

            if (holdUntil < 0f) holdUntil = Time.time + Random.Range(holdMin, holdMax);
            if (Time.time < holdUntil) return;

            PlayPass(BallCarrier);
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
                pending = PassPlanner.Choose(who.transform, from, mates, opponents,
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
            IntendedReceiver = pending.receiver;
            PassSerial++;
            hasPending = false;
            holdUntil = -1f;
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
            holdUntil = -1f;
            looseSince = -1f;
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
            AssessNumbers();

            for (int i = 0; i < members.Length; i++)
            {
                var m = members[i];
                if (m == null) continue;

                // The man on the ball is not looking for a spot to stand in.
                if (carrier != null && m.transform == carrier)
                {
                    m.SetStation(m.transform.position, false, false, ball);
                    continue;
                }

                bool squeezed = LaneClearance(CarrierPos, m.transform.position) < squeezedClearance;
                m.TickUnstick(squeezed);

                if (Formation.RunsInBehind(m.role) && Time.time >= m.nextRunAt) m.BeginRun();

                bool showing = i == showAhead || i == showBehind;
                m.SetStation(BestSpot(m, showing, squeezed), showing, squeezed, ball);
            }
        }

        /// <summary>
        /// Rebuild the control grid from what this side can see: its own men live, the
        /// opposition as of the last picture, carried forward by however they were
        /// moving. Same staleness as every other belief this side holds.
        /// </summary>
        void RebuildControl()
        {
            ctrlOurs.Clear(); ctrlOursVel.Clear();
            for (int i = 0; mates != null && i < mates.Length; i++)
            {
                if (mates[i] == null) continue;
                ctrlOurs.Add(mates[i].position);
                ctrlOursVel.Add(VelocityOf(mates[i]));
            }

            ctrlTheirs.Clear(); ctrlTheirsVel.Clear();
            for (int k = 0; opponents != null && k < opponents.Length; k++)
            {
                if (opponents[k] == null) continue;
                ctrlTheirs.Add(intel.Projected(k, Time.time));
                ctrlTheirsVel.Add(k < intel.OpponentVel.Length ? intel.OpponentVel[k] : Vector3.zero);
            }

            control.Rebuild(ctrlOurs, ctrlOursVel, ctrlTheirs, ctrlTheirsVel, controlSpeed);
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
        Vector3 BestSpot(AttackerAI m, bool showing, bool squeezed)
        {
            Vector3 anchor = m.homeSlot + shift;
            float radius = Formation.ZoneRadius(m.role);
            if (m.Running) radius *= 1.5f;      // a run is allowed out of the zone
            if (showing) radius *= 1.35f;

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
                    // Golden-angle spiral: even coverage, no randomness.
                    float a = (k - 2) * 2.39996323f;
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
