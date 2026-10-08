using System.Collections.Generic;
using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// A rig for watching the marking against a build-up, and nothing else.
    ///
    /// A ball in the opposition's back four is there for about two seconds in a live
    /// match before somebody plays forward and the picture becomes something else. Hold
    /// it there, and the same picture plays over and over: every marker on his man, and -
    /// with TeamDefence.allowHighPress on - the centre-back stepping onto their striker
    /// while our pivot lets him go.
    ///
    /// The defence is NOT frozen - the opposite of PassLab. Every defender marks and
    /// tackles as he would in a match; the only thing constrained is where the ball is
    /// allowed to go.
    ///
    /// The table says who is on whom and how far off him he is. Red is inside tackling
    /// range: if that man's opponent gets the ball, he is the one who goes in.
    ///
    ///   T   hold the ball in the back four / let it go again
    /// </summary>
    public class PressDrill : MonoBehaviour
    {
        [Header("Refs - found automatically if left empty")]
        public TeamAttack attack;
        public TeamDefence defence;
        public Ball ball;

        [Header("The rig")]
        [Tooltip("Hold the ball among the back four. T toggles it, and so does ticking it here.")]
        public bool running;

        [Header("Readout")]
        public bool showTable = true;
        [Tooltip("Draw a line from each defender to the man he is marking.")]
        public bool showDuties = true;
        [Tooltip("Draw each man's zone (TeamDefence.holdShape) round his slot.")]
        public bool showZones = true;
        [Tooltip("Draw the three lines the shape stands in (TeamDefence.holdLines) across the pitch, and the floor nobody goes behind.")]
        public bool showLines = true;

        [Header("Look")]
        public float height = 0.05f;
        public float lineWidth = 0.06f;
        public Color dutyCol = new Color(1f, 0.85f, 0.35f, 0.55f);
        [Tooltip("A marker inside his own tackling range.")]
        public Color tightCol = new Color(1f, 0.45f, 0.35f, 0.85f);
        [Tooltip("A zone whose marker is on his man.")]
        public Color zoneOnCol = new Color(0.45f, 0.85f, 1f, 0.7f);
        [Tooltip("A zone whose marker is waiting.")]
        public Color zoneIdleCol = new Color(0.45f, 0.85f, 1f, 0.18f);
        [Tooltip("The back four, midfield and front lines.")]
        public Color lineCol = new Color(1f, 1f, 1f, 0.3f);
        [Tooltip("The line that sent the man on the ball.")]
        public Color pressLineCol = new Color(1f, 0.85f, 0.35f, 0.7f);
        [Tooltip("The floor: keeperClearance in front of the keeper.")]
        public Color floorCol = new Color(1f, 0.3f, 0.3f, 0.6f);

        readonly List<LineRenderer> pool = new List<LineRenderer>();
        Material mat;
        Transform holder;
        int used;
        bool taken;
        bool wasBackFourOnly;
        float startedAt;

        GUIStyle sHead, sRow;
        Texture2D texPanel;

        const string HolderName = "PressDrillLines";

        void Awake()
        {
            mat = ProtoMat.UnlitFade(new Color(1f, 1f, 1f, 0.6f));
            if (attack == null) attack = Possession.FindHomeAttack();
            if (defence == null) defence = Possession.FindAwayDefence();
            if (ball == null) ball = FindAnyObjectByType<Ball>();
            AdoptStrays();
        }

        /// <summary>
        /// Our lines live under a child of our own and only that child is scanned. Several
        /// components on this GameObject pool LineRenderers the same way, and a sweep of
        /// everything below us would adopt somebody else's pool - after which the two of
        /// us spend every frame switching each other's drawings off (PROJECT.md 3.20).
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

        void Update()
        {
            if (ProtoInput.PressDrillPressed()) running = !running;

            // Driven off the flag rather than off the key, so the Inspector tick does the
            // same thing the key does.
            if (running && !taken) Begin();
            else if (!running && taken) Stop();
        }

        void OnDisable() { if (taken) Stop(); }

        void Begin()
        {
            if (attack == null) { running = false; return; }
            wasBackFourOnly = attack.backFourOnly;
            attack.backFourOnly = true;
            taken = true;
            startedAt = Time.time;
            if (defence != null && defence.members != null)
                for (int i = 0; i < defence.members.Length; i++)
                    if (defence.members[i] != null) defence.members[i].ResetRunLog();
        }

        void Stop()
        {
            if (attack != null) attack.backFourOnly = wasBackFourOnly;
            taken = false;
            running = false;
        }

        // -------------------------------------------------------------- drawing --

        void LateUpdate()
        {
            used = 0;
            if (taken && showDuties) DrawDuties();
            if (taken && showZones) DrawZones();
            if (taken && showLines) DrawLines();
            for (int i = used; i < pool.Count; i++) pool[i].enabled = false;
        }

        /// <summary>
        /// The three lines across the pitch, the one that went to the ball picked out, and
        /// the floor in red. Gaps between the lines are what the shape is; a body standing
        /// on the wrong line is a bug you can see from here.
        /// </summary>
        void DrawLines()
        {
            if (defence == null) return;
            float w = TacticalPitch.HalfW;
            if (defence.HoldingShape)
                for (int line = 0; line < 3; line++)
                {
                    float z = defence.LineZ(line);
                    Segment(new Vector3(-w, 0f, z), new Vector3(w, 0f, z),
                            line == defence.PressLine ? pressLineCol : lineCol);
                }
            float f = defence.FloorZ;
            Segment(new Vector3(-w, 0f, f), new Vector3(w, 0f, f), floorCol);
        }

        /// <summary>
        /// Each man's zone round his slot. Bright while he is out of the shape - pressing
        /// or tracking a runner; faint while he holds.
        /// </summary>
        void DrawZones()
        {
            if (defence == null || defence.members == null) return;
            const int seg = 32;

            for (int i = 0; i < defence.members.Length; i++)
            {
                Vector3 c; float r;
                if (!defence.ZoneOf(i, out c, out r)) continue;

                Color col = defence.DutyOf(i) >= 0 ? zoneOnCol : zoneIdleCol;
                LineRenderer lr = Take(seg + 1);
                lr.widthMultiplier = lineWidth;
                lr.startColor = col;
                lr.endColor = col;
                for (int s = 0; s <= seg; s++)
                {
                    float a = s * Mathf.PI * 2f / seg;
                    lr.SetPosition(s, Flat3(c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r)));
                }
            }
        }

        void DrawDuties()
        {
            if (defence == null || defence.members == null || defence.opponents == null) return;

            for (int i = 0; i < defence.members.Length; i++)
            {
                DefenderAI d = defence.members[i];
                Transform man = ManOf(i);
                if (d == null || man == null) continue;

                float gap = Flat(man.position - d.transform.position).magnitude;
                Segment(d.transform.position, man.position, gap <= d.lungeRange ? tightCol : dutyCol);
            }
        }

        Transform ManOf(int i)
        {
            int t = defence.DutyOf(i);
            return t >= 0 && t < defence.opponents.Length ? defence.opponents[t] : null;
        }

        LineRenderer Take(int points)
        {
            LineRenderer lr;
            if (used < pool.Count) lr = pool[used];
            else
            {
                var go = new GameObject("DrillLine");
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

        void Segment(Vector3 a, Vector3 b, Color c)
        {
            LineRenderer lr = Take(2);
            lr.widthMultiplier = lineWidth;
            lr.SetPosition(0, Flat3(a));
            lr.SetPosition(1, Flat3(b));
            lr.startColor = c;
            lr.endColor = c;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        Vector3 Flat3(Vector3 v) { return new Vector3(v.x, height, v.z); }

        // ------------------------------------------------------------------ HUD --

        void OnGUI()
        {
            if (!taken || !showTable) return;
            EnsureStyles();

            const float w = 560f;
            int rows = defence != null && defence.members != null ? defence.members.Length : 0;
            float h = 86f + rows * 17f;

            // Bottom-left. The director already owns the top-left corner and the control
            // legend owns the top-right, and a readout drawn on top of another readout is
            // not a readout.
            float top = Screen.height - h - 12f;
            GUI.DrawTexture(new Rect(8f, top, w, h), texPanel);

            float y = top + 6f;
            GUI.Label(new Rect(16f, y, w, 20f),
                "PRESS DRILL — 공은 4백 안에서만 · T 종료", sHead);
            y += 20f;

            string carrier = "-";
            if (attack != null && attack.BallCarrier != null) carrier = Short(attack.BallCarrier.name);
            // Formation.LineOf counts from the back; the lines are named from the front -
            // 1선 the front three, 3선 the back four.
            string pressLine = defence != null && defence.PressLine >= 0 ? " (" + (3 - defence.PressLine) + "선)" : "";
            GUI.Label(new Rect(16f, y, w, 20f), string.Format(
                "공: {0}    압박 강도: {1}    공 담당: {2}{3}", carrier,
                defence != null ? defence.Press.ToString() : "-",
                defence != null && defence.Presser != null ? Short(defence.Presser.name) : "-", pressLine), sRow);
            y += 18f;

            GUI.Label(new Rect(16f, y, w, 20f), "수비수        담당            거리     뛴 거리/분  전력질주/분", sHead);
            y += 18f;

            if (defence == null || defence.members == null) return;
            // Per minute since the drill started, so a short run and a long one compare.
            float minutes = Mathf.Max(1f / 60f, (Time.time - startedAt) / 60f);
            for (int i = 0; i < defence.members.Length; i++)
            {
                DefenderAI d = defence.members[i];
                if (d == null) continue;

                Transform man = ManOf(i);
                float gap = man != null ? Flat(man.position - d.transform.position).magnitude : 0f;
                bool tight = man != null && gap <= d.lungeRange;

                GUI.color = tight ? new Color(1f, 0.6f, 0.5f) : new Color(0.85f, 0.9f, 0.95f);
                GUI.Label(new Rect(16f, y, w, 18f), string.Format(
                    "{0,-12} {1,-14} {2,-8} {3,-11} {4}",
                    Short(d.name), man != null ? Short(man.name) : "- (자리 유지)",
                    man != null ? gap.ToString("0.0") + "m" : "",
                    (d.Distance / minutes).ToString("0") + "m",
                    (d.SprintDistance / minutes).ToString("0") + "m"), sRow);
                y += 17f;
            }
            GUI.color = Color.white;
        }

        static string Short(string n)
        {
            if (string.IsNullOrEmpty(n)) return "?";
            int i = n.IndexOf('(');
            if (i > 0) n = n.Substring(0, i);
            return n.Replace("Home_", "").Replace("Away_", "").Replace("Home ", "").Replace("Away ", "").Trim();
        }

        void EnsureStyles()
        {
            if (texPanel == null)
            {
                texPanel = new Texture2D(1, 1);
                texPanel.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.72f));
                texPanel.Apply();
                texPanel.hideFlags = HideFlags.HideAndDontSave;
            }
            if (sHead == null)
            {
                sHead = new GUIStyle(GUI.skin.label);
                sHead.fontSize = 13;
                sHead.fontStyle = FontStyle.Bold;
                sHead.normal.textColor = Color.white;
            }
            if (sRow == null)
            {
                sRow = new GUIStyle(GUI.skin.label);
                sRow.fontSize = 12;
                sRow.normal.textColor = Color.white;
            }
        }
    }
}
