using System.Collections.Generic;
using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// A small mouse in the bottom-right corner, drawn in lines and filled shapes - no
    /// image file - with the strike buttons coloured and labelled:
    ///
    ///   left button        short pass
    ///   right + left       shot      (the right button alone does nothing)
    ///   front side button  lob       (mouse 5)
    ///   back side button   through   (mouse 4)
    ///
    /// The two side buttons are the reason it exists. "Mouse 4" and "mouse 5" mean nothing
    /// to most people; a picture of the thumb buttons does (user, 2026-10-18). The wheel is
    /// drawn but left empty - nothing is on it.
    ///
    /// DrillDirector adds this at start, so no scene rebuild is needed.
    /// </summary>
    public class MouseLegend : MonoBehaviour
    {
        public bool show = true;
        [Tooltip("1 draws the body 64 x 104 pixels.")]
        public float scale = 1.4f;
        public Vector2 margin = new Vector2(28f, 100f);

        public Color outline = new Color(1f, 1f, 1f, 0.85f);
        public Color shortCol = new Color(0.45f, 0.85f, 1f, 0.9f);
        public Color throughCol = new Color(0.45f, 1f, 0.6f, 0.9f);
        public Color lobCol = new Color(1f, 0.78f, 0.35f, 0.9f);
        public Color shotCol = new Color(1f, 0.55f, 0.35f, 0.9f);

        Material mat;
        GUIStyle label;
        readonly List<Vector2> body = new List<Vector2>();
        readonly List<Vector2> leftButton = new List<Vector2>();
        readonly List<Vector2> rightButton = new List<Vector2>();

        void OnGUI()
        {
            if (!show) return;

            float s = scale;
            float w = 64f * s, h = 104f * s;
            float x0 = Screen.width - margin.x - w;
            float y0 = Screen.height - margin.y - h;
            float cx = x0 + w * 0.5f;
            float split = y0 + h * 0.42f;          // bottom edge of the buttons

            // Side buttons on the thumb side (left, for a right hand): mouse 5 in front.
            Rect lob = new Rect(x0 - 5f * s, y0 + h * 0.44f, 7f * s, h * 0.12f);
            Rect thru = new Rect(x0 - 5f * s, y0 + h * 0.64f, 7f * s, h * 0.12f);
            Rect wheel = new Rect(cx - 4f * s, y0 + h * 0.10f, 8f * s, h * 0.18f);

            if (Event.current.type == EventType.Repaint)
            {
                BuildShapes(x0, y0, w, h, cx, split);
                Begin();
                Fan(new Vector2(x0 + w * 0.26f, y0 + h * 0.26f), leftButton, shortCol);
                Fan(new Vector2(x0 + w * 0.74f, y0 + h * 0.26f), rightButton, shotCol);
                Fill(lob, lobCol);
                Fill(thru, throughCol);

                GL.Begin(GL.LINES);
                GL.Color(outline);
                Loop(body);
                Line(new Vector2(cx, y0), new Vector2(cx, split));
                Line(new Vector2(x0, split), new Vector2(x0 + w, split));
                Box(wheel);
                Box(lob);
                Box(thru);

                // leaders out to the labels on the left
                float lx = x0 - 26f * s;
                Line(new Vector2(x0 + w * 0.2f, y0 + h * 0.2f), new Vector2(lx, y0 + h * 0.2f));
                Line(new Vector2(lob.xMin, lob.center.y), new Vector2(lx, lob.center.y));
                Line(new Vector2(thru.xMin, thru.center.y), new Vector2(lx, thru.center.y));
                GL.End();
                GL.PopMatrix();
            }

            EnsureLabel();
            float right = x0 - 30f * s;
            Label(right, y0 + h * 0.2f, "좌클릭  짧은 패스", shortCol);
            Label(x0 + w, y0 - 14f * s, "우클릭 누른 채 좌클릭  슛", shotCol);
            Label(right, lob.center.y, "마우스5  로빙", lobCol);
            Label(right, thru.center.y, "마우스4  스루패스", throughCol);
        }

        // ------------------------------------------------------------ shapes --

        /// <summary>A capsule: a half-circle on top and bottom, straight sides.</summary>
        void BuildShapes(float x0, float y0, float w, float h, float cx, float split)
        {
            float r = w * 0.5f;
            body.Clear();
            for (int i = 0; i <= 12; i++)                  // top arc, left to right
            {
                float a = Mathf.PI + Mathf.PI * i / 12f;
                body.Add(new Vector2(cx + Mathf.Cos(a) * r, y0 + r + Mathf.Sin(a) * r));
            }
            for (int i = 0; i <= 12; i++)                  // bottom arc, right to left
            {
                float a = Mathf.PI * i / 12f;
                body.Add(new Vector2(cx + Mathf.Cos(a) * r, y0 + h - r + Mathf.Sin(a) * r));
            }

            // The left button: from the split up the left side, round the top-left arc to
            // the middle, and back down the centre line. Convex, so a fan fills it.
            leftButton.Clear();
            leftButton.Add(new Vector2(x0, split));
            for (int i = 0; i <= 6; i++)
            {
                float a = Mathf.PI + Mathf.PI * 0.5f * i / 6f;
                leftButton.Add(new Vector2(cx + Mathf.Cos(a) * r, y0 + r + Mathf.Sin(a) * r));
            }
            leftButton.Add(new Vector2(cx, split));

            // The right button, its mirror.
            rightButton.Clear();
            rightButton.Add(new Vector2(x0 + w, split));
            for (int i = 0; i <= 6; i++)
            {
                float a = 2f * Mathf.PI - Mathf.PI * 0.5f * i / 6f;
                rightButton.Add(new Vector2(cx + Mathf.Cos(a) * r, y0 + r + Mathf.Sin(a) * r));
            }
            rightButton.Add(new Vector2(cx, split));
        }

        void Begin()
        {
            if (mat == null)
            {
                mat = new Material(Shader.Find("Hidden/Internal-Colored"));
                mat.hideFlags = HideFlags.HideAndDontSave;
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                mat.SetInt("_ZWrite", 0);
                mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            }
            mat.SetPass(0);
            GL.PushMatrix();
            // GUI space: origin top-left, y down.
            GL.LoadPixelMatrix(0f, Screen.width, Screen.height, 0f);
        }

        static void Fan(Vector2 centre, List<Vector2> rim, Color c)
        {
            GL.Begin(GL.TRIANGLES);
            GL.Color(c);
            for (int i = 0; i < rim.Count - 1; i++)
            {
                GL.Vertex3(centre.x, centre.y, 0f);
                GL.Vertex3(rim[i].x, rim[i].y, 0f);
                GL.Vertex3(rim[i + 1].x, rim[i + 1].y, 0f);
            }
            GL.Vertex3(centre.x, centre.y, 0f);
            GL.Vertex3(rim[rim.Count - 1].x, rim[rim.Count - 1].y, 0f);
            GL.Vertex3(rim[0].x, rim[0].y, 0f);
            GL.End();
        }

        static void Fill(Rect r, Color c)
        {
            GL.Begin(GL.QUADS);
            GL.Color(c);
            GL.Vertex3(r.xMin, r.yMin, 0f);
            GL.Vertex3(r.xMax, r.yMin, 0f);
            GL.Vertex3(r.xMax, r.yMax, 0f);
            GL.Vertex3(r.xMin, r.yMax, 0f);
            GL.End();
        }

        static void Line(Vector2 a, Vector2 b)
        {
            GL.Vertex3(a.x, a.y, 0f);
            GL.Vertex3(b.x, b.y, 0f);
        }

        static void Loop(List<Vector2> pts)
        {
            for (int i = 0; i < pts.Count; i++) Line(pts[i], pts[(i + 1) % pts.Count]);
        }

        static void Box(Rect r)
        {
            Line(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin));
            Line(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax));
            Line(new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax));
            Line(new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin));
        }

        // ------------------------------------------------------------ labels --

        void EnsureLabel()
        {
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label);
            label.fontSize = 14;
            label.alignment = TextAnchor.MiddleRight;
            try
            {
                Font f = Font.CreateDynamicFontFromOSFont(
                    new string[] { "Malgun Gothic", "맑은 고딕", "Gulim", "Dotum", "Arial" }, 14);
                if (f != null) label.font = f;
            }
            catch { }
        }

        void Label(float right, float y, string text, Color c)
        {
            Color was = GUI.color;
            GUI.color = c;
            GUI.Label(new Rect(right - 180f, y - 11f, 180f, 22f), text, label);
            GUI.color = was;
        }
    }
}
