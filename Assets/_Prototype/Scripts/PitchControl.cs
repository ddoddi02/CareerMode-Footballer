using System.Collections.Generic;
using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// Who would get there first, for every point on the pitch.
    ///
    /// This is the thing the AI was missing. Everything before it asked questions about
    /// BODIES - how far is the nearest defender, how many are in this cell - and a body
    /// count cannot tell the difference between a man standing still and the same man
    /// sprinting away from the space. So the side kept playing balls into rooms that were
    /// about to be full, and kept ignoring rooms that were about to be empty.
    ///
    /// The question here is about TIME instead: for each square of the pitch, who
    /// arrives first, us or them? A man running toward a square owns it from further
    /// away than a man standing on it, which is exactly how space works in football and
    /// is not expressible as a distance.
    ///
    /// One grid answers three separate questions that used to have three separate bits
    /// of code:
    ///   - where should an off-ball player stand?      -> the square we control best
    ///   - is this pass into space or into trouble?    -> control at the target
    ///   - where do we hit it when everything is shut? -> the emptiest region
    ///
    /// Deliberately coarse and deliberately cheap. It is rebuilt when the side takes a
    /// new picture, not every frame, so it is as stale as everything else that side
    /// believes (PROJECT.md §3.15) - and each side builds its own from its own snapshot,
    /// so neither is reading the other's mind.
    /// </summary>
    [System.Serializable]
    public class PitchControl
    {
        [Tooltip("Square size in metres. 2 m is 53 x 34 squares - fine enough to see a passing lane, coarse enough to be free.")]
        public float cellSize = 2f;

        [Tooltip("Seconds of his current run that carry him on. This is what lets a moving man own space in front of him: he is credited from where his momentum is taking him, not from where he stands.")]
        public float momentum = 0.45f;

        [Tooltip("Seconds before he can react at all. Everyone gets the same head start, so it never decides anything on its own - it just stops a man owning the square he is standing on by an absurd margin.")]
        public float reaction = 0.2f;

        [Tooltip("Time difference, in seconds, that counts as owning a square outright. Smaller makes the map hard-edged; larger blurs it into mush.")]
        public float decisiveness = 0.9f;

        /// <summary>Squares across (x) and along (z) the pitch.</summary>
        public int Nx { get; private set; }
        public int Nz { get; private set; }
        public float BuiltAt { get; private set; }

        float[] control = new float[0];      // +1 = ours outright, -1 = theirs outright
        float halfW, halfL;

        // ------------------------------------------------------------- building --

        /// <summary>
        /// Rebuild from one side's point of view. `ours` should be live positions - a
        /// side always knows where its own players are - and `theirs` whatever that side
        /// currently believes, projected forward.
        /// </summary>
        public void Rebuild(IList<Vector3> ours, IList<Vector3> oursVel,
                            IList<Vector3> theirs, IList<Vector3> theirsVel,
                            float topSpeed)
        {
            halfW = TacticalPitch.HalfW;
            halfL = TacticalPitch.HalfL;

            int nx = Mathf.Max(4, Mathf.CeilToInt(halfW * 2f / Mathf.Max(cellSize, 0.5f)));
            int nz = Mathf.Max(4, Mathf.CeilToInt(halfL * 2f / Mathf.Max(cellSize, 0.5f)));

            if (nx != Nx || nz != Nz || control.Length != nx * nz)
            {
                Nx = nx; Nz = nz;
                control = new float[nx * nz];
            }

            float v = Mathf.Max(topSpeed, 0.1f);

            for (int iz = 0; iz < nz; iz++)
            {
                for (int ix = 0; ix < nx; ix++)
                {
                    Vector3 p = CentreOf(ix, iz);
                    float tOurs = BestTime(p, ours, oursVel, v);
                    float tTheirs = BestTime(p, theirs, theirsVel, v);

                    // Positive when we get there first. Squashed so a half-second edge
                    // reads as an edge rather than as ownership.
                    float lead = (tTheirs - tOurs) / Mathf.Max(decisiveness, 0.05f);
                    control[iz * nx + ix] = lead / (1f + Mathf.Abs(lead));
                }
            }

            BuiltAt = Time.time;
        }

        /// <summary>How soon the quickest of them could be standing on this point.</summary>
        float BestTime(Vector3 p, IList<Vector3> who, IList<Vector3> vel, float topSpeed)
        {
            float best = float.MaxValue;
            for (int i = 0; who != null && i < who.Count; i++)
            {
                // Where his run is taking him. A man already moving this way is closer
                // than his feet are.
                Vector3 from = who[i];
                if (vel != null && i < vel.Count) from += vel[i] * momentum;

                float dx = p.x - from.x;
                float dz = p.z - from.z;
                float d = Mathf.Sqrt(dx * dx + dz * dz);

                float t = reaction + d / topSpeed;
                if (t < best) best = t;
            }
            return best == float.MaxValue ? 99f : best;
        }

        // -------------------------------------------------------------- reading --

        /// <summary>-1 (theirs) .. +1 (ours) at a point.</summary>
        public float At(Vector3 p)
        {
            if (control.Length == 0) return 0f;
            int ix = Mathf.Clamp(Mathf.FloorToInt((p.x + halfW) / cellSize), 0, Nx - 1);
            int iz = Mathf.Clamp(Mathf.FloorToInt((p.z + halfL) / cellSize), 0, Nz - 1);
            return control[iz * Nx + ix];
        }

        /// <summary>0 (theirs) .. 1 (ours), for scoring terms that want a plain fraction.</summary>
        public float OursAt(Vector3 p) { return At(p) * 0.5f + 0.5f; }

        public float Raw(int ix, int iz)
        {
            if (control.Length == 0) return 0f;
            return control[Mathf.Clamp(iz, 0, Nz - 1) * Nx + Mathf.Clamp(ix, 0, Nx - 1)];
        }

        public bool Ready { get { return control.Length > 0; } }

        public Vector3 CentreOf(int ix, int iz)
        {
            return new Vector3((ix + 0.5f) * cellSize - halfW, 0f, (iz + 0.5f) * cellSize - halfL);
        }

        /// <summary>
        /// The best square we own inside a region - what a long ball is aimed at when
        /// every lane on the floor is shut.
        /// </summary>
        public Vector3 BestSpaceNear(Vector3 around, float radius, bool attacksPositiveZ)
        {
            if (control.Length == 0) return around;

            float dir = attacksPositiveZ ? 1f : -1f;
            Vector3 best = around;
            float bestScore = float.NegativeInfinity;

            int r = Mathf.Max(1, Mathf.CeilToInt(radius / cellSize));
            int cx = Mathf.Clamp(Mathf.FloorToInt((around.x + halfW) / cellSize), 0, Nx - 1);
            int cz = Mathf.Clamp(Mathf.FloorToInt((around.z + halfL) / cellSize), 0, Nz - 1);

            for (int iz = Mathf.Max(0, cz - r); iz <= Mathf.Min(Nz - 1, cz + r); iz++)
            {
                for (int ix = Mathf.Max(0, cx - r); ix <= Mathf.Min(Nx - 1, cx + r); ix++)
                {
                    Vector3 p = CentreOf(ix, iz);
                    if ((p - around).sqrMagnitude > radius * radius) continue;

                    // Space we own, with a nudge toward the goal so it does not settle
                    // for the emptiest square being the one behind us.
                    float s = control[iz * Nx + ix] + 0.015f * p.z * dir;
                    if (s > bestScore) { bestScore = s; best = p; }
                }
            }
            return best;
        }

        public void Clear()
        {
            control = new float[0];
            Nx = Nz = 0;
            BuiltAt = -99f;
        }
    }
}
