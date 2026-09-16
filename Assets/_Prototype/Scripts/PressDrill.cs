using System.Collections.Generic;
using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// A rig for watching the press, and nothing else.
    ///
    /// The 4-3-3 pressing sequence (DefenceDuties.PressChain) only happens when the ball
    /// is in the opposition's back four, and in a live match it is there for about two
    /// seconds at a time before somebody plays forward and the whole picture becomes
    /// something else. You cannot check a rotation you only glimpse.
    ///
    /// So: hold the ball in the back four and let everything else run normally. The
    /// defence is NOT frozen - this is the opposite of PassLab. Every defender chooses
    /// his own duty, runs at his own man and covers his own holes; the only thing
    /// constrained is where the ball is allowed to go, so the trigger keeps re-firing
    /// and the same rotation plays over and over until you have seen it properly.
    ///
    /// What to watch, in order:
    ///
    ///   1  the striker leaves the man on the ball alone until he is a CENTRE-BACK,
    ///      then goes
    ///   2  the FAR winger comes inside to the other centre-back - not the near one
    ///   3  the full-back behind that winger steps up onto the full-back he abandoned
    ///   4  and stops level with the winger he left. The cap line is drawn: if he is
    ///      sitting on it, rule 4 is doing something
    ///
    /// The table says who was given to whom. A rotation that looks right on the grass but
    /// reads wrong in the table is two bugs cancelling.
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
        [Tooltip("Draw a line from each defender to the man he was given.")]
        public bool showDuties = true;
        [Tooltip("Draw the line a rotated full-back has been told not to advance past.")]
        public bool showDepthCap = true;

        [Header("Look")]
        public float height = 0.05f;
        public float lineWidth = 0.06f;
        public Color dutyCol = new Color(1f, 0.85f, 0.35f, 0.55f);
        [Tooltip("The line for a man who has actually gone tight rather than just leaning.")]
        public Color tightCol = new Color(1f, 0.45f, 0.35f, 0.85f);
        public Color capCol = new Color(0.45f, 0.8f, 1f, 0.7f);

        readonly List<LineRenderer> pool = new List<LineRenderer>();
        Material mat;
        Transform holder;
        int used;
        bool taken;
        bool wasBackFourOnly;

        GUIStyle sHead, sRow;
        Texture2D texPanel;

        const string HolderName = "PressDrillLines";

        void Awake()
        {
            mat = ProtoMat.UnlitFade(new Color(1f, 1f, 1f, 0.6f));
            if (attack == null) attack = FindAnyObjectByType<TeamAttack>();
            if (defence == null) defence = FindAnyObjectByType<TeamDefence>();
            if (ball == null) ball = FindAnyObjectByType<Ball>();
            AdoptStrays();
        }

        /// <summary>
        /// Our lines live under a child of our own and only that child is scanned. Three
        /// components on this GameObject pool LineRenderers the same way now, and a sweep
        /// of everything below us would adopt somebody else's pool - after which the two
        /// of us spend every frame switching each other's drawings off (PROJECT.md 3.20).
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
            for (int i = used; i < pool.Count; i++) pool[i].enabled = false;
        }

        void DrawDuties()
        {
            if (defence == null || defence.members == null || defence.opponents == null) return;

            for (int i = 0; i < defence.members.Length; i++)
            {
                DefenderAI d = defence.members[i];
                if (d == null) continue;

                int t = defence.DutyOf(i);
                if (t >= 0 && t < defence.opponents.Length && defence.opponents[t] != null)
                {
                    // Tight or merely leaning - the difference is the whole point of
                    // rule 3, so it is drawn rather than left to be guessed.
                    float gap = Flat(defence.opponents[t].position - d.transform.position).magnitude;
                    bool tight = gap <= Formation.DefensiveZone(d.role);
                    Segment(d.transform.position, defence.opponents[t].position,
                            tight ? tightCol : dutyCol);
                }

                if (!showDepthCap) continue;
                float cap = defence.DepthCapOf(i);
                if (float.IsNaN(cap)) continue;
                Segment(new Vector3(d.transform.position.x - 4f, 0f, cap),
                        new Vector3(d.transform.position.x + 4f, 0f, cap), capCol);
            }
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

            const float w = 520f;
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
            GUI.Label(new Rect(16f, y, w, 20f), string.Format(
                "공: {0}    압박 강도: {1}    압박자: {2} {3}", carrier,
                defence != null ? defence.Press.ToString() : "-",
                defence != null && defence.Presser != null ? Short(defence.Presser.name) : "-",
                defence != null && defence.PresserByDuty ? "(담당)" : "(가장 가까움 — 압박이 뚫림)"), sRow);
            y += 18f;

            GUI.Label(new Rect(16f, y, w, 20f),
                "수비수        담당            거리   상태", sHead);
            y += 18f;

            if (defence == null || defence.members == null) return;
            for (int i = 0; i < defence.members.Length; i++)
            {
                DefenderAI d = defence.members[i];
                if (d == null) continue;

                int t = defence.DutyOf(i);
                string mark = "-";
                string state = "";
                float gap = 0f;

                if (t >= 0 && t < defence.opponents.Length && defence.opponents[t] != null)
                {
                    mark = Short(defence.opponents[t].name);
                    gap = Flat(defence.opponents[t].position - d.transform.position).magnitude;
                    state = gap <= Formation.DefensiveZone(d.role) ? "압박" : "견제";
                }
                if (!float.IsNaN(defence.DepthCapOf(i))) state += " · 라인유지";

                GUI.color = state.StartsWith("압박") ? new Color(1f, 0.6f, 0.5f)
                                                     : new Color(0.85f, 0.9f, 0.95f);
                GUI.Label(new Rect(16f, y, w, 18f), string.Format(
                    "{0,-12} {1,-14} {2,5:0.0}m  {3}",
                    Short(d.name), mark, gap, state), sRow);
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
