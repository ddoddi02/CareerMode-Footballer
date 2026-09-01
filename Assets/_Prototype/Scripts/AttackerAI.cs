using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// One player without the ball. Like <see cref="DefenderAI"/> he does not decide
    /// where to be - <see cref="TeamAttack"/> hands him a station every time the side
    /// takes a new picture - but he owns the timers that make his movement his own:
    /// when he next goes in behind, and which way he is currently wriggling to get free.
    ///
    /// Those timers live here rather than in the coordinator so that eleven players do
    /// not all make their runs on the same frame, which is what a single shared clock
    /// would produce and which looks nothing like football.
    ///
    /// He can also have the ball. When he does he stops looking for a station and stands
    /// over it, and TeamAttack decides where it goes next - the same split as everywhere
    /// else here: the coordinator chooses, the player executes.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class AttackerAI : MonoBehaviour, IBallCarrier
    {
        [Header("Who he is")]
        public Role role = Role.ST;
        [Tooltip("His slot in the formation. He looks for space around this, not anywhere.")]
        public Vector3 homeSlot;

        [Header("Movement")]
        public float speed = 6.0f;
        [Tooltip("Used while running in behind - a run is only a run if it is quick.")]
        public float sprintSpeed = 7.9f;
        public bool active = true;

        [Header("4 - runs in behind")]
        [Tooltip("Seconds between runs, randomised per player so they do not all go at once.")]
        public float runIntervalMin = 5f;
        public float runIntervalMax = 11f;
        public float runDuration = 1.8f;

        [Header("3 - unsticking")]
        [Tooltip("How long he commits to one direction before mixing it up.")]
        public float unstickPeriod = 1.4f;

        [Header("On the ball")]
        [Tooltip("How well he passes, 0..1. Drives his own error (PROJECT.md 3.8) and makes him worth passing TO - the selector prefers giving it to a better passer than the man on it.")]
        [Range(0f, 1f)] public float passing = 0.65f;
        [Tooltip("How long after playing a pass he will not collect the ball again. Without it he takes his own pass back off the receiver.")]
        public float passCooldown = 0.8f;

        /// <summary>Where TeamAttack wants him.</summary>
        public Vector3 Station { get { return station; } }

        /// <summary>He is attacking the line right now, so he moves at pace.</summary>
        public bool Running { get { return Time.time < runUntil; } }

        /// <summary>He has been asked to come and help an outnumbered team-mate.</summary>
        public bool Showing { get; private set; }

        /// <summary>True while the lane to him is congested and he is working to get free.</summary>
        public bool Unsticking { get; private set; }

        /// <summary>He is on the ball. He stands over it and waits to be told where it goes.</summary>
        public bool HasBall { get; private set; }

        /// <summary>He has just played it and is not allowed to collect it back.</summary>
        public bool CanCollect { get { return !HasBall && Time.time >= collectAt; } }

        /// <summary>He has been sent to go and get a loose ball. Beats his station.</summary>
        public bool Chasing { get; private set; }

        public Vector3 Velocity { get { return vel; } }

        // --- IBallCarrier -------------------------------------------------------
        public Transform CarrierTransform { get { return transform; } }
        public Vector2 CarrierForward
        {
            get
            {
                float a = transform.eulerAngles.y * Mathf.Deg2Rad;
                return new Vector2(Mathf.Sin(a), Mathf.Cos(a));
            }
        }
        public float CarrierSpeed { get { return new Vector2(vel.x, vel.z).magnitude; } }
        public float CarrierTopSpeed { get { return sprintSpeed; } }

        // --- timers owned by the player, read and armed by TeamAttack ----------
        [HideInInspector] public float nextRunAt = -1f;
        [HideInInspector] public float runUntil = -99f;
        [HideInInspector] public float unstickFlipAt = -1f;
        [HideInInspector] public int unstickAxis;      // 0 = across the pitch, 1 = up and down it
        [HideInInspector] public int unstickSign = 1;

        Vector3 station;
        Transform lookAt;
        CharacterController cc;
        Vector3 vel;
        float collectAt = -99f;
        Vector3 faceTarget;
        bool facing;
        Vector3 chasePoint;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            station = transform.position;
            // Stagger the first run so the front three do not set off together.
            nextRunAt = Time.time + Random.Range(runIntervalMin, runIntervalMax);
        }

        /// <summary>Put him back on his slot for a restart.</summary>
        public void Warp(Vector3 pos)
        {
            if (cc == null) cc = GetComponent<CharacterController>();
            cc.enabled = false;
            transform.position = pos;
            cc.enabled = true;
            vel = Vector3.zero;
            station = pos;
            HasBall = false;
            facing = false;
            Chasing = false;
            collectAt = -99f;
            runUntil = -99f;
            nextRunAt = Time.time + Random.Range(runIntervalMin, runIntervalMax);
        }

        // -------------------------------------------------------------- the ball --

        /// <summary>He has the ball at his feet.</summary>
        public void TakeBall() { HasBall = true; facing = false; }

        /// <summary>He has played it. The cooldown stops him collecting his own pass.</summary>
        public void ReleaseBall()
        {
            HasBall = false;
            facing = false;
            collectAt = Time.time + passCooldown;
        }

        /// <summary>Turn to look at where the ball is about to go, so the pass is not struck out of his back.</summary>
        public void FaceTarget(Vector3 p) { faceTarget = p; facing = true; }

        /// <summary>
        /// Go and get it. TeamAttack sends exactly one man, at the point the ball is
        /// going to be rather than the point it is at - chasing the ball's current
        /// position is how you follow it around the pitch without ever arriving.
        /// </summary>
        public void SetChase(Vector3 p) { Chasing = true; chasePoint = p; }

        public void ClearChase() { Chasing = false; }

        public void SetStation(Vector3 p, bool showing, bool unsticking, Transform watch)
        {
            station = p;
            Showing = showing;
            Unsticking = unsticking;
            lookAt = watch;
        }

        /// <summary>Kick off a run in behind. TeamAttack calls this when his timer is up.</summary>
        public void BeginRun()
        {
            runUntil = Time.time + runDuration;
            nextRunAt = Time.time + runDuration + Random.Range(runIntervalMin, runIntervalMax);
        }

        /// <summary>
        /// Principle 3. Every period he changes which way he is trying to get free -
        /// across, then up and down, then across again. Mixing the two is what actually
        /// breaks a marker; repeating one of them just walks him into the same defender.
        /// </summary>
        public void TickUnstick(bool squeezed)
        {
            if (!squeezed) { unstickFlipAt = -1f; return; }

            if (Time.time >= unstickFlipAt)
            {
                unstickAxis = 1 - unstickAxis;
                unstickSign = Random.value < 0.5f ? -1 : 1;
                unstickFlipAt = Time.time + unstickPeriod;
            }
        }

        /// <summary>The direction he is currently trying to get free in.</summary>
        public Vector3 UnstickDir()
        {
            return unstickAxis == 0
                ? new Vector3(unstickSign, 0f, 0f)
                : new Vector3(0f, 0f, unstickSign);
        }

        void Update()
        {
            if (!active) return;

            if (HasBall)
            {
                // On the ball he does not go looking for space - he stands it up and
                // waits for TeamAttack to tell him where it goes.
                vel = Vector3.MoveTowards(vel, Vector3.zero, 26f * Time.deltaTime);
                cc.Move((vel + Vector3.down * 3f) * Time.deltaTime);

                Vector3 f = facing ? faceTarget - transform.position
                                   : (lookAt != null ? lookAt.position - transform.position : Vector3.zero);
                f.y = 0f;
                if (f.sqrMagnitude > 0.04f)
                    transform.rotation = Quaternion.RotateTowards(
                        transform.rotation, Quaternion.LookRotation(f), 480f * Time.deltaTime);
                return;
            }

            Vector3 dv = (Chasing ? chasePoint : station) - transform.position;
            dv.y = 0f;

            float top = Running || Chasing ? sprintSpeed : speed;
            Vector3 want = Vector3.ClampMagnitude(dv * 3.0f, top);
            vel = Vector3.MoveTowards(vel, want, 30f * Time.deltaTime);
            cc.Move((vel + Vector3.down * 3f) * Time.deltaTime);

            // Head up, watching the ball - except on a run, when he is looking where he
            // is going.
            Vector3 look = (Running && !Chasing) || lookAt == null
                ? new Vector3(vel.x, 0f, vel.z)
                : lookAt.position - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.04f) transform.rotation = Quaternion.LookRotation(look);
        }
    }
}
