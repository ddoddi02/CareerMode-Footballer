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
                    // Derived, not random, so a replay still reproduces it (PROJECT.md 3.6).
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
            if (!pushBackLine) return anchor;
            if (!Formation.IsCentreBack(m.role) && !Formation.IsFullBack(m.role)) return anchor;

            float dir = attacksPositiveZ ? 1f : -1f;
            float lineAlong = BackLineAlong();
            float slotAlong = anchor.z * dir;

            // Only ever forward. He squeezes the pitch; he never drops off his own slot.
            anchor.z = Mathf.Max(slotAlong, lineAlong) * dir;
            return anchor;
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
