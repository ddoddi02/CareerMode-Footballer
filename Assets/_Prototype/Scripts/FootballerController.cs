using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// Keyboard-first, FC-conventional. The stick moves you, the torso follows your
    /// run, sprint is a modifier, pass and through-ball are buffered presses.
    ///
    /// The only addition is the SHOULDER CHECK on Q: hold it and your head turns
    /// round, release and it snaps back to the ball. Which shoulder you turn over is
    /// chosen for you by where the pitch actually is - see ChooseScanSide.
    ///
    /// Mouse control is commented out for now (see the [MOUSE] blocks) while the
    /// keyboard scheme is being tuned.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FootballerController : MonoBehaviour
    {
        /// <summary>How the receiver is allowed to move while a pass is live.</summary>
        public enum Receive
        {
            Free,     // not receiving
            OnLine,   // the ball is coming at you: only along its travel axis
            Chase     // the ball is not coming at you: you can only go to it
        }

        [Header("Movement")]
        public float walkSpeed = 5.6f;
        public float sprintSpeed = 7.9f;
        public float accel = 40f;
        public float scanSpeedMul = 0.9f;

        [Header("Turning (deg/sec)")]
        public float bodyTurn = 640f;
        public float headTurn = 760f;

        [Header("Shoulder check (Q)")]
        [Tooltip("How far round your head goes while Q is held.")]
        public float scanAngle = 140f;

        [Header("Pitch (for scan side selection)")]
        public float pitchHalfX = 34f;    // 68 m wide
        public float pitchHalfZ = 52.5f;  // 105 m long
        public float goalX = 0f;
        [Tooltip("Inside this distance from a touchline you always look infield, and your gaze never crosses the line.")]
        public float sidelineMargin = 5f;
        [Tooltip("The opponent whose last known side biases later shoulder checks.")]
        public Perceivable threat;

        [Header("Receiving")]
        [Tooltip("Lateral pull that keeps you glued to the ball's line, m/s per metre of error.")]
        public float lineStick = 3.5f;
        public float maxLineCorrection = 2.5f;
        [Tooltip("Perpendicular distance at which the ball counts as coming AT you.")]
        public float onLineEnter = 2.4f;
        public float onLineExit = 3.2f;
        [Tooltip("Minimum effort when chasing a ball played away from you.")]
        public float chaseFloor = 0.55f;

        [Header("Refs")]
        public Camera cam;
        public Transform headMarker;
        public Transform lookTarget;    // the ball

        // -- state the director reads --------------------------------------------
        public Vector2 InputDir { get; private set; }
        public Vector2 CallDir { get; private set; }
        public bool Holding { get; private set; }
        public bool Sprinting { get; private set; }
        public bool Scanning { get; private set; }
        public bool Shielding { get; private set; }
        public float LastPassPress { get; private set; }
        public float LastThroughPress { get; private set; }
        public float LastCrossPress { get; private set; }
        public float LastShootPress { get; private set; }
        public float BodyAngle { get; private set; }
        public float HeadAngle { get; private set; }
        public bool TrackBall { get; set; }
        public Receive Mode { get; private set; }

        // -- scan telemetry, for the HUD -----------------------------------------
        public int ScanCount { get; private set; }
        public int ScanPitchSide { get; private set; }   // +1 = looked toward +x, -1 = toward -x
        public bool NearSideline { get; private set; }
        public float AreaLeft { get; private set; }
        public float AreaRight { get; private set; }
        public string ScanReason { get; private set; }
        public bool ScanClamped { get; private set; }

        // [MOUSE] kept so the director still compiles while the mouse is parked.
        public Vector3 AimPoint { get; private set; }
        public Vector2 AimDir { get; private set; }
        public bool HasAim { get; private set; }

        public float Speed { get { return new Vector2(vel.x, vel.z).magnitude; } }
        public Vector3 Velocity { get { return vel; } }

        public Vector2 BodyForward
        {
            get { float r = BodyAngle * Mathf.Deg2Rad; return new Vector2(Mathf.Sin(r), Mathf.Cos(r)); }
        }

        public Vector2 HeadForward
        {
            get { float r = HeadAngle * Mathf.Deg2Rad; return new Vector2(Mathf.Sin(r), Mathf.Cos(r)); }
        }

        CharacterController cc;
        Vector3 vel;

        // scan state
        bool scanHeld;
        int scanShoulder = 1;
        int lastPitchSide;
        float scanEpoch;

        // receive state
        Vector3 rcvBallPos;
        Vector3 rcvBallDir = Vector3.forward;

        // [MOUSE] readonly Plane ground = new Plane(Vector3.up, 0f);

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            if (cam == null) cam = Camera.main;
            LastPassPress = -99f;
            LastThroughPress = -99f;
            LastCrossPress = -99f;
            LastShootPress = -99f;
            CallDir = new Vector2(0f, 1f);
            TrackBall = true;
            Mode = Receive.Free;
            ScanReason = "-";
        }

        public void Teleport(Vector3 pos, float bodyAngle)
        {
            cc.enabled = false;
            transform.position = pos;
            cc.enabled = true;
            BodyAngle = bodyAngle;
            HeadAngle = bodyAngle;
            vel = Vector3.zero;
            LastPassPress = -99f;
            LastThroughPress = -99f;
            LastCrossPress = -99f;
            LastShootPress = -99f;
            CallDir = new Vector2(0f, 1f);
            EndReceive();
            ResetScan();
            transform.rotation = Quaternion.Euler(0f, BodyAngle, 0f);
        }

        /// <summary>New situation: forget which way you have already looked.</summary>
        public void ResetScan()
        {
            ScanCount = 0;
            lastPitchSide = 0;
            scanHeld = false;
            scanEpoch = Time.time;
            ScanReason = "-";
        }

        // -------------------------------------------------------- receiving ----

        public void BeginReceive() { Mode = Receive.Chase; }
        public void EndReceive() { Mode = Receive.Free; }

        public void UpdateReceive(Vector3 ballPos, Vector3 ballVelocity, bool moving)
        {
            if (Mode == Receive.Free) return;

            rcvBallPos = new Vector3(ballPos.x, 0f, ballPos.z);

            Vector3 d = ballVelocity;
            d.y = 0f;
            if (moving && d.sqrMagnitude > 0.01f) rcvBallDir = d.normalized;

            if (!moving) { Mode = Receive.Chase; return; }

            Vector3 toMe = transform.position - rcvBallPos;
            toMe.y = 0f;
            float along = Vector3.Dot(toMe, rcvBallDir);
            float lateral = (toMe - rcvBallDir * along).magnitude;

            bool stillComing = along > -0.5f;
            float threshold = Mode == Receive.OnLine ? onLineExit : onLineEnter;

            Mode = (stillComing && lateral < threshold) ? Receive.OnLine : Receive.Chase;
        }

        // ------------------------------------------------------------- loop ----

        void Update()
        {
            float dt = Time.deltaTime;

            Vector2 mv = ProtoInput.Move();
            InputDir = mv;
            if (mv.sqrMagnitude > 0.25f) CallDir = mv.normalized;

            Holding = ProtoInput.HoldHeld();
            Sprinting = ProtoInput.SprintHeld();
            Shielding = ProtoInput.ShieldHeld();
            if (ProtoInput.PassPressed()) LastPassPress = Time.time;
            if (ProtoInput.ThroughPassPressed()) LastThroughPress = Time.time;
            if (ProtoInput.CrossPressed()) LastCrossPress = Time.time;
            if (ProtoInput.ShootPressed()) LastShootPress = Time.time;

            // [MOUSE] UpdateAim();
            HasAim = false;

            UpdateFacing(dt, mv);
            UpdateMovement(dt, mv);
        }

        // [MOUSE] ------------------------------------------------------------------
        // Project the cursor onto the pitch, driving both the head and the pass aim.
        // Parked while the keyboard scheme is being tuned.
        //
        // void UpdateAim()
        // {
        //     if (cam == null || !ProtoInput.MouseAvailable()) { HasAim = false; return; }
        //     Ray ray = cam.ScreenPointToRay(ProtoInput.MousePosition());
        //     float t;
        //     if (!ground.Raycast(ray, out t)) return;
        //     Vector3 hit = ray.GetPoint(t);
        //     Vector3 d = hit - transform.position;
        //     d.y = 0f;
        //     if (d.sqrMagnitude < 0.25f) return;
        //     AimPoint = hit;
        //     AimDir = new Vector2(d.x, d.z).normalized;
        //     HasAim = true;
        // }
        // --------------------------------------------------------------------------

        float AngleTo(Vector3 worldPos)
        {
            Vector3 d = worldPos - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-4f) return BodyAngle;
            return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        // ------------------------------------------------------ shoulder check --

        static float PolyArea(Vector2[] v)
        {
            float a = 0f;
            for (int i = 0; i < v.Length; i++)
            {
                Vector2 c = v[i], n = v[(i + 1) % v.Length];
                a += c.x * n.y - n.x * c.y;
            }
            return Mathf.Abs(a) * 0.5f;
        }

        /// <summary>
        /// Draw a line from where you stand to each goal. That polyline cuts the
        /// pitch in two; the bigger piece is the side worth looking at first,
        /// because that is where the play can actually come from.
        /// </summary>
        void ComputeAreas()
        {
            Vector3 p = transform.position;
            Vector2 P = new Vector2(p.x, p.z);
            Vector2 G1 = new Vector2(goalX, pitchHalfZ);
            Vector2 G2 = new Vector2(goalX, -pitchHalfZ);

            AreaRight = PolyArea(new Vector2[] {
                P, G1, new Vector2(pitchHalfX, pitchHalfZ), new Vector2(pitchHalfX, -pitchHalfZ), G2 });
            AreaLeft = PolyArea(new Vector2[] {
                P, G1, new Vector2(-pitchHalfX, pitchHalfZ), new Vector2(-pitchHalfX, -pitchHalfZ), G2 });
        }

        /// <summary>Which pitch side the opponent was last actually seen on, 0 if unknown.</summary>
        int KnownThreatSide()
        {
            if (threat == null) return 0;
            if (threat.LastSeenTime <= scanEpoch) return 0;   // nothing seen this situation
            float dx = threat.LastSeenPos.x - transform.position.x;
            if (Mathf.Abs(dx) < 0.5f) return 0;               // dead astern: no clear side
            return dx > 0f ? 1 : -1;
        }

        /// <summary>
        /// Turning by +angle sweeps the head through the body's right vector, whose
        /// world x-component is cos(bodyYaw). So pick the shoulder whose sweep
        /// actually travels toward the pitch side we want.
        /// </summary>
        int ShoulderFor(int pitchSide)
        {
            float c = Mathf.Cos(BodyAngle * Mathf.Deg2Rad);
            if (Mathf.Abs(c) < 0.15f) return scanShoulder;    // facing along x: degenerate
            return (c * pitchSide > 0f) ? 1 : -1;
        }

        void ChooseScanSide()
        {
            ComputeAreas();
            int wide = AreaRight > AreaLeft ? 1 : -1;
            NearSideline = (pitchHalfX - Mathf.Abs(transform.position.x)) <= sidelineMargin;

            int side;
            if (NearSideline)
            {
                side = wide;                       // a winger never looks at the advertising boards
                ScanReason = "터치라인 — 항상 넓은 쪽";
            }
            else if (ScanCount <= 1)
            {
                side = wide;
                ScanReason = "첫 확인 — 넓은 쪽";
            }
            else if (ScanCount == 2)
            {
                side = -wide;
                ScanReason = "두 번째 — 반대쪽";
            }
            else
            {
                int known = KnownThreatSide();
                if (known != 0)
                {
                    side = known;
                    ScanReason = "상대를 본 쪽";
                }
                else
                {
                    side = lastPitchSide != 0 ? -lastPitchSide : wide;
                    ScanReason = "미확인 — 계속 번갈아";
                }
            }

            lastPitchSide = side;
            ScanPitchSide = side;
            scanShoulder = ShoulderFor(side);
        }

        /// <summary>
        /// Near a touchline your gaze is not allowed past it - there is nothing out
        /// there to see. Clamp to whichever direction along the line is closer.
        /// </summary>
        float ClampScanToPitch(float yaw)
        {
            ScanClamped = false;
            float px = transform.position.x;
            if ((pitchHalfX - Mathf.Abs(px)) > sidelineMargin) return yaw;

            float outward = Mathf.Sign(px);
            float s = Mathf.Sin(yaw * Mathf.Deg2Rad);
            if (s * outward <= 0f) return yaw;              // already inward or parallel

            ScanClamped = true;
            float toZero = Mathf.Abs(Mathf.DeltaAngle(yaw, 0f));
            float to180 = Mathf.Abs(Mathf.DeltaAngle(yaw, 180f));
            return toZero < to180 ? 0f : 180f;
        }

        void UpdateFacing(float dt, Vector2 mv)
        {
            // --- torso: follows where you are actually going ----------------------
            float bodyTarget = BodyAngle;
            if (Speed > 0.4f)
                bodyTarget = Mathf.Atan2(vel.x, vel.z) * Mathf.Rad2Deg;
            else if (mv.sqrMagnitude > 0.09f && Mode == Receive.Free && !Holding)
                bodyTarget = Mathf.Atan2(mv.x, mv.y) * Mathf.Rad2Deg;
            else if (TrackBall && lookTarget != null)
                bodyTarget = AngleTo(lookTarget.position);

            BodyAngle = Mathf.MoveTowardsAngle(BodyAngle, bodyTarget, bodyTurn * dt);

            // --- shoulder check: hold to look, release to snap back ---------------
            bool q = ProtoInput.ScanHeld();
            if (q && !scanHeld)
            {
                scanHeld = true;
                ScanCount++;
                ChooseScanSide();   // side rules still apply, see ChooseScanSide
            }
            else if (!q && scanHeld)
            {
                scanHeld = false;
            }

            // --- head -------------------------------------------------------------
            float headTarget;
            Vector2 rs = ProtoInput.RightStick();

            if (scanHeld)
            {
                headTarget = ClampScanToPitch(BodyAngle + scanAngle * scanShoulder);
                Scanning = true;
            }
            else if (rs.sqrMagnitude > 0.16f)
            {
                headTarget = Mathf.Atan2(rs.x, rs.y) * Mathf.Rad2Deg;
                Scanning = true;
            }
            // [MOUSE] else if (HasAim)
            // {
            //     headTarget = Mathf.Atan2(AimDir.x, AimDir.y) * Mathf.Rad2Deg;
            //     Scanning = Mathf.Abs(Mathf.DeltaAngle(BodyAngle, headTarget)) > 70f;
            // }
            else
            {
                Scanning = false;
                ScanClamped = false;
                headTarget = (TrackBall && lookTarget != null) ? AngleTo(lookTarget.position) : BodyAngle;
            }

            HeadAngle = Mathf.MoveTowardsAngle(HeadAngle, headTarget, headTurn * dt);

            transform.rotation = Quaternion.Euler(0f, BodyAngle, 0f);
            if (headMarker != null)
                headMarker.localRotation = Quaternion.Euler(0f, Mathf.DeltaAngle(BodyAngle, HeadAngle), 0f);
        }

        void UpdateMovement(float dt, Vector2 mv)
        {
            float top = Sprinting ? sprintSpeed : walkSpeed;
            if (Scanning) top *= scanSpeedMul;
            if (Shielding) top *= 0.65f;

            Vector3 want = new Vector3(mv.x, 0f, mv.y) * top;

            // Feet planted: the stick is a request, not a run. Never allowed to
            // override a chase - you always have to go and get your own ball.
            if (Holding && Mode != Receive.Chase) want = Vector3.zero;

            if (Mode == Receive.OnLine)
            {
                Vector3 axis = rcvBallDir;
                want = axis * Vector3.Dot(want, axis);

                Vector3 rel = transform.position - rcvBallPos;
                rel.y = 0f;
                Vector3 lat = rel - axis * Vector3.Dot(rel, axis);
                want += Vector3.ClampMagnitude(-lat * lineStick, maxLineCorrection);
            }
            else if (Mode == Receive.Chase)
            {
                Vector3 toBall = rcvBallPos - transform.position;
                toBall.y = 0f;
                if (toBall.sqrMagnitude > 1e-4f)
                {
                    float effort = Mathf.Max(chaseFloor, mv.magnitude);
                    want = toBall.normalized * top * effort;
                }
                else want = Vector3.zero;
            }

            vel = Vector3.MoveTowards(vel, want, accel * dt);
            cc.Move((vel + Vector3.down * 3f) * dt);
        }
    }
}
