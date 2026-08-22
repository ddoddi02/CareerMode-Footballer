using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// Hand-rolled ball flight rather than a Rigidbody, so pass travel time is exact
    /// and the drill stays deterministic and replayable.
    /// </summary>
    public class Ball : MonoBehaviour
    {
        public float radius = 0.13f;

        public bool InFlight { get; private set; }
        public float Eta { get; private set; }

        Vector3 vel;
        float g;

        /// <summary>peak &lt;= 0.2 gives a flat driven pass; higher values arc and bounce.</summary>
        public void Launch(Vector3 from, Vector3 to, float travelTime, float peak)
        {
            float T = Mathf.Max(travelTime, 0.05f);
            transform.position = from + Vector3.up * radius;

            Vector3 flat = to - from;
            flat.y = 0f;
            vel = flat / T;

            if (peak <= 0.2f)
            {
                vel.y = 0f;
                g = 0f;
            }
            else
            {
                vel.y = 4f * peak / T;
                g = 8f * peak / (T * T);
            }

            Eta = T;
            InFlight = true;
        }

        public void Hold(Vector3 pos)
        {
            InFlight = false;
            Eta = 0f;
            vel = Vector3.zero;
            transform.position = pos + Vector3.up * radius;
        }

        public void Stop()
        {
            InFlight = false;
            Eta = 0f;
            vel = Vector3.zero;
        }

        void Update()
        {
            if (!InFlight) return;
            float dt = Time.deltaTime;
            Eta = Mathf.Max(0f, Eta - dt);

            if (g > 0f) vel.y -= g * dt;
            transform.position += vel * dt;

            if (transform.position.y < radius)
            {
                Vector3 p = transform.position;
                p.y = radius;
                transform.position = p;

                if (vel.y < 0f) vel.y = -vel.y * 0.34f;
                vel.x *= 0.95f;
                vel.z *= 0.95f;
                if (Mathf.Abs(vel.y) < 0.5f) { vel.y = 0f; g = 0f; }
            }
        }
    }
}
