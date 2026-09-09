using UnityEngine;

namespace Prototype
{
    public enum PressStyle
    {
        Tight,  // right on your back
        Loose,  // a couple of metres off
        Drop    // dropped away, you are free to turn
    }

    /// <summary>What the man nearest the ball has been told to do about it.</summary>
    public enum PressMode
    {
        None,
        /// <summary>Get tight and stop him turning. The default.</summary>
        Deny,
        /// <summary>Stand off and delay him - TeamDefence owns the position, he just holds it and faces the ball.</summary>
        Contain
    }

    /// <summary>
    /// One defender. He does not decide where the shape should be - <see cref="TeamDefence"/>
    /// hands him a station every time the defence takes a new picture - but he decides how
    /// to stand once he gets there, and when to go.
    ///
    /// THE PRESS IS NOT AN ATTEMPT TO WIN THE BALL. Diving in at a man who is facing his
    /// own goal is how you get turned. The job is to stop him turning at all: stand
    /// goal-side and shade the INSIDE shoulder, so the only comfortable ball he has left
    /// is square to the touchline or backwards. A centre-forward who receives in the
    /// middle and has to play it back has been defended perfectly, and nobody tackled
    /// anyone.
    ///
    /// The tackle then has exactly one trigger: he tries to turn anyway while you are
    /// tight. That is the moment his body is across the ball and it is furthest from his
    /// feet, and you either take it or knock it out of play. Everything else - the
    /// random lunge - is what gives up the goal.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class DefenderAI : MonoBehaviour
    {
        [Header("Who he is")]
        public Role role = Role.LCB;
        [Tooltip("His slot in the formation. TeamDefence slides the whole shape off these.")]
        public Vector3 homeSlot;

        [Header("Refs")]
        public Transform target;
        [Tooltip("When set he abandons his station and goes for this - the ball, once it is being carried.")]
        [HideInInspector] public Transform chase;
        public float speed = 5.8f;
        public bool active = true;
        [Tooltip("What his body can actually do: how fast he starts, stops and turns. See PlayerMotion.")]
        public MotionModel motion = new MotionModel();

        [Header("Denying the turn")]
        [Tooltip("How far off the carrier he sets up. Close enough that turning into him is a bad idea.")]
        public float denyGap = 1.05f;
        [Tooltip("How far to the inside shoulder he shades, which is what leaves the touchline open.")]
        public float shoulderShade = 0.85f;
        [Tooltip("Extra shading on a break - showing him wide matters more when he is running at you.")]
        public float breakShade = 1.5f;
        [Tooltip("He counts as tight inside this. Only from here does an attempted turn become a tackle.")]
        public float tightRange = 1.5f;
        [Tooltip("Degrees off our goal within which the carrier counts as having turned in.")]
        public float turnedInAngle = 75f;

        [Header("Going for a loose ball")]
        [Tooltip("Pace he moves at when he has been sent to cut a pass out. Reading the ball early is worth more than being quick, but he still has to get there.")]
        public float interceptSpeed = 7.4f;

        [Header("Tackle")]
        [Tooltip("He will commit from this far off the ball.")]
        public float lungeRange = 1.9f;
        [Tooltip("Touch distance that reads as a heavy touch and invites the challenge.")]
        public float exposureTrigger = 0.9f;
        [Tooltip("Chance per second of going in on a tucked-in ball with no turn on. Deliberately near zero - he is not trying to win it.")]
        public float gambleChance = 0.05f;
        public float tackleCooldown = 1.1f;
        [Tooltip("How long he is out of it after missing.")]
        public float missRecovery = 1.0f;
        [Range(0f, 1f)] public float tackling01 = 0.60f;

        public PressStyle Style { get; private set; }
        public bool Recovering { get { return Time.time < recoverUntil; } }

        /// <summary>Set by TeamDefence: the man he has been told to pick up, if any.</summary>
        [HideInInspector] public Vector3? markTarget;

        /// <summary>Set by TeamDefence: he is filling a hole rather than holding his own slot.</summary>
        [HideInInspector] public bool covering;

        /// <summary>True while he is the one closing the ball down.</summary>
        public bool Pressing { get { return pressing; } }

        /// <summary>Engaging him, or standing off and delaying him.</summary>
        public PressMode Mode { get { return mode; } }

        /// <summary>True while he has been sent to cut a pass out. Overrides everything else.</summary>
        public bool Intercepting { get; private set; }

        /// <summary>The point on the ball's path he was sent to.</summary>
        public Vector3 InterceptPoint { get { return interceptPoint; } }

        /// <summary>His current orders - where TeamDefence wants him standing.</summary>
        public Vector3 Station { get { return station; } }

        Vector3 station;
        Vector3 interceptPoint;
        PressMode mode = PressMode.Deny;
        bool pressing;
        Vector3 carrierPos;
        bool defendsPositiveZ = true;
        bool onBreak;

        float lateral;
        bool drillShape;          // Begin() was called - the drill owns his shape
        float switchAfter = -1f;
        PressStyle switchTo;
        float t0;
        bool switched;

        float nextTackle = -99f;
        float recoverUntil = -99f;

        CharacterController cc;
        Vector3 vel;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            station = transform.position;
        }

        // ----------------------------------------------------------- team orders --

        public void SetStation(Vector3 p) { station = p; }

        /// <summary>
        /// TeamDefence has worked out that this man, and only this man, can reach the
        /// ball before the intended receiver does. He goes there and stops doing
        /// anything else - a pass that is already travelling will not wait for him to
        /// finish holding his shape.
        /// </summary>
        public void SetIntercept(Vector3 p) { Intercepting = true; interceptPoint = p; }

        public void ClearIntercept() { Intercepting = false; }

        public void SetPressing(bool on, PressMode how, Vector3 carrier, bool goalAtPositiveZ, bool breaking)
        {
            pressing = on;
            mode = how;
            carrierPos = carrier;
            defendsPositiveZ = goalAtPositiveZ;
            onBreak = breaking;
        }

        // ------------------------------------------------------- drill interface --

        public void Warp(Vector3 pos)
        {
            cc.enabled = false;
            transform.position = pos;
            cc.enabled = true;
            vel = Vector3.zero;
            station = pos;
            Intercepting = false;
            nextTackle = -99f;
            recoverUntil = -99f;
        }

        public void Begin(PressStyle style, float lateralOffset, float switchDelay, PressStyle to)
        {
            Style = style;
            drillShape = true;
            lateral = lateralOffset;
            switchAfter = switchDelay;
            switchTo = to;
            switched = false;
            t0 = Time.time;
        }

        public float Gap(Vector3 p)
        {
            Vector3 d = transform.position - p;
            d.y = 0f;
            return d.magnitude;
        }

        public static float DesiredGap(PressStyle s)
        {
            switch (s)
            {
                case PressStyle.Tight: return 0.72f;
                case PressStyle.Loose: return 2.9f;
                default: return 6.4f;
            }
        }

        // ------------------------------------------------------------ the moment --

        /// <summary>
        /// Is the man on the ball trying to spin off him right now? Reads his torso, not
        /// the ball: the ball can be anywhere, it is the body coming round that says he
        /// has committed to turning.
        /// </summary>
        public bool CarrierIsTurningIn()
        {
            if (target == null) return false;
            var fc = target.GetComponent<FootballerController>();
            if (fc == null) return false;

            Vector3 goal = TacticalPitch.GoalCentre(defendsPositiveZ);
            Vector3 d = goal - target.position;
            Vector2 toGoal = new Vector2(d.x, d.z);
            if (toGoal.sqrMagnitude < 1e-4f) return false;

            float ang = Vector2.Angle(fc.BodyForward, toGoal.normalized);
            return ang < turnedInAngle;
        }

        /// <summary>Does he go in this frame?</summary>
        public bool WantsTackle(Vector3 ballPos, float exposure)
        {
            if (Time.time < nextTackle || Recovering) return false;

            // Delaying and tackling are opposites. A man told to stand off who then dives
            // in has thrown away the only thing delay buys - the seconds the rest of the
            // shape needs to get back.
            if (pressing && mode == PressMode.Contain) return false;

            Vector3 d = ballPos - transform.position;
            d.y = 0f;
            float gap = d.magnitude;
            if (gap > lungeRange) return false;

            // The one trigger that matters: tight, and he is coming round anyway.
            if (gap <= tightRange && CarrierIsTurningIn()) return true;

            // A touch that has run away from him is free money either way.
            if (exposure > exposureTrigger) return true;

            // Otherwise stay on your feet. This is near enough never.
            return Random.value < gambleChance * Time.deltaTime;
        }

        public void BeganTackle() { nextTackle = Time.time + tackleCooldown; }
        public void MissedTackle() { recoverUntil = Time.time + missRecovery; }

        // ------------------------------------------------------------ where to be --

        /// <summary>
        /// Goal-side of the carrier and shaded to his inside shoulder. The gap he is left
        /// with points at the touchline, which is the whole idea: out there he has one
        /// less passing angle, no shot, and a line to put him over.
        /// </summary>
        public Vector3 DenyTurnStance(Vector3 carrier)
        {
            Vector3 goal = TacticalPitch.GoalCentre(defendsPositiveZ);
            Vector3 toGoal = goal - carrier;
            toGoal.y = 0f;
            if (toGoal.sqrMagnitude < 1e-4f) toGoal = Vector3.forward;
            toGoal.Normalize();

            Vector3 inside = TacticalPitch.InsideDir(carrier);
            float shade = onBreak ? breakShade : shoulderShade;

            // Only shade when he is central. Out by the touchline the pitch is already
            // doing the job and cheating inside just opens the line back in.
            if (!TacticalPitch.IsCentral(TacticalPitch.LaneOf(carrier.x))) shade *= 0.35f;

            return carrier + toGoal * denyGap + inside * shade;
        }

        // ------------------------------------------------------------------ loop --

        void Update()
        {
            if (!active) return;

            float dt = Time.deltaTime;
            float yaw = transform.eulerAngles.y;

            if (Recovering)
            {
                // On the floor. He cannot chase for a moment.
                vel = Vector3.MoveTowards(vel, Vector3.zero, 40f * dt);
                cc.Move((vel + Vector3.down * 3f) * dt);
                return;
            }

            Vector3 want;
            Vector3 look;
            float top = speed;

            if (Intercepting)
            {
                want = interceptPoint;
                look = interceptPoint - transform.position;
                top = interceptSpeed;
            }
            else if (chase != null)
            {
                // The drill has handed him the carrier. Do not run at the ball - take up
                // the stance that stops the turn and hold it.
                Vector3 carrier = target != null ? target.position : chase.position;
                want = DenyTurnStance(carrier);
                look = carrier - transform.position;
            }
            else if (pressing && mode == PressMode.Contain)
            {
                // Delaying a break. TeamDefence worked out where to stand - goal-side,
                // shading the middle, on the lane behind him - and the job here is to
                // hold it and stay square to him. Closing the last three metres is the
                // one thing that must not happen: that is the touch he is waiting for.
                want = station;
                look = carrierPos - transform.position;
            }
            else if (pressing)
            {
                want = DenyTurnStance(carrierPos);
                look = carrierPos - transform.position;
            }
            else if (drillShape && target != null)
            {
                // Legacy drill shape: hold a gap behind the receiver until the ball comes.
                if (!switched && switchAfter > 0f && Time.time - t0 > switchAfter)
                { Style = switchTo; switched = true; }
                want = target.position + new Vector3(lateral, 0f, DesiredGap(Style));
                look = target.position - transform.position;
            }
            else
            {
                want = station;
                look = markTarget.HasValue
                    ? markTarget.Value - transform.position
                    : station - transform.position;
            }

            vel = PlayerMotion.Step(vel, transform.position, want, top, yaw, motion, dt);
            cc.Move((vel + Vector3.down * 3f) * dt);

            // He watches the man, not his own feet - so the body angle is solved from
            // where he is LOOKING, and running backwards is priced in by PlayerMotion.
            transform.rotation = Quaternion.Euler(0f, PlayerMotion.Turn(yaw, look, motion, dt), 0f);
        }
    }
}
