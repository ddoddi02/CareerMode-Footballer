using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// What a side currently BELIEVES the picture is, which is not the same as what it
    /// is. Both TeamDefence and TeamAttack run off one of these.
    ///
    /// Bots that read live transforms every frame defend like a machine: the block slides
    /// continuously and never once gets caught out. Real defenders take a picture every
    /// second or so, over the shoulder, and act on that picture until the next one. So
    /// the shape here moves in steps, and a striker who moves right after a refresh is
    /// unmarked until the next one. That lag IS the defending.
    ///
    /// Under duress the refresh gets SLOWER, not faster. A defender in a duel is looking
    /// at the ball, and the rest of the pitch goes stale on him - which is exactly how
    /// a side gets pulled apart while everyone watches the man in possession. The one
    /// thing that stays live is whatever he is directly engaged with.
    /// </summary>
    [System.Serializable]
    public class PitchIntel
    {
        [Tooltip("Seconds between pictures when nothing urgent is happening.")]
        public float calmInterval = 1.0f;
        [Tooltip("Seconds between pictures once the ball is close. Longer on purpose - he is watching the ball, not the pitch.")]
        public float urgentInterval = 1.8f;
        [Tooltip("Distance from the ball inside which the situation counts as urgent.")]
        public float urgentRange = 12f;

        /// <summary>Last picture of where the opposition were.</summary>
        public Vector3[] Opponents { get { return oppSnap; } }

        /// <summary>How each of them was moving when the picture was taken.</summary>
        public Vector3[] OpponentVel { get { return oppVel; } }

        /// <summary>Last picture of the ball. Not live unless someone is engaged with it.</summary>
        public Vector3 Ball { get { return ballSnap; } }

        public float TakenAt { get { return takenAt; } }
        public bool Urgent { get { return urgent; } }
        public float Age(float now) { return now - takenAt; }

        Vector3[] oppSnap = new Vector3[0];
        Vector3[] oppVel = new Vector3[0];
        Vector3[] oppPrev = new Vector3[0];
        Vector3 ballSnap;
        float takenAt = -99f;
        float nextAt = -99f;
        bool urgent;

        /// <summary>
        /// Takes a fresh picture when the interval is up. Returns true on the frames it
        /// actually refreshed, so callers can recompute assignments only then.
        /// </summary>
        public bool Tick(float now, Transform[] opponents, Vector3 ballPos, Vector3 blockCentre)
        {
            Vector3 d = ballPos - blockCentre;
            d.y = 0f;
            urgent = d.sqrMagnitude < urgentRange * urgentRange;

            if (now < nextAt) return false;

            int n = opponents != null ? opponents.Length : 0;
            if (oppSnap.Length != n)
            {
                oppSnap = new Vector3[n];
                oppVel = new Vector3[n];
                oppPrev = new Vector3[n];
                for (int i = 0; i < n; i++)
                    if (opponents[i] != null) oppPrev[i] = opponents[i].position;
            }

            float dt = Mathf.Max(now - takenAt, 0.0001f);
            for (int i = 0; i < n; i++)
            {
                if (opponents[i] == null) continue;
                Vector3 p = opponents[i].position;
                // Velocity from the last picture, not from last frame - the defence only
                // knows a man is running because he is further along than he was.
                oppVel[i] = takenAt > 0f ? (p - oppPrev[i]) / dt : Vector3.zero;
                oppPrev[i] = p;
                oppSnap[i] = p;
            }

            ballSnap = ballPos;
            takenAt = now;
            nextAt = now + (urgent ? urgentInterval : calmInterval);
            return true;
        }

        /// <summary>Forget everything - a restart, a new round.</summary>
        public void Clear()
        {
            takenAt = -99f;
            nextAt = -99f;
            oppSnap = new Vector3[0];
            oppVel = new Vector3[0];
            oppPrev = new Vector3[0];
        }

        /// <summary>
        /// Where a man probably is now, given where he was and how he was moving. Capped,
        /// because extrapolating a full second of running is a guess, not knowledge.
        /// </summary>
        public Vector3 Projected(int i, float now)
        {
            if (i < 0 || i >= oppSnap.Length) return Vector3.zero;
            float age = Mathf.Min(now - takenAt, 0.6f);
            return oppSnap[i] + oppVel[i] * age;
        }
    }
}
