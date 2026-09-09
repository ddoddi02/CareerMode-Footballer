using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// What a body can and cannot do, as numbers. One of these per bot.
    /// </summary>
    [System.Serializable]
    public class MotionModel
    {
        [Tooltip("Getting up to speed, m/s^2. About a second from a standstill to a sprint - quick for a human, slow enough that a bot cannot appear at full pace.")]
        public float accel = 10f;
        [Tooltip("Slowing down, m/s^2. Higher than acceleration, because stopping is easier than starting - but nothing like instant.")]
        public float brake = 16f;

        [Tooltip("How hard he can push SIDEWAYS while already running, m/s^2. This one number IS the turning circle: radius = speed^2 / this. 12 gives about 5.2 m at a 7.9 m/s sprint and 3.0 m at a 6 m/s run, which is roughly what a real player needs. Raise it and bodies start pivoting like turrets.")]
        public float turnAccel = 12f;

        [Tooltip("He starts easing off this far from where he is going, instead of arriving at full pace and jittering on the spot.")]
        public float arriveRadius = 3f;
        [Tooltip("Close enough. Stop.")]
        public float stopRadius = 0.35f;

        [Tooltip("How fast the body comes round, degrees per second.")]
        public float bodyTurn = 480f;
        [Tooltip("Top speed when running more or less backwards, as a fraction. Nobody backpedals at sprint pace.")]
        [Range(0.2f, 1f)] public float backpedalSpeed = 0.55f;
        [Tooltip("Angle off his facing beyond which he counts as going backwards.")]
        public float backpedalAngle = 110f;
    }

    /// <summary>
    /// How a body gets from where it is to where it was told to be.
    ///
    /// The old version was one line - point at the target, accelerate at a fixed rate,
    /// clamp - and it moved like a unit in a strategy game: instant direction changes at
    /// full pace, arriving at top speed and vibrating on the spot, running backwards as
    /// fast as forwards. None of that is a decision problem. The AI can be right about
    /// where to go and still look wrong getting there.
    ///
    /// The fix is one idea: SPLIT THE CHANGE IN VELOCITY INTO ALONG AND ACROSS.
    ///
    /// Speeding up or slowing down along the way you are already running is cheap.
    /// Pushing sideways is not, and limiting it is what produces a turning circle
    /// without anyone having to compute an arc - a man at 7.9 m/s with 12 m/s^2 to
    /// spend sideways simply cannot come round inside about five metres. He has to run
    /// the corner.
    ///
    /// Everything else here is a consequence of that same honesty about bodies:
    /// braking beats acceleration, arriving means easing off first, and going backwards
    /// is slow.
    /// </summary>
    public static class PlayerMotion
    {
        /// <summary>
        /// One step. Returns the new velocity; `facingDeg` is turned toward where he is
        /// actually going, at his own rate.
        /// </summary>
        public static Vector3 Step(Vector3 vel, Vector3 pos, Vector3 target, float topSpeed,
                                   float facingDeg, MotionModel m, float dt)
        {
            vel.y = 0f;

            Vector3 to = target - pos;
            to.y = 0f;
            float dist = to.magnitude;

            // --- how fast does he want to be going, right now ----------------------
            float want = topSpeed;
            if (dist <= m.stopRadius) want = 0f;
            else if (dist < m.arriveRadius) want = topSpeed * (dist / m.arriveRadius);

            Vector3 dir = dist > 1e-4f ? to / dist : Vector3.zero;

            // Running at something behind you is not running, it is backpedalling.
            if (dir != Vector3.zero)
            {
                Vector2 face = Facing(facingDeg);
                float off = Vector2.Angle(face, new Vector2(dir.x, dir.z));
                if (off > m.backpedalAngle) want *= m.backpedalSpeed;
            }

            Vector3 desired = dir * want;
            Vector3 dv = desired - vel;

            // --- and what he is allowed to do about it ------------------------------
            float speed = vel.magnitude;
            if (speed < 0.2f)
            {
                // Standing still: nothing to turn, just push off.
                vel = Vector3.MoveTowards(vel, desired, m.accel * dt);
            }
            else
            {
                Vector3 fwd = vel / speed;

                float along = Vector3.Dot(dv, fwd);
                Vector3 across = dv - fwd * along;

                float alongLimit = (along >= 0f ? m.accel : m.brake) * dt;
                along = Mathf.Clamp(along, -alongLimit, alongLimit);
                across = Vector3.ClampMagnitude(across, m.turnAccel * dt);

                vel += fwd * along + across;
            }

            return Vector3.ClampMagnitude(vel, topSpeed);
        }

        /// <summary>
        /// Turn the body toward `lookAt`, no faster than he can. Returns the new angle.
        /// Kept separate from Step because where a man is looking and where he is running
        /// are not the same thing - a defender retreating watches the ball.
        /// </summary>
        public static float Turn(float facingDeg, Vector3 lookDir, MotionModel m, float dt)
        {
            lookDir.y = 0f;
            if (lookDir.sqrMagnitude < 0.04f) return facingDeg;

            float want = Mathf.Atan2(lookDir.x, lookDir.z) * Mathf.Rad2Deg;
            return Mathf.MoveTowardsAngle(facingDeg, want, m.bodyTurn * dt);
        }

        public static Vector2 Facing(float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r), Mathf.Cos(r));
        }

        /// <summary>The circle he cannot turn inside of, at this speed. Handy for the debug view.</summary>
        public static float TurnRadius(float speed, MotionModel m)
        {
            return speed * speed / Mathf.Max(m.turnAccel, 0.01f);
        }
    }
}
