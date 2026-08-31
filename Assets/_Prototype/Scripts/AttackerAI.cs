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
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class AttackerAI : MonoBehaviour
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

        /// <summary>Where TeamAttack wants him.</summary>
        public Vector3 Station { get { return station; } }

        /// <summary>He is attacking the line right now, so he moves at pace.</summary>
        public bool Running { get { return Time.time < runUntil; } }

        /// <summary>He has been asked to come and help an outnumbered team-mate.</summary>
        public bool Showing { get; private set; }

        /// <summary>True while the lane to him is congested and he is working to get free.</summary>
        public bool Unsticking { get; private set; }

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

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            station = transform.position;
            // Stagger the first run so the front three do not set off together.
            nextRunAt = Time.time + Random.Range(runIntervalMin, runIntervalMax);
        }

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

            Vector3 dv = station - transform.position;
            dv.y = 0f;

            float top = Running ? sprintSpeed : speed;
            Vector3 want = Vector3.ClampMagnitude(dv * 3.0f, top);
            vel = Vector3.MoveTowards(vel, want, 30f * Time.deltaTime);
            cc.Move((vel + Vector3.down * 3f) * Time.deltaTime);

            // Head up, watching the ball - except on a run, when he is looking where he
            // is going.
            Vector3 look = Running || lookAt == null
                ? new Vector3(vel.x, 0f, vel.z)
                : lookAt.position - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.04f) transform.rotation = Quaternion.LookRotation(look);
        }
    }
}
