using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// Mouse and keyboard. WASD runs, the cursor aims.
    ///
    /// The head is what the player can SEE (PROJECT.md 3.3), and it points where his
    /// body does - the way he is running (user, 2026-10-22). To see anything else he
    /// checks over his shoulder: while Q is held the head turns to the mouse. So seeing
    /// round you is something you have to do, and pay for - a check is counted
    /// (ScanCount) and slows the run (Scanning).
    ///
    /// The cursor is the aim all the time, Q or not: passes, shots and the short pass's
    /// search for a team-mate go where it points.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FootballerController : MonoBehaviour, IBallCarrier, IForm
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

        [Header("Receiving")]
        [Tooltip("Lateral pull that keeps you glued to the ball's line, m/s per metre of error.")]
        public float lineStick = 3.5f;
        public float maxLineCorrection = 2.5f;
        [Tooltip("Perpendicular distance at which the ball counts as coming AT you.")]
        public float onLineEnter = 2.4f;
        public float onLineExit = 3.2f;
        [Tooltip("Minimum effort when chasing a ball played away from you.")]
        public float chaseFloor = 0.55f;

        [Header("Who he is")]
        [Tooltip("His slot in the shape. The opposing defence reads it to decide who picks him up - a full-back marks a winger, and it has to be able to tell that this is one.")]
        public Role role = Role.DM;

        [Header("Attributes")]
        [Tooltip("Short passing, 1..100 - the ball to feet. Rolled at kickoff (DrillDirector.rollPassing). Lives here rather than on the director so there is ONE of it: the pass selector reads the same number the strike does.")]
        [Range(1, 100)] public int shortPassing = 78;
        [Tooltip("Long passing, 1..100 - the ball over the top and the through ball.")]
        [Range(1, 100)] public int longPassing = 78;

        /// <summary>Short passing on the 0..1 scale the kick model works in (Career.StatScale.To01).</summary>
        public float passing { get { return Career.StatScale.To01(shortPassing); } }
        /// <summary>Long passing, 0..1.</summary>
        public float LongPassing { get { return Career.StatScale.To01(longPassing); } }

        [Tooltip("His form, 60..120 % - how much of his ability shows up in this match (Form). 100 = as written.")]
        [Range(Form.MinPercent, Form.MaxPercent)] public int form = 100;
        public int FormPercent { get { return form; } }

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
        /// <summary>The right mouse button is held: a left click now is a shot, and the camera pulls back.</summary>
        public bool AimingShot { get; private set; }
        public bool Shielding { get; private set; }
        public float LastPassPress { get; private set; }
        public float LastThroughPress { get; private set; }
        public float LastCrossPress { get; private set; }
        public float LastLobPress { get; private set; }
        public float LastShootPress { get; private set; }

        /// <summary>
        /// A strike button. The passes go the moment they are pressed, at the pace the
        /// distance calls for; only the shot charges while held and goes on release
        /// (2026-10-22 - the passes had a gauge for two days).
        /// </summary>
        public enum Strike { None, Pass, Through, Lob, Shoot }

        [Header("Shot power - hold to strike harder")]
        [Tooltip("Seconds the shot has to be held for a full gauge. The shot goes on release (2026-10-20). Passes have no gauge.")]
        public float chargeSeconds = 1f;

        /// <summary>The shot whose button is down right now, if any.</summary>
        public Strike Charging { get; private set; }
        /// <summary>0..1 - how full the shot's gauge is.</summary>
        public float Gauge { get { return Charging == Strike.None ? 0f : Mathf.Clamp01((Time.time - chargeStart) / Mathf.Max(chargeSeconds, 0.01f)); } }
        /// <summary>When that strike was last let go - for a pass, when it was pressed - and the shot's gauge then.</summary>
        public float ReleasedAt(Strike s) { return releasedAt[(int)s]; }
        public float ReleasePower(Strike s) { return releasePower[(int)s]; }

        /// <summary>The held strike has been used already - its release does nothing.</summary>
        public void CancelCharge()
        {
            Charging = Strike.None;
            chargeSource = -1;
        }

        float chargeStart;
        int chargeSource = -1;
        readonly bool[] wasHeld = new bool[5];
        readonly float[] releasedAt = { -99f, -99f, -99f, -99f, -99f };
        readonly float[] releasePower = new float[5];

        public float BodyAngle { get; private set; }
        public float HeadAngle { get; private set; }
        public bool TrackBall { get; set; }
        public Receive Mode { get; private set; }

        /// <summary>Shoulder checks (Q) this situation, for the HUD.</summary>
        public int ScanCount { get; private set; }

        /// <summary>Where on the grass the cursor is, and the direction to it.</summary>
        public Vector3 AimPoint { get; private set; }
        public Vector2 AimDir { get; private set; }
        public bool HasAim { get; private set; }

        public float Speed { get { return new Vector2(vel.x, vel.z).magnitude; } }
        public Vector3 Velocity { get { return vel; } }

        // --- IBallCarrier: the ball does not care that this one has a human on it ---
        public Transform CarrierTransform { get { return transform; } }
        public Vector2 CarrierForward { get { return BodyForward; } }
        public float CarrierSpeed { get { return Speed; } }
        public float CarrierTopSpeed { get { return sprintSpeed; } }

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

        bool scanHeld;          // Q is down

        // receive state
        Vector3 rcvBallPos;
        Vector3 rcvBallDir = Vector3.forward;

        readonly Plane ground = new Plane(Vector3.up, 0f);

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            if (cam == null) cam = Camera.main;
            LastPassPress = -99f;
            LastThroughPress = -99f;
            LastCrossPress = -99f;
            LastLobPress = -99f;
            LastShootPress = -99f;
            CallDir = new Vector2(0f, 1f);
            TrackBall = true;
            Mode = Receive.Free;
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
            LastLobPress = -99f;
            LastShootPress = -99f;
            CancelCharge();
            for (int i = 0; i < releasedAt.Length; i++) releasedAt[i] = -99f;
            CallDir = new Vector2(0f, 1f);
            EndReceive();
            ResetScan();
            transform.rotation = Quaternion.Euler(0f, BodyAngle, 0f);
        }

        /// <summary>New situation: start counting shoulder checks again.</summary>
        public void ResetScan()
        {
            ScanCount = 0;
            scanHeld = false;
        }

        /// <summary>
        /// One strike button. Down: it counts as pressed (a call for the ball goes on the
        /// press), a pass is let go there and then, and the shot starts charging. Up: the
        /// shot is let go with whatever the gauge reached. One charge at a time - a new
        /// shot button takes over from the old.
        /// </summary>
        void PollStrike(int source, bool held, Strike kind)
        {
            if (held && !wasHeld[source])
            {
                switch (kind)
                {
                    case Strike.Pass: LastPassPress = Time.time; break;
                    case Strike.Through: LastThroughPress = Time.time; break;
                    case Strike.Lob: LastLobPress = Time.time; break;
                    case Strike.Shoot: LastShootPress = Time.time; break;
                }
                if (kind == Strike.Shoot)
                {
                    Charging = kind;
                    chargeSource = source;
                    chargeStart = Time.time;
                }
                else
                {
                    releasedAt[(int)kind] = Time.time;
                    releasePower[(int)kind] = 0f;
                }
            }
            else if (!held && wasHeld[source] && source == chargeSource && Charging != Strike.None)
            {
                releasePower[(int)Charging] = Gauge;
                releasedAt[(int)Charging] = Time.time;
                CancelCharge();
            }
            wasHeld[source] = held;
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
            // Left click is a short pass, or a shot if the right button is already down.
            AimingShot = ProtoInput.RightHeld();
            PollStrike(0, ProtoInput.LeftHeld(), AimingShot ? Strike.Shoot : Strike.Pass);
            PollStrike(1, ProtoInput.PassKeyHeld(), Strike.Pass);
            PollStrike(2, ProtoInput.ThroughHeld(), Strike.Through);
            PollStrike(3, ProtoInput.LobHeld(), Strike.Lob);
            PollStrike(4, ProtoInput.ShootKeyHeld(), Strike.Shoot);
            if (ProtoInput.CrossPressed()) LastCrossPress = Time.time;

            UpdateAim();

            UpdateFacing(dt, mv);
            UpdateMovement(dt, mv);
        }

        /// <summary>
        /// Project the cursor onto the pitch. This one ray drives both the head and the
        /// pass aim, which is why they can never disagree.
        ///
        /// The cursor sitting almost on top of him is not an aim - the direction would
        /// be whatever sub-pixel jitter says - so the last good one is kept rather than
        /// dropped. Dropping it snapped the head back to the ball whenever the mouse
        /// passed over his own feet.
        /// </summary>
        void UpdateAim()
        {
            if (cam == null || !ProtoInput.MouseAvailable()) { HasAim = false; return; }
            Ray ray = cam.ScreenPointToRay(ProtoInput.MousePosition());
            float t;
            if (!ground.Raycast(ray, out t)) return;
            Vector3 hit = ray.GetPoint(t);
            Vector3 d = hit - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 0.25f) return;
            AimPoint = hit;
            AimDir = new Vector2(d.x, d.z).normalized;
            HasAim = true;
        }

        float AngleTo(Vector3 worldPos)
        {
            Vector3 d = worldPos - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-4f) return BodyAngle;
            return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
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

            // --- head: the way his body points, unless he checks over his shoulder ----
            // (user, 2026-10-22) He looks where he is going. Only while Q is held does he
            // look away - toward the mouse - and that counts as a check and as looking away
            // (Scanning, a slower run). The cursor stays the aim either way (UpdateAim).
            bool q = ProtoInput.ScanHeld();
            if (q && !scanHeld) ScanCount++;
            scanHeld = q;

            float headTarget = BodyAngle;
            if (scanHeld)
            {
                Vector2 rs = ProtoInput.RightStick();
                if (HasAim) headTarget = Mathf.Atan2(AimDir.x, AimDir.y) * Mathf.Rad2Deg;
                else if (rs.sqrMagnitude > 0.16f) headTarget = Mathf.Atan2(rs.x, rs.y) * Mathf.Rad2Deg;
            }
            Scanning = scanHeld;

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
