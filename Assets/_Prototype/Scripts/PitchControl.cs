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

        [Tooltip("Time difference, in seconds, that counts as owning a square outright. Smaller makes the map hard-edged; larger blurs it into mush. It is scaled against whatever spread the arrival model produces, so it has to be RE-MEASURED whenever that model changes - it was, when the model became an honest one, and 0.9 survived: nothing saturates (peak 0.81), mean |control| sits at 0.49-0.59 through a possession, and 3-6% of the pitch reads as genuinely contested. Raising it only blurred the map without fixing anything.")]
        public float decisiveness = 0.9f;

        /// <summary>
        /// One body the race is run by, reduced to what the race actually depends on.
        ///
        /// The stats are read from the man himself rather than assumed, and reading them
        /// off an opponent is not cheating: how quick somebody is is public - you can see
        /// it from the first minute. It is where he IS that this side only knows a second
        /// late (PROJECT.md 3.15), and that still comes from the snapshot.
        /// </summary>
        public struct Runner
        {
            public Vector3 pos;
            public Vector3 vel;
            public float top;          // how fast he can be going
            public float accel;
            public float brake;
            public float turnAccel;    // sideways push - this is his agility
        }

        static readonly MotionModel DefaultBody = new MotionModel();

        /// <summary>
        /// Build a runner for whoever this is. Position and velocity are passed in rather
        /// than read off the transform, because for the opposition they come from the
        /// snapshot, not from the truth.
        /// </summary>
        public static Runner Of(Transform body, Vector3 pos, Vector3 vel, float fallbackTop)
        {
            Runner r;
            r.pos = pos;
            r.vel = vel;
            r.top = fallbackTop;
            r.accel = DefaultBody.accel;
            r.brake = DefaultBody.brake;
            r.turnAccel = DefaultBody.turnAccel;
            if (body == null) return r;

            // Contesting a square is a sprint, so it is the sprinting speed that decides
            // it - not the pace he happens to be jogging at.
            AttackerAI a = body.GetComponent<AttackerAI>();
            if (a != null) { r.top = a.sprintSpeed; Take(a.motion, ref r); return r; }

            DefenderAI d = body.GetComponent<DefenderAI>();
            if (d != null) { r.top = d.interceptSpeed; Take(d.motion, ref r); return r; }

            // The human has no MotionModel - his body is driven by hand, not by
            // PlayerMotion - so he races on the default one.
            FootballerController f = body.GetComponent<FootballerController>();
            if (f != null) r.top = f.sprintSpeed;
            return r;
        }

        static void Take(MotionModel m, ref Runner r)
        {
            if (m == null) return;
            r.accel = m.accel;
            r.brake = m.brake;
            r.turnAccel = m.turnAccel;
        }

        /// <summary>Squares across (x) and along (z) the pitch.</summary>
        public int Nx { get; private set; }
        public int Nz { get; private set; }
        public float BuiltAt { get; private set; }

        float[] control = new float[0];      // +1 = ours outright, -1 = theirs outright
        float halfW, halfL;
        Runner[] bufOurs, bufTheirs;
        int nOurs, nTheirs;

        // ------------------------------------------------------------- building --

        /// <summary>
        /// Rebuild from one side's point of view. `ours` should be live - a side always
        /// knows where its own players are - and `theirs` whatever that side currently
        /// believes, projected forward.
        /// </summary>
        public void Rebuild(IList<Runner> ours, IList<Runner> theirs)
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

            // Copied into arrays once, because the inner loop runs tens of thousands of
            // times and indexing an IList of structs copies one every read.
            nOurs = Copy(ours, ref bufOurs);
            nTheirs = Copy(theirs, ref bufTheirs);

            for (int iz = 0; iz < nz; iz++)
            {
                for (int ix = 0; ix < nx; ix++)
                {
                    Vector3 p = CentreOf(ix, iz);
                    float tOurs = BestTime(p, bufOurs, nOurs);
                    float tTheirs = BestTime(p, bufTheirs, nTheirs);

                    // Positive when we get there first. Squashed so a half-second edge
                    // reads as an edge rather than as ownership.
                    float lead = (tTheirs - tOurs) / Mathf.Max(decisiveness, 0.05f);
                    control[iz * nx + ix] = lead / (1f + Mathf.Abs(lead));
                }
            }

            BuiltAt = Time.time;
        }

        static int Copy(IList<Runner> src, ref Runner[] dst)
        {
            int n = src == null ? 0 : src.Count;
            if (dst == null || dst.Length < n) dst = new Runner[Mathf.Max(n, 16)];
            for (int i = 0; i < n; i++) dst[i] = src[i];
            return n;
        }

        /// <summary>How soon the quickest of them could be standing on this point.</summary>
        float BestTime(Vector3 p, Runner[] who, int n)
        {
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                float t = TimeToReach(ref who[i], p.x, p.z);
                if (t < best) best = t;
            }
            return best == float.MaxValue ? 99f : best;
        }

        /// <summary>
        /// How long this body would actually take to be standing on that point.
        ///
        /// The old version was `distance / topSpeed`, which is a body that reaches full
        /// pace instantly in any direction - a turret, not a footballer. PlayerMotion
        /// (PROJECT.md 3.23) had already stopped the bodies moving like that, so the map
        /// and the men it describes disagreed: the map handed a sprinting player ground
        /// he physically could not turn back for.
        ///
        /// Same split PlayerMotion runs on, and for the same reason - ALONG the way he is
        /// already going is cheap, ACROSS it is not:
        ///
        ///   turn    kill whatever of his run is going sideways     (his agility)
        ///   brake   if it is behind him, stop first - and he loses ground doing it
        ///   run     accelerate from what is left, then cruise      (his pace)
        ///
        /// Closed form, like the ball (3.6): no stepping, so it stays cheap enough to run
        /// for every square of the pitch.
        ///
        /// This is also what finally makes the old `reaction` knob unnecessary. It was a
        /// constant added to both sides of a subtraction, so it cancelled exactly and
        /// changed nothing. The cost of not already running at the square is real now,
        /// and it differs per man AND per square, which a constant never could.
        /// </summary>
        static float TimeToReach(ref Runner r, float px, float pz)
        {
            float dx = px - r.pos.x;
            float dz = pz - r.pos.z;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            if (d < 1e-3f) return 0f;

            float ux = dx / d, uz = dz / d;
            float top = Mathf.Max(r.top, 0.1f);
            float a = Mathf.Max(r.accel, 0.1f);

            // Split his run into the part already going there and the part going across.
            float vPar = Mathf.Min(r.vel.x * ux + r.vel.z * uz, top);
            float cx = r.vel.x - vPar * ux;
            float cz = r.vel.z - vPar * uz;

            float t = Mathf.Sqrt(cx * cx + cz * cz) / Mathf.Max(r.turnAccel, 0.1f);

            if (vPar < 0f)
            {
                // It is behind him. He has to stop before he can go, and the stopping
                // itself carries him further away.
                float brake = Mathf.Max(r.brake, 0.1f);
                t += -vPar / brake;
                d += vPar * vPar / (2f * brake);
                vPar = 0f;
            }

            float dAccel = (top * top - vPar * vPar) / (2f * a);
            if (dAccel >= d) t += (-vPar + Mathf.Sqrt(vPar * vPar + 2f * a * d)) / a;
            else t += (top - vPar) / a + (d - dAccel) / top;

            return t;
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
