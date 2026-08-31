using UnityEngine;

namespace Prototype
{
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

        [Header("Carry")]
        [Tooltip("How far in front of him the ball sits at a standstill.")]
        public float touchNear = 0.40f;
        [Tooltip("How far in front at full sprint. Well past a defender's reach - that is the point.")]
        public float touchFar = 1.45f;
        [Tooltip("Above 1 the ball stays tucked in until he really opens up. Linear would make ordinary running as exposed as sprinting.")]
        public float touchCurve = 2.5f;
        [Tooltip("How sharply the ball catches up to the touch point.")]
        public float carryLerp = 11f;

        /// <summary>Still rolling with meaningful pace.</summary>
        public bool InFlight { get; private set; }

        /// <summary>Glued to a carrier - he is dribbling.</summary>
        public bool Carried { get; private set; }

        public FootballerController Carrier { get; private set; }

        public Vector3 Velocity { get { return vel; } }
        public float SpeedNow { get { return new Vector2(vel.x, vel.z).magnitude; } }

        /// <summary>Horizontal gap from the carrier. This is what a defender is aiming at.</summary>
        public float Exposure
        {
            get
            {
                if (Carrier == null) return 0f;
                Vector3 d = transform.position - Carrier.transform.position;
                d.y = 0f;
                return d.magnitude;
            }
        }

        Vector3 vel;

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
        public Vector3 TouchPoint(FootballerController c)
        {
            float t = Mathf.InverseLerp(0f, Mathf.Max(c.sprintSpeed, 0.1f), c.Speed);
            float ahead = Mathf.Lerp(touchNear, touchFar, Mathf.Pow(t, touchCurve));
            Vector2 f = c.BodyForward;
            Vector3 p = c.transform.position + new Vector3(f.x, 0f, f.y) * ahead;
            p.y = radius;
            return p;
        }


        /// 나중에 자연스러운 모션으로 수정할지 남겨둘지 고민할 부분
        public void Attach(FootballerController c)
        {
            Carrier = c;
            Carried = true;
            InFlight = false;
            vel = Vector3.zero;
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
            InFlight = true;
        }

        public void Hold(Vector3 pos)
        {
            Carried = false;
            Carrier = null;
            InFlight = false;
            vel = Vector3.zero;
            transform.position = new Vector3(pos.x, radius, pos.z);
        }

        public void Stop()
        {
            InFlight = false;
            vel = Vector3.zero;
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

        public float StopDistance(float v0)
        {
            return v0 * v0 / (2f * Mathf.Max(rollDecel, 0.01f));
        }
    }
}
