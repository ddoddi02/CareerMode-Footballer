using System.Collections.Generic;
using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// The circle under the cursor: how wide this ball might miss by.
    ///
    /// This is not a debug view, and it is not toggled with the rest of them. It is the
    /// aiming interface. Without it the player is being asked to judge a pass against a
    /// spread he cannot see, and the honest answer to "why did that go three metres
    /// wide" would be invisible to him.
    ///
    /// WHAT THE CIRCLE MEANS, precisely - it is easy to read it as the wrong thing:
    ///
    ///   it is NOT where the ball stops.
    ///   it is the set of LINES the ball might travel down.
    ///
    /// The strike picks one line through that circle and the ball carries on along it
    /// (BallModel.Resolve turns the spread into an angle). So a wide circle does not
    /// mean a ball that lands somewhere nearby - it means a ball that leaves at the
    /// wrong angle and keeps being wrong, further out the longer it runs.
    ///
    /// It shrinks with the passer's ability, and it grows the further out he points
    /// (PROJECT.md 3.9) - so pointing at the man rather than past him is not a nicety,
    /// it is the accuracy. Under pressure it opens up too, because pressure is what
    /// drives direction error.
    ///
    /// Every number here is asked for rather than worked out: DrillDirector owns the aim
    /// point and the spread, and it is the same call the strike itself makes. A reticle
    /// that computes its own would eventually disagree with the ball, and the player
    /// would believe the reticle (PROJECT.md 3.20).
    /// </summary>
    public class AimReticle : MonoBehaviour
    {
        [Header("Refs - found automatically if left empty")]
        public FootballerController player;
        public DrillDirector director;
        public Ball ball;

        [Header("Show it")]
        public bool show = true;
        [Tooltip("Only while he is actually on the ball. Aiming means nothing when he has not got it.")]
        public bool onlyWithBall = true;

        [Header("Look")]
        [Range(12, 64)] public int segments = 40;
        public float height = 0.07f;
        public float ringWidth = 0.08f;
        public float lineWidth = 0.045f;
        [Tooltip("Draw the line from his foot out to the aim point. It is the pass he is about to hit.")]
        public bool showAimLine = true;
        [Tooltip("A dot at the exact point he is pointing at, inside the circle of doubt.")]
        public float centreDotRadius = 0.22f;

        [Header("Colours")]
        [Tooltip("A pass he can be confident in.")]
        public Color tightCol = new Color(0.45f, 1f, 0.65f, 0.85f);
        [Tooltip("A pass that is mostly hope. Blended toward as the circle opens up.")]
        public Color looseCol = new Color(1f, 0.45f, 0.35f, 0.85f);
        [Tooltip("Spread, in metres, that counts as fully loose for the colour blend. Colour only - it changes nothing about the ball.")]
        public float looseAt = 2.5f;
        public Color lineCol = new Color(1f, 1f, 1f, 0.30f);

        readonly List<LineRenderer> pool = new List<LineRenderer>();
        Material mat;
        Transform holder;
        int used;

        void Awake()
        {
            mat = ProtoMat.UnlitFade(new Color(1f, 1f, 1f, 0.6f));
            if (player == null) player = FindAnyObjectByType<FootballerController>();
            if (director == null) director = FindAnyObjectByType<DrillDirector>();
            if (ball == null) ball = FindAnyObjectByType<Ball>();

            AdoptStrays();
        }

        /// <summary>
        /// Take back the lines from before the last domain reload. The pool is a plain
        /// field, so editing a script with the game running empties it while the child
        /// objects it was tracking survive - orphans that freeze on screen with nobody
        /// left to switch them off (PROJECT.md 3.20).
        ///
        /// They are kept under a child of our own rather than directly beneath this
        /// GameObject, and ONLY that child is scanned. PlayDebugView sits on the same
        /// GameObject and pools its own lines the same way: scanning everything below us
        /// would adopt its pool, and then the two of us would spend every frame switching
        /// each other's drawings off.
        /// </summary>
        void AdoptStrays()
        {
            pool.Clear();

            Transform t = transform.Find(HolderName);
            if (t == null)
            {
                var go = new GameObject(HolderName);
                go.transform.SetParent(transform, false);
                holder = go.transform;
                return;
            }

            holder = t;
            LineRenderer[] strays = holder.GetComponentsInChildren<LineRenderer>(true);
            for (int i = 0; i < strays.Length; i++)
            {
                strays[i].enabled = false;
                pool.Add(strays[i]);
            }
        }

        const string HolderName = "AimReticleLines";

        void LateUpdate()
        {
            used = 0;
            if (show && Aiming()) Draw();
            for (int i = used; i < pool.Count; i++) pool[i].enabled = false;
        }

        bool Aiming()
        {
            if (player == null || director == null) return false;
            if (!onlyWithBall) return true;
            return ball != null && ball.Carried && ReferenceEquals(ball.Carrier, player);
        }

        void Draw()
        {
            Vector3 from = player.transform.position;
            Vector3 target = director.AimTarget();
            float spread = director.AimSpreadNow();

            Color c = Color.Lerp(tightCol, looseCol,
                                 Mathf.Clamp01(spread / Mathf.Max(looseAt, 0.01f)));

            if (showAimLine) Segment(from, target, lineCol);
            Ring(target, spread, c, ringWidth);
            if (centreDotRadius > 0f) Ring(target, centreDotRadius, c, ringWidth * 0.7f);
        }

        // ------------------------------------------------------------- drawing --

        LineRenderer Take(int points)
        {
            LineRenderer lr;
            if (used < pool.Count) lr = pool[used];
            else
            {
                var go = new GameObject("AimLine");
                go.transform.SetParent(holder, false);
                lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.sharedMaterial = mat;
                lr.numCapVertices = 0;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                pool.Add(lr);
            }
            used++;
            lr.enabled = true;
            lr.positionCount = points;
            return lr;
        }

        void Ring(Vector3 centre, float radius, Color c, float width)
        {
            if (radius <= 0.01f) return;
            LineRenderer lr = Take(segments + 1);
            lr.widthMultiplier = width;
            for (int i = 0; i <= segments; i++)
            {
                float a = (float)i / segments * Mathf.PI * 2f;
                lr.SetPosition(i, Flat(centre) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
            }
            lr.startColor = c;
            lr.endColor = c;
        }

        void Segment(Vector3 a, Vector3 b, Color c)
        {
            LineRenderer lr = Take(2);
            lr.widthMultiplier = lineWidth;
            lr.SetPosition(0, Flat(a));
            lr.SetPosition(1, Flat(b));
            lr.startColor = c;
            lr.endColor = c;
        }

        Vector3 Flat(Vector3 v) { return new Vector3(v.x, height, v.z); }
    }
}
