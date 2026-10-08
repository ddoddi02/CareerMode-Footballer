using UnityEngine;

namespace Prototype
{
    /// <summary>How hard a defender runs to where he has been told to stand.</summary>
    public enum DefendPace
    {
        Hold,   // getting back into shape: a jog
        Mark,   // on a man who is not on the ball
        Press   // going out to the ball: flat out
    }

    /// <summary>
    /// One defender. He does not decide who to mark or where to stand - <see cref="TeamDefence"/>
    /// hands him a station goal-side of his man every frame - but he decides how to get
    /// there and when to go in for the ball.
    ///
    /// THE TACKLE HAS ONE REAL TRIGGER: his man tries to turn while he is tight. That is
    /// the moment the attacker's body is across the ball and it is furthest from his feet.
    /// A heavy touch is the other invitation. Everything else - the random lunge - is what
    /// gives up the goal, so it is kept near zero.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class DefenderAI : MonoBehaviour
    {
        [Header("Who he is")]
        public Role role = Role.LCB;
        [Tooltip("His slot in the formation. He stands here when his side gives him nobody to mark.")]
        public Vector3 homeSlot;

        [Header("Refs")]
        [Tooltip("The human. His torso is read to time a tackle on him (CarrierIsTurningIn).")]
        public Transform target;
        [Tooltip("Marking a man who is not on the ball.")]
        public float speed = 5.8f;
        [Tooltip("Going out to the man on the ball. The one run the side makes at full pace.")]
        public float sprintSpeed = 7.9f;
        [Tooltip("Getting back into shape. A jog - it is the run he makes most often, and the one that should cost least.")]
        public float jogSpeed = 4.5f;
        [Tooltip("Faster than this counts toward SprintDistance.")]
        public float sprintLogAbove = 6.5f;
        public bool active = true;
        [Tooltip("What his body can actually do: how fast he starts, stops and turns. See PlayerMotion.")]
        public MotionModel motion = new MotionModel();

        [Header("Tackle")]
        [Tooltip("Degrees off our goal within which the carrier counts as having turned in.")]
        public float turnedInAngle = 75f;
        [Tooltip("Touch distance that reads as a heavy touch and invites the challenge.")]
        public float exposureTrigger = 0.7f;
        [Tooltip("Chance per second of going in on a tucked-in ball with no turn on. Deliberately near zero - he is not trying to win it.")]
        public float gambleChance = 0.05f;
        public float tackleCooldown = 1.1f;
        [Tooltip("How long he is out of it after missing.")]
        public float missRecovery = 1.0f;
        [Tooltip("How long a man who has just had the ball taken off him - tackled, or touched off him by the human - waits before he may tackle back. He is standing right beside the man who took it, and without this he went straight back in. Covers the new carrier's turn to play it (TeamAttack.maxTurnWait, 0.9 s).")]
        public float lostBallHold = 1.0f;
        [Range(0f, 1f)] public float tackling01 = 0.60f;

        public bool Recovering { get { return Time.time < recoverUntil; } }

        /// <summary>Set by TeamDefence: the man he is marking, if any.</summary>
        [HideInInspector] public Vector3? markTarget;

        /// <summary>Set by TeamDefence: which goal is his. Read when judging a turn.</summary>
        [HideInInspector] public bool defendsPositiveZ = true;

        /// <summary>Set by TeamDefence: how hard he runs to his station.</summary>
        [HideInInspector] public DefendPace pace = DefendPace.Mark;

        /// <summary>
        /// Metres he has covered on this brain, and how many of them above sprintLogAbove.
        /// Read by the press drill, to see who a shape makes run before there is any
        /// stamina to spend.
        /// </summary>
        public float Distance { get; private set; }
        public float SprintDistance { get; private set; }
        public void ResetRunLog() { Distance = 0f; SprintDistance = 0f; }

        /// <summary>His current orders - where TeamDefence wants him standing.</summary>
        public Vector3 Station { get { return station; } }

        /// <summary>How he is moving.</summary>
        public Vector3 Velocity { get { return vel; } }

        /// <summary>
        /// Take over the body mid-stride. This brain and the other one share a body but
        /// not a velocity, so without this a turnover would switch a sprinting man onto
        /// a brain that last moved him seconds ago - and he would stop dead and set off
        /// again, in the middle of the one moment that is meant to be sudden.
        /// </summary>
        public void CarryVelocity(Vector3 v) { vel = v; }

        Vector3 station;
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

        public void Warp(Vector3 pos)
        {
            cc.enabled = false;
            transform.position = pos;
            cc.enabled = true;
            vel = Vector3.zero;
            station = pos;
            nextTackle = -99f;
            recoverUntil = -99f;
        }

        /// <summary>How far he is from a point, on the grass.</summary>
        public float Gap(Vector3 p)
        {
            Vector3 d = transform.position - p;
            d.y = 0f;
            return d.magnitude;
        }

        // ------------------------------------------------------------ the moment --

        /// <summary>
        /// Is THIS carrier coming round to face our goal? Reads his torso, not the ball,
        /// through IBallCarrier - which the human and every bot implement - so the same
        /// trigger works whoever is on the ball.
        /// </summary>
        public bool IsTurningIn(IBallCarrier c)
        {
            if (c == null) return false;

            Vector3 goal = TacticalPitch.GoalCentre(defendsPositiveZ);
            Vector3 d = goal - c.CarrierTransform.position;
            Vector2 toGoal = new Vector2(d.x, d.z);
            if (toGoal.sqrMagnitude < 1e-4f) return false;

            float ang = Vector2.Angle(c.CarrierForward, toGoal.normalized);
            return ang < turnedInAngle;
        }

        /// <summary>Does he go in on the human this frame?</summary>
        public bool WantsTackle(Vector3 ballPos, float exposure)
        {
            return WantsTackle(ballPos, exposure,
                               target != null ? target.GetComponent<IBallCarrier>() : null);
        }

        /// <summary>
        /// Does he go in this frame, against this particular carrier?
        ///
        /// Never from further than he can reach (Tackle.Reach). He used to commit from
        /// 2.4 m, and the turn-in trigger fired from 1.9 m, against a reach of 1.25 - so
        /// every lunge in between was a guaranteed miss and a second on the floor. Behind
        /// a man with his back to goal that rarely mattered: the ball is on the far side
        /// of him until he turns. From the front it was every challenge, because a
        /// centre-back bringing it out is facing our goal and counts as turned in from the
        /// first frame - the presser lunged the moment he came within 1.9 m. Measured:
        /// 21 of 21 missed lunges from 1.87-1.90 m.
        /// </summary>
        public bool WantsTackle(Vector3 ballPos, float exposure, IBallCarrier carrier)
        {
            if (Time.time < nextTackle || Recovering) return false;

            Vector3 d = ballPos - transform.position;
            d.y = 0f;
            if (d.magnitude > Tackle.Reach) return false;

            // The one trigger that matters: tight, and he is coming round anyway.
            if (IsTurningIn(carrier)) return true;

            // A touch that has run away from him is free money either way.
            if (exposure > exposureTrigger) return true;

            // Otherwise stay on your feet. This is near enough never.
            return Random.value < gambleChance * Time.deltaTime;
        }

        public void BeganTackle() { nextTackle = Time.time + tackleCooldown; }
        public void MissedTackle() { recoverUntil = Time.time + missRecovery; }

        /// <summary>
        /// The ball has just been taken off his feet - by a tackle, or by the human's
        /// touch (Possession.TurnOver calls this). He is still within a stride of the man
        /// who took it, and nothing else stopped him going straight back in: the cooldown
        /// and the fall are the TACKLER's, and this man tackled nobody. Measured before
        /// this: at FrontWin 0.50 and above, a third of all turnovers were undone inside
        /// two seconds. Never shortens a wait he already has.
        /// </summary>
        public void LostTheBall() { nextTackle = Mathf.Max(nextTackle, Time.time + lostBallHold); }

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

            Vector3 look = markTarget.HasValue
                ? markTarget.Value - transform.position
                : station - transform.position;

            float top = pace == DefendPace.Press ? sprintSpeed : pace == DefendPace.Hold ? jogSpeed : speed;
            vel = PlayerMotion.Step(vel, transform.position, station, top, yaw, motion, dt);
            cc.Move((vel + Vector3.down * 3f) * dt);

            float step = new Vector2(vel.x, vel.z).magnitude * dt;
            Distance += step;
            if (step > sprintLogAbove * dt) SprintDistance += step;

            // He watches the man, not his own feet - so the body angle is solved from
            // where he is LOOKING, and running backwards is priced in by PlayerMotion.
            transform.rotation = Quaternion.Euler(0f, PlayerMotion.Turn(yaw, look, motion, dt), 0f);
        }
    }
}
