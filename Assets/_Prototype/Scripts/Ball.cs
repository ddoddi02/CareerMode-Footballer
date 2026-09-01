using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// Anything that can have the ball glued to it. The human and a bot are different
    /// classes with nothing else in common, and the ball does not care which it is
    /// carrying - it only needs to know where he is, which way he faces and how fast he
    /// is going, because those three decide how far in front of him it sits.
    /// </summary>
    public interface IBallCarrier
    {
        Transform CarrierTransform { get; }
        Vector2 CarrierForward { get; }
        float CarrierSpeed { get; }
        float CarrierTopSpeed { get; }
    }

    /// <summary>
    /// A rolling ball with real friction, plus the "glued to the dribbler" state that
    /// nearly every football game uses.
    ///
    /// Free flight:
    ///     v(t) = v0 - a*t          a = rollDecel  (m/s^2, grass)
    ///     D(t) = v0*t - a*t^2 / 2
    /// so TravelTime and SpeedToReach below give the director an exact solution to
    /// weight a pass with.
    ///
    /// Carried: the ball sits a touch in front of the carrier, and HOW FAR in front
    /// depends on how fast he is going. Walking tucks it under him at half a metre;
    /// sprinting shoves it out past a metre and a half, where a defender can reach it.
    /// That single number is the whole risk/reward of running with the ball.
    /// </summary>
    public class Ball : MonoBehaviour
    {
        [Tooltip("Ball radius in metres. Resting height, and what every position write snaps to. 0.11 is a size 5 match ball - 69 cm around.")]
        public float radius = 0.11f;

        /// <summary>Uniform transform scale for a unit-sphere mesh.</summary>
        public float Diameter { get { return radius * 2f; } }

        [Header("Rolling")]
        [Tooltip("Rolling deceleration on grass, m/s^2.")]
        public float rollDecel = 5f;
        [Tooltip("Below this speed the ball is treated as dead.")]
        public float deadSpeed = 0.35f;

        [Header("Lofted balls")]
        [Tooltip("Gravity for a ball played over the top, m/s^2. Real gravity - the flight is a closed form either way, and a heavier number just makes long balls feel like mortars.")]
        public float gravity = 9.81f;
        [Tooltip("Fraction of the ball's pace that survives the first bounce.")]
        [Range(0.2f, 1f)] public float bounceKeep = 0.62f;

        [Header("Carry")]
        [Tooltip("How far in front of him the ball sits at a standstill.")]
        public float touchNear = 0.40f;
        [Tooltip("How far in front at full sprint. Well past a defender's reach - that is the point.")]
        public float touchFar = 1.45f;
        [Tooltip("Above 1 the ball stays tucked in until he really opens up. Linear would make ordinary running as exposed as sprinting.")]
        public float touchCurve = 2.5f;
        [Tooltip("How sharply the ball catches up to the touch point.")]
        public float carryLerp = 11f;

        /// <summary>Still moving with meaningful pace - rolling or in the air.</summary>
        public bool InFlight { get; private set; }

        /// <summary>Off the ground. Nobody on the floor can intercept it up here.</summary>
        public bool Airborne { get; private set; }

        /// <summary>Where the current pass, shot or clearance was struck from.</summary>
        public Vector3 LaunchOrigin { get; private set; }

        /// <summary>
        /// How far it has run since it was struck. Used to keep whoever was standing on
        /// the passer's toes from simply owning every ball he plays: for the first metre
        /// the ball is still leaving his foot, and a body next to it has not intercepted
        /// anything.
        /// </summary>
        public float Travelled
        {
            get
            {
                Vector3 d = transform.position - LaunchOrigin;
                d.y = 0f;
                return d.magnitude;
            }
        }

        /// <summary>Glued to a carrier - he is dribbling.</summary>
        public bool Carried { get; private set; }

        public IBallCarrier Carrier { get; private set; }

        /// <summary>The body it is glued to, or null if it is loose.</summary>
        public Transform CarrierTransform
        {
            get { return Carrier != null ? Carrier.CarrierTransform : null; }
        }

        public Vector3 Velocity { get { return vel; } }
        public float SpeedNow { get { return new Vector2(vel.x, vel.z).magnitude; } }

        /// <summary>Horizontal gap from the carrier. This is what a defender is aiming at.</summary>
        public float Exposure
        {
            get
            {
                if (Carrier == null) return 0f;
                Vector3 d = transform.position - Carrier.CarrierTransform.position;
                d.y = 0f;
                return d.magnitude;
            }
        }

        Vector3 vel;
        float vy;

        // ------------------------------------------------------------- size ----

        /// <summary>
        /// The mesh is a unit sphere, so its transform scale IS the ball's diameter.
        /// Driving it from here rather than from the scene builder keeps `radius` the
        /// single source of truth: change it in the Inspector and the ball resizes on
        /// the spot, in edit mode and at runtime, without a scene rebuild.
        /// </summary>
        public void ApplyScale()
        {
            radius = Mathf.Max(radius, 0.005f);
            transform.localScale = Vector3.one * Diameter;
        }

        void Awake() { ApplyScale(); }

        void OnValidate() { ApplyScale(); }

        // ------------------------------------------------------------ carry ----

        /// <summary>Where the ball should sit right now, given how fast he is going.</summary>
        public Vector3 TouchPoint(IBallCarrier c)
        {
            float t = Mathf.InverseLerp(0f, Mathf.Max(c.CarrierTopSpeed, 0.1f), c.CarrierSpeed);
            float ahead = Mathf.Lerp(touchNear, touchFar, Mathf.Pow(t, touchCurve));
            Vector2 f = c.CarrierForward;
            Vector3 p = c.CarrierTransform.position + new Vector3(f.x, 0f, f.y) * ahead;
            p.y = radius;
            return p;
        }


        /// 나중에 자연스러운 모션으로 수정할지 남겨둘지 고민할 부분
        public void Attach(IBallCarrier c)
        {
            Carrier = c;
            Carried = true;
            InFlight = false;
            Airborne = false;
            vel = Vector3.zero;
            vy = 0f;
            transform.position = TouchPoint(c);   // no visible glide in from wherever it was
        }

        /// <summary>Knock it loose - a pass, a shot, or a defender taking it off him.</summary>
        public void Release(Vector3 dirXZ, float speed)
        {
            Carried = false;
            Carrier = null;
            Launch(transform.position, dirXZ, speed);
        }

        // ----------------------------------------------------------- flight ----

        /// <summary>Strike it along a direction. Friction decides where it ends up.</summary>
        public void Launch(Vector3 from, Vector3 dirXZ, float speed)
        {
            Carried = false;
            Carrier = null;
            transform.position = new Vector3(from.x, radius, from.z);

            Vector3 d = dirXZ;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-4f) d = Vector3.forward;
            vel = d.normalized * Mathf.Max(speed, 0.01f);
            vy = 0f;
            LaunchOrigin = transform.position;
            Airborne = false;
            InFlight = true;
        }

        /// <summary>
        /// Play it over the top. Ballistic while it is up - no rolling friction acts on a
        /// ball that is not touching the grass - then it lands where it was aimed and
        /// rolls on from there.
        ///
        /// The apex is the input rather than the launch angle because the apex is what
        /// the pass is FOR: it has to clear the heads between here and there. Hang time
        /// falls out of it (T = 2*sqrt(2h/g)) and the horizontal speed is then just
        /// distance over hang time, so the flight stays a closed form the way the rolling
        /// one is - a defender can still be asked where the ball will be in 0.8 seconds.
        /// </summary>
        public void Loft(Vector3 from, Vector3 to, float apex)
        {
            Carried = false;
            Carrier = null;
            transform.position = new Vector3(from.x, radius, from.z);

            Vector3 d = to - from;
            d.y = 0f;
            float dist = d.magnitude;
            Vector3 dir = dist > 1e-4f ? d / dist : Vector3.forward;

            apex = Mathf.Max(apex, 0.5f);
            float hang = HangTime(apex, gravity);

            vel = dir * (dist / Mathf.Max(hang, 0.05f));
            vy = Mathf.Sqrt(2f * gravity * apex);
            LaunchOrigin = transform.position;
            Airborne = true;
            InFlight = true;
        }

        public void Hold(Vector3 pos)
        {
            Carried = false;
            Carrier = null;
            InFlight = false;
            Airborne = false;
            vel = Vector3.zero;
            vy = 0f;
            transform.position = new Vector3(pos.x, radius, pos.z);
        }

        public void Stop()
        {
            InFlight = false;
            Airborne = false;
            vel = Vector3.zero;
            vy = 0f;
        }

        void Update()
        {
            float dt = Time.deltaTime;

            if (Carried && Carrier != null)
            {
                // The faster he runs, the further out the touch - and the easier he is
                // to rob.
                Vector3 target = TouchPoint(Carrier);
                transform.position = Vector3.Lerp(transform.position, target,
                                                  1f - Mathf.Exp(-carryLerp * dt));
                return;
            }

            if (!InFlight) return;

            if (Airborne)
            {
                // Nothing slows it down up here but gravity.
                vy -= gravity * dt;
                Vector3 p2 = transform.position + (vel + Vector3.up * vy) * dt;

                if (p2.y <= radius)
                {
                    p2.y = radius;
                    Airborne = false;
                    vy = 0f;
                    vel *= bounceKeep;      // it does not arrive with the pace it left at
                    if (SpeedNow <= deadSpeed) { vel = Vector3.zero; InFlight = false; }
                }

                transform.position = p2;
                return;
            }

            float s = SpeedNow - rollDecel * dt;
            if (s <= deadSpeed)
            {
                vel = Vector3.zero;
                InFlight = false;
                return;
            }

            vel = vel.normalized * s;
            transform.position += vel * dt;

            Vector3 p = transform.position;
            p.y = radius;
            transform.position = p;
        }

        // ------------------------------------------------------------- maths ---

        /// <summary>
        /// How long a ball struck at v0 takes to cover `distance` while decelerating.
        /// Solves D = v0*t - a*t^2/2 for the first root. Infinity if it dies short.
        /// </summary>
        public static float TravelTime(float distance, float v0, float decel)
        {
            if (distance <= 0f) return 0f;
            if (decel <= 0.001f) return distance / Mathf.Max(v0, 0.01f);

            float disc = v0 * v0 - 2f * decel * distance;
            if (disc <= 0f) return Mathf.Infinity;
            return (v0 - Mathf.Sqrt(disc)) / decel;
        }

        /// <summary>The strike needed to still be doing `arriveSpeed` after `distance`.</summary>
        public static float SpeedToReach(float distance, float arriveSpeed, float decel)
        {
            return Mathf.Sqrt(arriveSpeed * arriveSpeed + 2f * Mathf.Max(decel, 0f) * Mathf.Max(distance, 0f));
        }

        /// <summary>Seconds a ball reaching `apex` metres spends off the ground.</summary>
        public static float HangTime(float apex, float g)
        {
            return 2f * Mathf.Sqrt(2f * Mathf.Max(apex, 0.01f) / Mathf.Max(g, 0.01f));
        }

        /// <summary>
        /// Where the ball will be `t` seconds from now if nobody touches it - height
        /// included, because a defender cannot intercept what is over his head.
        ///
        /// This is the same closed form the passing maths uses, run forwards instead of
        /// backwards. It is what lets a defender pick the earliest point on the path he
        /// can actually get to rather than chasing the ball's current position, which is
        /// always a step behind and never catches anything.
        /// </summary>
        public Vector3 Predict(float t)
        {
            Vector3 p = transform.position;
            if (!InFlight || t <= 0f) return p;

            Vector3 flat = new Vector3(vel.x, 0f, vel.z);
            float v0 = flat.magnitude;
            Vector3 dir = v0 > 1e-4f ? flat / v0 : Vector3.forward;

            if (Airborne)
            {
                // Solve y(t) = radius for the landing, then roll on from there.
                float land = LandingTime();
                if (t <= land)
                {
                    float y = p.y + vy * t - 0.5f * gravity * t * t;
                    return p + dir * (v0 * t) + Vector3.up * (y - p.y);
                }

                Vector3 touchdown = p + dir * (v0 * land);
                touchdown.y = radius;
                return RollFrom(touchdown, dir, v0 * bounceKeep, t - land);
            }

            return RollFrom(p, dir, v0, t);
        }

        /// <summary>Seconds until an airborne ball is back on the grass.</summary>
        public float LandingTime()
        {
            if (!Airborne) return 0f;
            float h = Mathf.Max(transform.position.y - radius, 0f);
            // vy*t - g t^2/2 + h = 0  ->  the positive root.
            return (vy + Mathf.Sqrt(vy * vy + 2f * gravity * h)) / Mathf.Max(gravity, 0.01f);
        }

        Vector3 RollFrom(Vector3 p, Vector3 dir, float v0, float t)
        {
            float stop = v0 / Mathf.Max(rollDecel, 0.01f);
            float dt2 = Mathf.Min(t, stop);
            float travelled = v0 * dt2 - 0.5f * rollDecel * dt2 * dt2;
            Vector3 q = p + dir * travelled;
            q.y = radius;
            return q;
        }

        public float StopDistance(float v0)
        {
            return v0 * v0 / (2f * Mathf.Max(rollDecel, 0.01f));
        }
    }
}
