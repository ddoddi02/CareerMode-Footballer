using System.Collections.Generic;
using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// Where a short pass would arrive, and who would take it - nothing else.
    ///
    /// The match screen shows only the cursor, where the ball will land and who is where
    /// (user, 2026-10-18). So this draws two things while he is on the ball:
    ///
    ///   a ring where a click would put the ball - the lead point of the team-mate nearest
    ///   the cursor (DrillDirector.ShortPassTarget), or the cursor itself if nobody is near
    ///   a ring under that team-mate (DrillDirector.ShortPassReceiver)
    ///
    /// It used to draw the circle of doubt (AimSpread), the line out to the aim, the scan
    /// radius and a shot bar on the goal line. They went with the rest of the effects.
    /// Once the ball is struck, DrillDirector marks where it is going.
    ///
    /// Every number is asked for rather than worked out: the director owns the target,
    /// and it is the same call the strike itself makes (PROJECT.md 3.20).
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
        [Tooltip("Where the ball would arrive.")]
        public Color targetCol = new Color(1f, 1f, 1f, 0.85f);
        public float targetRing = 0.45f;
        [Tooltip("Ring under the team-mate a click would play it to.")]
        public Color receiverCol = new Color(0.45f, 0.85f, 1f, 0.95f);
        public float receiverRing = 0.9f;

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
            Transform receiver = director.ShortPassReceiver();
            Ring(director.ShortPassTarget(), targetRing, targetCol, ringWidth);
            if (receiver != null) Ring(receiver.position, receiverRing, receiverCol, ringWidth * 1.4f);
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

        Vector3 Flat(Vector3 v) { return new Vector3(v.x, height, v.z); }
    }
}
