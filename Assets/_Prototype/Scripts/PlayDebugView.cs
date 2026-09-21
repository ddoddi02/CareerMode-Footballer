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
    /// <summary>Whose control grid the view is painting.</summary>
    public enum ControlView
    {
        Attack,         // the attacking side's raw map
        Defence,        // the defending side's raw map - a different snapshot, so a different map
        DefenceThreat   // the defending side's map priced by what it costs to concede there
    }

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
        [Tooltip("The corridor the ball is about to travel down, red if somebody is standing in it. Where it is aimed and how wide it might miss is AimReticle's job, not this one's.")]
        public bool showAimCone = true;
        [Tooltip("Paint the control grid on the pitch: blue where we would get there first, red where they would.")]
        public bool showControl = true;
        [Tooltip("WHOSE map to paint. Each side builds its own from its own snapshot, so the two disagree - and where they disagree is where somebody is about to be wrong. DefenceThreat is the defence's map read the way a defence should read it: not 'where do we control' but 'where do THEY own ground that hurts', which is a different picture.")]
        public ControlView controlView = ControlView.Attack;
        [Tooltip("Strongest the paint ever gets. Kept low - it is under the play, not over it.")]
        [Range(0.05f, 0.9f)] public float controlAlpha = 0.34f;

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
        public Color pickCol = new Color(0.5f, 1f, 0.8f, 0.9f);

        readonly List<LineRenderer> pool = new List<LineRenderer>();
        Material mat;
        int used;

        Transform controlQuad;
        Texture2D controlTex;
        Color32[] controlPix;
        float controlBuiltAt = -99f;
        ControlView controlDrawn = (ControlView)(-1);

        void Awake()
        {
            mat = ProtoMat.UnlitFade(new Color(1f, 1f, 1f, 0.5f));

            // Anything left unwired finds itself. There is exactly one of each in the
            // scene, and a debug view that silently draws nothing because a field was
            // empty is a debug view that lies about what it is showing you.
            if (ball == null) ball = FindAnyObjectByType<Ball>();
            if (attack == null) attack = Possession.FindHomeAttack();
            if (defence == null) defence = Possession.FindAwayDefence();
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
                DrawAimLane();
            }
            for (int i = used; i < pool.Count; i++) pool[i].enabled = false;

            DrawControl();
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

        // --------------------------------------------------------------- lane ---

        /// <summary>
        /// What the pass button would do right now: the corridor the ball is about to
        /// travel down, and whether anybody is standing in it.
        ///
        /// This used to draw a wedge either side of the stick and ring the team-mate the
        /// pass would snap onto. It does not any more, because the pass does not snap any
        /// more - the cursor names the target and the ball goes there (PROJECT.md 3.19).
        /// Drawing the old wedge would be drawing a rule that no longer runs, which is
        /// the one thing a debug view must never do.
        ///
        /// The aim point and how wide it might miss by belong to AimReticle. What is
        /// added here is the part the reticle deliberately leaves out: the LANE. "How
        /// accurate is this ball" and "is there a man in the way" are different
        /// questions, and merging them into one drawing answers neither.
        /// </summary>
        void DrawAimLane()
        {
            if (!showAimCone || player == null || attack == null || ball == null) return;
            if (director == null) return;
            if (!ball.Carried || !ReferenceEquals(ball.Carrier, player)) return;

            Corridor(player.transform.position, director.AimTarget(), attack.pass, attack.opponents);
        }

        // ------------------------------------------------------- control grid ---

        /// <summary>
        /// The control grid, painted straight onto the grass.
        ///
        /// A texture rather than a thousand line renderers, because it IS an image: one
        /// pixel per square, point-filtered so the squares stay squares. Blue is ground
        /// we would reach first, red is theirs, and the washed-out middle is the part
        /// that is genuinely up for grabs - which is usually the only part worth looking
        /// at.
        /// </summary>
        void DrawControl()
        {
            PitchControl c = SelectedGrid();
            bool on = show && showControl && c != null && c.Ready;

            if (!on)
            {
                if (controlQuad != null) controlQuad.gameObject.SetActive(false);
                return;
            }

            EnsureControlQuad(c);
            controlQuad.gameObject.SetActive(true);

            // Only repaint when that side has taken a new picture - or when you switched
            // which map you are looking at, which is also a new picture as far as the
            // texture is concerned.
            if (controlDrawn == controlView && Mathf.Approximately(controlBuiltAt, c.BuiltAt)) return;
            controlBuiltAt = c.BuiltAt;
            controlDrawn = controlView;

            bool threat = controlView == ControlView.DefenceThreat && defence != null;

            for (int iz = 0; iz < c.Nz; iz++)
            {
                for (int ix = 0; ix < c.Nx; ix++)
                {
                    Color col;
                    if (threat)
                    {
                        // One-sided, so one hue. Nothing to show where it does not hurt.
                        float e = Mathf.Clamp01(defence.ExposureAt(c.CentreOf(ix, iz)));
                        col = new Color(1f, 0.25f, 0.18f, e * controlAlpha);
                    }
                    else
                    {
                        float v = c.Raw(ix, iz);
                        float a = Mathf.Clamp01(Mathf.Abs(v)) * controlAlpha;
                        col = v >= 0f
                            ? new Color(0.30f, 0.62f, 1f, a)
                            : new Color(1f, 0.32f, 0.30f, a);
                    }
                    controlPix[iz * c.Nx + ix] = col;
                }
            }

            controlTex.SetPixels32(controlPix);
            controlTex.Apply(false);
        }

        /// <summary>
        /// Whose grid is on screen. Blue is always "the side whose map this is", so the
        /// attack's map and the defence's map are mirror images of each other - and where
        /// they are NOT mirror images is the interesting part, because that is the gap
        /// between what the two sides believe.
        /// </summary>
        PitchControl SelectedGrid()
        {
            if (controlView == ControlView.Attack)
                return attack != null ? attack.control : null;
            return defence != null ? defence.control : null;
        }

        void EnsureControlQuad(PitchControl c)
        {
            if (controlTex == null || controlTex.width != c.Nx || controlTex.height != c.Nz)
            {
                controlTex = new Texture2D(c.Nx, c.Nz, TextureFormat.RGBA32, false);
                controlTex.filterMode = FilterMode.Point;    // squares should look like squares
                controlTex.wrapMode = TextureWrapMode.Clamp;
                controlPix = new Color32[c.Nx * c.Nz];
                controlBuiltAt = -99f;
            }

            if (controlQuad == null)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "ControlGrid";
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(transform, false);

                Renderer r = go.GetComponent<Renderer>();
                Material m = ProtoMat.UnlitFade(Color.white);
                m.SetTexture("_BaseMap", controlTex);
                m.mainTexture = controlTex;
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;

                controlQuad = go.transform;
            }
            else
            {
                Renderer r = controlQuad.GetComponent<Renderer>();
                r.sharedMaterial.SetTexture("_BaseMap", controlTex);
                r.sharedMaterial.mainTexture = controlTex;
            }

            // Flat on the grass, covering the whole pitch, just above the markings.
            controlQuad.rotation = Quaternion.Euler(90f, 0f, 0f);
            controlQuad.position = new Vector3(0f, 0.03f, 0f);
            controlQuad.localScale = new Vector3(TacticalPitch.HalfW * 2f, TacticalPitch.HalfL * 2f, 1f);
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
