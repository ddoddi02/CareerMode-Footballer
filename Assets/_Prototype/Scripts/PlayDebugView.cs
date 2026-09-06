using System.Collections.Generic;
using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// Draws the numbers the AI is actually working to, on the pitch, at runtime.
    ///
    /// It reads and draws. It decides nothing, and it must stay that way: every distance
    /// here is pulled from the component that owns it rather than copied, so a value
    /// changed in the Inspector moves the picture with it. A debug view that keeps its
    /// own copy of a number is worse than no debug view, because it will eventually
    /// disagree with the thing it is describing and you will believe the picture.
    ///
    /// What it answers:
    ///
    ///   - how near a defender really is. An orthographic camera 90 m up flattens depth
    ///     and a man a metre away looks like a man five metres away, so the ranges are
    ///     drawn as rings on the grass instead of guessed from the screen
    ///   - why a pass was allowed. The 2 m corridor is drawn either side of the line,
    ///     INCLUDING the stretch nearest the passer that the lane test deliberately does
    ///     not look at (PassRules.carrierShadow) - which is the usual answer to "there
    ///     was a defender right there"
    ///   - where a pass would go if you played it now: the wedge either side of your
    ///     stick, and the man currently inside it
    ///
    /// G toggles it.
    /// </summary>
    public class PlayDebugView : MonoBehaviour
    {
        [Header("Refs")]
        public Ball ball;
        public TeamAttack attack;
        public TeamDefence defence;
        public FootballerController player;
        [Tooltip("Where the steal radius lives. Read, never written.")]
        public DrillDirector director;
        [Tooltip("The pass bench. While it is running, every option it is scoring is drawn.")]
        public PassLab lab;

        [Header("Toggle")]
        public bool show = true;

        [Header("Which rings")]
        [Tooltip("The radius inside which a defender simply takes a loose ball off you.")]
        public bool showSteal = true;
        [Tooltip("Defender-to-ball reach - condition 1 of a tackle.")]
        public bool showReach = true;
        [Tooltip("How far off the ball he will commit from.")]
        public bool showLunge = true;

        [Header("Which lanes")]
        public bool showPassCorridor = true;
        [Tooltip("Draw every option the pass bench is scoring, not just the one that won. Green survived the gates, red did not - which is the only way to see that the ball taken was taken over a better one.")]
        public bool showCandidates = true;
        public bool showAimCone = true;
        [Tooltip("Length of the drawn wedge, in metres.")]
        public float coneLength = 26f;

        [Header("Look")]
        public float height = 0.06f;
        public float ringWidth = 0.05f;
        public float laneWidth = 0.07f;
        [Range(8, 64)] public int segments = 40;

        [Header("Colours")]
        public Color stealCol = new Color(1f, 0.25f, 0.25f, 0.85f);
        public Color reachCol = new Color(1f, 0.55f, 0.25f, 0.55f);
        public Color lungeCol = new Color(1f, 0.85f, 0.35f, 0.28f);
        public Color laneOpenCol = new Color(0.35f, 1f, 0.5f, 0.55f);
        public Color laneBlockedCol = new Color(1f, 0.3f, 0.3f, 0.6f);
        public Color shadowCol = new Color(0.6f, 0.6f, 0.7f, 0.28f);
        public Color coneCol = new Color(0.5f, 0.8f, 1f, 0.5f);
        public Color pickCol = new Color(0.5f, 1f, 0.8f, 0.9f);

        readonly List<LineRenderer> pool = new List<LineRenderer>();
        Material mat;
        int used;

        void Awake()
        {
            mat = ProtoMat.UnlitFade(new Color(1f, 1f, 1f, 0.5f));

            // Anything left unwired finds itself. There is exactly one of each in the
            // scene, and a debug view that silently draws nothing because a field was
            // empty is a debug view that lies about what it is showing you.
            if (ball == null) ball = FindAnyObjectByType<Ball>();
            if (attack == null) attack = FindAnyObjectByType<TeamAttack>();
            if (defence == null) defence = FindAnyObjectByType<TeamDefence>();
            if (player == null) player = FindAnyObjectByType<FootballerController>();
            if (director == null) director = FindAnyObjectByType<DrillDirector>();
            if (lab == null) lab = FindAnyObjectByType<PassLab>();

            AdoptStrays();
        }

        /// <summary>
        /// Take ownership of any lines already hanging off us.
        ///
        /// The pool is a plain private field, so a domain reload - which happens every
        /// time a script is edited with the game running - empties it while the child
        /// GameObjects it was tracking survive in the scene. The result is a set of
        /// orphans nobody can reach to switch off: they freeze at whatever they were
        /// drawing and stay on top of the live picture, doubled and a frame out of date.
        /// A debug view that shows a stale number is worse than one that shows nothing.
        /// </summary>
        void AdoptStrays()
        {
            pool.Clear();
            LineRenderer[] strays = GetComponentsInChildren<LineRenderer>(true);
            for (int i = 0; i < strays.Length; i++)
            {
                if (strays[i].gameObject == gameObject) continue;
                strays[i].enabled = false;
                pool.Add(strays[i]);
            }
        }

        void LateUpdate()
        {
            if (ProtoInput.DebugViewPressed()) show = !show;

            used = 0;
            if (show)
            {
                DrawDefenderRings();
                DrawCandidates();
                DrawPassCorridor();
                DrawAimCone();
            }
            for (int i = used; i < pool.Count; i++) pool[i].enabled = false;
        }

        // ------------------------------------------------------------- rings ----

        /// <summary>
        /// Three rings per defender, and they mean three different things. The innermost
        /// is the one that hurts: inside it a loose ball is simply his.
        /// </summary>
        void DrawDefenderRings()
        {
            if (defence == null || defence.members == null) return;

            float steal = director != null ? director.interceptRadius : 0.7f;

            for (int i = 0; i < defence.members.Length; i++)
            {
                DefenderAI d = defence.members[i];
                if (d == null || !d.active) continue;

                Vector3 p = d.transform.position;
                if (showLunge) Ring(p, d.lungeRange, lungeCol);
                if (showReach) Ring(p, Tackle.Reach, reachCol);
                if (showSteal) Ring(p, steal, stealCol);
            }
        }

        // -------------------------------------------------------------- lanes ---

        /// <summary>
        /// The corridor the ball in flight is travelling down, drawn at the width the
        /// lane test actually uses.
        ///
        /// The first stretch is drawn separately and dimmed. That stretch is NOT tested -
        /// a man pressing the passer sits near the start of every lane out of him, so
        /// measuring from the passer's feet reports every ball as blocked (PROJECT.md
        /// §3.16). It is also the single most common reason a pass looks wrong: the
        /// defender you can see was standing in the part nobody looks at.
        /// </summary>
        void DrawPassCorridor()
        {
            if (!showPassCorridor || attack == null || ball == null) return;
            if (!ball.InFlight || !attack.LastPlan.valid) return;

            PassPlan plan = attack.LastPlan;
            Corridor(plan.from, plan.target, attack.pass, attack.opponents);
        }

        /// <summary>
        /// Every ball that was considered, drawn at once - the rejected ones in red, the
        /// survivors in green, and the corridor drawn in full only for the one currently
        /// winning. "Why that pass" is not answerable on its own: it is only ever an
        /// answer next to the passes it beat, which is why the losers are on the grass too.
        ///
        /// Only while the bench is running. In a live match these change several times a
        /// second and would draw a fan of lines nobody can read.
        /// </summary>
        void DrawCandidates()
        {
            if (!showCandidates || lab == null || !lab.Running || attack == null) return;

            IList<PassCandidate> list = lab.Candidates;
            Transform fav = lab.Favourite;
            Vector3 from = lab.Origin;

            for (int i = 0; list != null && i < list.Count; i++)
            {
                PassCandidate c = list[i];
                if (c.receiver == null) continue;
                bool win = fav != null && c.receiver == fav;
                Color col = c.open ? laneOpenCol : laneBlockedCol;
                Segment(from, c.target, col);
                Ring(c.target, win ? 1.1f : 0.6f, win ? pickCol : col);
            }

            if (fav != null) Corridor(from, LaneTarget(list, fav), attack.pass, attack.opponents);
        }

        static Vector3 LaneTarget(IList<PassCandidate> list, Transform who)
        {
            for (int i = 0; list != null && i < list.Count; i++)
                if (list[i].receiver == who) return list[i].target;
            return who.position;
        }

        void Corridor(Vector3 from, Vector3 to, PassRules r, Transform[] opponents)
        {
            Vector3 a = Flat(from);
            Vector3 b = Flat(to);
            Vector3 ab = b - a;
            float len = ab.magnitude;
            if (len < 0.5f) return;

            Vector3 dir = ab / len;
            Vector3 n = Vector3.Cross(Vector3.up, dir) * r.laneHalfWidth;

            // Where the test actually begins.
            float shadow = len > r.carrierShadow * 2f ? r.carrierShadow : 0f;
            Vector3 s = a + dir * shadow;

            float clear = PassPlanner.LaneClearance(from, to, opponents, r.carrierShadow);
            Color c = clear >= r.laneHalfWidth ? laneOpenCol : laneBlockedCol;

            // The untested stretch, dimmed, with the line across it that marks the cut.
            if (shadow > 0.01f)
            {
                Segment(a + n, s + n, shadowCol);
                Segment(a - n, s - n, shadowCol);
                Segment(s + n, s - n, shadowCol);
            }

            Segment(s + n, b + n, c);
            Segment(s - n, b - n, c);
            Segment(b + n, b - n, c);
        }

        // --------------------------------------------------------------- cone ---

        /// <summary>
        /// What the pass button would do right now: the wedge either side of the stick,
        /// and the man it would pick out of it. Nothing here chooses anything - it calls
        /// the same selector the strike does and throws the answer away.
        /// </summary>
        void DrawAimCone()
        {
            if (!showAimCone || player == null || attack == null || ball == null) return;
            if (!ball.Carried || !ReferenceEquals(ball.Carrier, player)) return;

            Vector3 pp = player.transform.position;
            Vector2 aim = player.InputDir.sqrMagnitude > 0.09f
                        ? player.InputDir.normalized
                        : player.BodyForward;

            PassRules r = attack.pass;
            Vector3 fwd = new Vector3(aim.x, 0f, aim.y);
            if (fwd.sqrMagnitude < 1e-4f) return;
            fwd.Normalize();

            Vector3 left = Quaternion.Euler(0f, -r.coneHalfAngle, 0f) * fwd;
            Vector3 right = Quaternion.Euler(0f, r.coneHalfAngle, 0f) * fwd;

            Segment(pp, pp + left * coneLength, coneCol);
            Segment(pp, pp + right * coneLength, coneCol);
            Arc(pp, coneLength, aim, r.coneHalfAngle, coneCol);

            PassPlan plan = PassPlanner.PickInCone(player.transform, pp, aim,
                                                   attack.mates, r, coneLength);
            if (plan.receiver != null)
            {
                Ring(plan.receiver.position, 1.0f, pickCol);
                Corridor(pp, plan.target, r, attack.opponents);
            }
        }

        // ------------------------------------------------------------- pooling --

        LineRenderer Take(int points)
        {
            LineRenderer lr;
            if (used < pool.Count) lr = pool[used];
            else
            {
                var go = new GameObject("DebugLine");
                go.transform.SetParent(transform, false);
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

        void Ring(Vector3 centre, float radius, Color c)
        {
            LineRenderer lr = Take(segments + 1);
            lr.widthMultiplier = ringWidth;
            for (int i = 0; i <= segments; i++)
            {
                float a = (float)i / segments * Mathf.PI * 2f;
                lr.SetPosition(i, Flat(centre) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
            }
            lr.startColor = c;
            lr.endColor = c;
        }

        void Arc(Vector3 centre, float radius, Vector2 aim, float halfAngle, Color c)
        {
            int segs = Mathf.Max(4, Mathf.RoundToInt(halfAngle * 0.5f) * 2);
            LineRenderer lr = Take(segs + 1);
            lr.widthMultiplier = ringWidth;

            float mid = Mathf.Atan2(aim.x, aim.y) * Mathf.Rad2Deg;
            for (int i = 0; i <= segs; i++)
            {
                float a = (mid - halfAngle + 2f * halfAngle * i / segs) * Mathf.Deg2Rad;
                lr.SetPosition(i, Flat(centre) + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius);
            }
            lr.startColor = c;
            lr.endColor = c;
        }

        void Segment(Vector3 a, Vector3 b, Color c)
        {
            LineRenderer lr = Take(2);
            lr.widthMultiplier = laneWidth;
            lr.SetPosition(0, Flat(a));
            lr.SetPosition(1, Flat(b));
            lr.startColor = c;
            lr.endColor = c;
        }

        Vector3 Flat(Vector3 v) { return new Vector3(v.x, height, v.z); }
    }
}
