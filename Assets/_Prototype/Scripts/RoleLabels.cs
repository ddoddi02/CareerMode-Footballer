using System.Collections.Generic;
using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// Writes each body's slot over his head, and draws a line from him to where his
    /// brain currently wants him to stand.
    ///
    /// For the question "why is he standing THERE". The line splits the two answers:
    ///
    ///   long line     he is not where he is meant to be - movement, a chase, a press
    ///   short line    he is exactly where he is meant to be, so the station itself is
    ///                 wrong - that is the shape, not the legs
    ///
    /// Like PlayDebugView it reads and draws and decides nothing. It spawns itself, so
    /// no scene needs rebuilding to get it. L toggles it.
    /// </summary>
    public class RoleLabels : MonoBehaviour
    {
        public bool show = true;
        [Tooltip("Line from each body to the station his running brain is steering to.")]
        public bool showStation = true;
        public float headHeight = 2.3f;
        public float lineWidth = 0.12f;

        public Color homeCol = new Color(0.35f, 0.7f, 1f);
        public Color awayCol = new Color(1f, 0.4f, 0.35f);

        struct Body
        {
            public Transform t;
            public Role role;
            public AttackerAI atk;
            public DefenderAI def;
            public bool human;
        }

        readonly List<Body> bodies = new List<Body>();
        readonly List<LineRenderer> lines = new List<LineRenderer>();
        Possession possession;
        Material mat;
        float nextScan;
        GUIStyle style, shadow;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Spawn()
        {
            if (FindAnyObjectByType<RoleLabels>() != null) return;
            new GameObject("RoleLabels").AddComponent<RoleLabels>();
        }

        void Scan()
        {
            nextScan = Time.time + 1f;
            if (possession == null) possession = FindAnyObjectByType<Possession>();

            bodies.Clear();
            var seen = new HashSet<GameObject>();
            foreach (var h in FindObjectsByType<FootballerController>())
                if (seen.Add(h.gameObject)) bodies.Add(new Body { t = h.transform, role = h.role, human = true });
            foreach (var a in FindObjectsByType<AttackerAI>())
                if (seen.Add(a.gameObject))
                    bodies.Add(new Body { t = a.transform, role = a.role, atk = a, def = a.GetComponent<DefenderAI>() });
            foreach (var d in FindObjectsByType<DefenderAI>())
                if (seen.Add(d.gameObject))
                    bodies.Add(new Body { t = d.transform, role = d.role, def = d, atk = d.GetComponent<AttackerAI>() });
        }

        Side? SideOf(Body b)
        {
            if (b.human) return Side.Home;
            if (possession == null) return null;
            if (possession.homeDefence != null && b.def != null && Contains(possession.homeDefence.members, b.def)) return Side.Home;
            if (possession.awayDefence != null && b.def != null && Contains(possession.awayDefence.members, b.def)) return Side.Away;
            return possession.SideOf(b.t);
        }

        static bool Contains(DefenderAI[] arr, DefenderAI d)
        {
            if (arr == null) return false;
            for (int i = 0; i < arr.Length; i++) if (arr[i] == d) return true;
            return false;
        }

        void Update()
        {
            if (ProtoInput.RoleLabelsPressed()) show = !show;
            if (Time.time >= nextScan) Scan();

            int used = 0;
            if (show && showStation)
            {
                for (int i = 0; i < bodies.Count; i++)
                {
                    Body b = bodies[i];
                    if (b.t == null || b.human) continue;
                    Vector3 target;
                    if (b.atk != null && b.atk.enabled) target = b.atk.Station;
                    else if (b.def != null && b.def.enabled) target = b.def.Station;
                    else continue;

                    LineRenderer lr = Take(used++);
                    Color c = SideOf(b) == Side.Away ? awayCol : homeCol;
                    c.a = 0.8f;
                    lr.startColor = c;
                    lr.endColor = c;
                    lr.SetPosition(0, Flat(b.t.position));
                    lr.SetPosition(1, Flat(target));
                }
            }
            for (int i = used; i < lines.Count; i++) lines[i].enabled = false;
        }

        static Vector3 Flat(Vector3 p) { p.y = 0.08f; return p; }

        LineRenderer Take(int i)
        {
            if (i < lines.Count) { lines[i].enabled = true; return lines[i]; }
            if (mat == null) mat = ProtoMat.UnlitFade(Color.white);
            var go = new GameObject("StationLine");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.sharedMaterial = mat;
            lr.positionCount = 2;
            lr.widthMultiplier = lineWidth;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lines.Add(lr);
            return lr;
        }

        static string Name(PressIntensity p)
        {
            switch (p)
            {
                case PressIntensity.High: return "강한 압박";
                case PressIntensity.Low: return "내려앉기";
                default: return "중간";
            }
        }

        /// <summary>How hard the defending side is pressing, top centre.</summary>
        void DrawPress()
        {
            if (possession == null) return;
            TeamDefence d = possession.Defending;
            if (d == null) return;

            string text = (possession.InPossession == Side.Home ? "원정" : "홈") + " 수비  압박 강도: " + Name(d.Press);
            if (d.PressWanted != d.Press) text += "  (" + Name(d.PressWanted) + " 구간 - 전환 꺼짐)";

            var r = new Rect(0f, 8f, Screen.width, 24f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, shadow);
            style.normal.textColor = Color.white;
            GUI.Label(r, text, style);
        }

        void OnGUI()
        {
            if (!show) return;
            Camera cam = Camera.main;
            if (cam == null) return;

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                shadow = new GUIStyle(style);
                shadow.normal.textColor = Color.black;
            }

            DrawPress();

            for (int i = 0; i < bodies.Count; i++)
            {
                Body b = bodies[i];
                if (b.t == null) continue;
                Vector3 s = cam.WorldToScreenPoint(b.t.position + Vector3.up * headHeight);
                if (s.z <= 0f) continue;

                // Which brain is running: 공 = attacking, 수 = defending.
                string brain = b.human ? "나"
                    : b.atk != null && b.atk.enabled ? "공"
                    : b.def != null && b.def.enabled ? "수" : "-";
                string text = b.role + " " + brain;

                var r = new Rect(s.x - 50f, Screen.height - s.y - 12f, 100f, 24f);
                GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, shadow);
                style.normal.textColor = b.human ? Color.yellow : SideOf(b) == Side.Away ? awayCol : homeCol;
                GUI.Label(r, text, style);
            }
        }
    }
}
