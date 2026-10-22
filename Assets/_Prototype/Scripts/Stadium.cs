using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Prototype
{
    /// <summary>
    /// The ground around the pitch: a dugout either side of the halfway line with each
    /// side's substitutes, two coaches and the manager, the technical areas marked out in
    /// front of them, and stepped stands with a crowd on all four sides (user, 2026-10-22).
    ///
    /// SCENERY ONLY. No colliders, no Perceivable - nothing here can block the ball, be
    /// tackled, be seen or remembered by the sight system, or show up in any judgement.
    /// The only thing that moves is each manager turning to follow the ball.
    ///
    /// Built at runtime in any scene with a DrillDirector, so no scene has to change
    /// (PROJECT.md 4). Materials are made in code (ProtoMat) like the rest of the runtime
    /// drawing. The crowd is merged into one mesh per shirt colour - a handful of draw
    /// calls for thousands of people - and casts no shadows.
    ///
    /// Every size here is a placeholder (assistant, 2026-10-22).
    /// </summary>
    public class Stadium : MonoBehaviour
    {
        [Header("Benches - on the -x touchline, either side of halfway")]
        [Tooltip("Dugout centre, metres outside the touchline.")]
        public float benchOut = 6.5f;
        [Tooltip("Dugout centre, metres either side of the halfway line. Home is on the side it defends (-z).")]
        public float benchAlong = 9f;
        public int substitutes = 9;
        public int coaches = 2;
        [Tooltip("How quickly a manager turns to follow the ball, per second.")]
        public float managerTurn = 4f;

        [Header("Stands - front row this far beyond the lines")]
        public float standGapSide = 9f;
        public float standGapEnd = 9f;
        public int rows = 16;
        public float rowDepth = 0.85f;
        public float rowRise = 0.5f;
        [Tooltip("One seat every this many metres along a row.")]
        public float seatPitch = 0.7f;
        [Range(0f, 1f)] public float fullness = 0.85f;

        public Color homeKit = new Color(0.12f, 0.28f, 0.62f);
        public Color awayKit = new Color(0.6f, 0.12f, 0.12f);
        public Color coachKit = new Color(0.22f, 0.22f, 0.24f);
        public Color managerKit = new Color(0.07f, 0.07f, 0.08f);
        public Color shelter = new Color(0.18f, 0.19f, 0.21f);
        public Color concrete = new Color(0.42f, 0.42f, 0.44f);
        public Color marking = new Color(0.92f, 0.95f, 0.92f);

        static readonly Color[] Shirts =
        {
            new Color(0.85f, 0.2f, 0.2f), new Color(0.2f, 0.4f, 0.85f), new Color(0.95f, 0.95f, 0.95f),
            new Color(0.12f, 0.12f, 0.14f), new Color(0.95f, 0.8f, 0.2f), new Color(0.25f, 0.6f, 0.3f),
            new Color(0.6f, 0.35f, 0.65f), new Color(0.9f, 0.55f, 0.25f),
        };

        readonly List<Transform> managers = new List<Transform>();
        Ball ball;
        Mesh cube, capsule;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            SceneManager.sceneLoaded += OnLoaded;
            Spawn();
        }

        static void OnLoaded(Scene s, LoadSceneMode mode) { Spawn(); }

        static void Spawn()
        {
            if (FindAnyObjectByType<DrillDirector>() == null || FindAnyObjectByType<Stadium>() != null) return;
            new GameObject("Stadium").AddComponent<Stadium>();
        }

        void Start()
        {
            ball = FindAnyObjectByType<Ball>();
            cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            capsule = Resources.GetBuiltinResource<Mesh>("Capsule.fbx");

            Bench(-1f, homeKit, "Home bench");
            Bench(+1f, awayKit, "Away bench");
            Stands();
        }

        void Update()
        {
            if (ball == null) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < managers.Count; i++)
            {
                Transform m = managers[i];
                Vector3 to = ball.transform.position - m.position;
                to.y = 0f;
                if (to.sqrMagnitude < 0.01f) continue;
                m.rotation = Quaternion.Slerp(m.rotation, Quaternion.LookRotation(to), 1f - Mathf.Exp(-managerTurn * dt));
            }
        }

        // ------------------------------------------------------------- benches --

        /// <summary>One dugout: shelter, bench, subs and coaches sitting, the manager standing out front.</summary>
        void Bench(float sideZ, Color kit, string label)
        {
            Transform root = new GameObject(label).transform;
            root.SetParent(transform, false);

            int seated = substitutes + coaches;
            float length = seated * 0.75f + 0.6f;
            float x = -TacticalPitch.HalfW - benchOut;      // centre of the dugout, its back to the stand
            float z = sideZ * benchAlong;

            Material wall = ProtoMat.Lit(shelter);
            Material seat = ProtoMat.Lit(kit * 0.7f);
            // The canopy is see-through: the camera looks down at 58 degrees, and a solid
            // roof hid everyone sitting under it.
            Color glass = shelter; glass.a = 0.3f;
            Box(root, "Back", new Vector3(x - 1f, 1.1f, z), new Vector3(0.12f, 2.2f, length + 0.2f), wall);
            Box(root, "Roof", new Vector3(x - 0.1f, 2.2f, z), new Vector3(1.9f, 0.08f, length + 0.2f), ProtoMat.UnlitFade(glass), false);
            Box(root, "End A", new Vector3(x - 0.5f, 1.1f, z - length * 0.5f - 0.1f), new Vector3(1f, 2.2f, 0.08f), wall);
            Box(root, "End B", new Vector3(x - 0.5f, 1.1f, z + length * 0.5f + 0.1f), new Vector3(1f, 2.2f, 0.08f), wall);
            Box(root, "Seat", new Vector3(x - 0.55f, 0.22f, z), new Vector3(0.5f, 0.44f, length), seat);

            // Coaches at the halfway end, subs down the rest.
            Material subMat = ProtoMat.Lit(kit);
            Material coachMat = ProtoMat.Lit(coachKit);
            for (int i = 0; i < seated; i++)
            {
                float along = z - sideZ * (length * 0.5f - 0.6f) + sideZ * i * 0.75f;
                bool coach = i < coaches;
                Person(root, coach ? "Coach" : "Sub", new Vector3(x - 0.5f, 0.8f, along), 0.62f, coach ? coachMat : subMat);
            }

            Transform boss = Person(root, "Manager", new Vector3(-TacticalPitch.HalfW - 3f, 0.9f, z), 0.9f, ProtoMat.Lit(managerKit));
            boss.rotation = Quaternion.Euler(0f, 90f, 0f);
            managers.Add(boss);

            // The technical area: a metre either side of the dugout, out to a metre short of the touchline.
            Material line = ProtoMat.Unlit(marking);
            float front = -TacticalPitch.HalfW - 1f, back = x;
            float zLo = z - length * 0.5f - 1f, zHi = z + length * 0.5f + 1f;
            Box(root, "Area front", new Vector3(front, 0.012f, z), new Vector3(0.12f, 0.01f, zHi - zLo), line, false);
            Box(root, "Area side A", new Vector3((front + back) * 0.5f, 0.012f, zLo), new Vector3(front - back, 0.01f, 0.12f), line, false);
            Box(root, "Area side B", new Vector3((front + back) * 0.5f, 0.012f, zHi), new Vector3(front - back, 0.01f, 0.12f), line, false);
        }

        /// <summary>A capsule body, `halfHeight` half as tall as he is, feet on whatever `centre` is above.</summary>
        Transform Person(Transform parent, string label, Vector3 centre, float halfHeight, Material mat)
        {
            GameObject g = new GameObject(label);
            g.transform.SetParent(parent, false);
            g.transform.position = centre;
            g.transform.localScale = new Vector3(0.55f, halfHeight, 0.55f);
            g.AddComponent<MeshFilter>().sharedMesh = capsule;
            g.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return g.transform;
        }

        void Box(Transform parent, string label, Vector3 centre, Vector3 size, Material mat, bool shadows = true)
        {
            GameObject g = new GameObject(label);
            g.transform.SetParent(parent, false);
            g.transform.position = centre;
            g.transform.localScale = new Vector3(Mathf.Abs(size.x), size.y, size.z);
            g.AddComponent<MeshFilter>().sharedMesh = cube;
            MeshRenderer r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            if (!shadows) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
        }

        // -------------------------------------------------------------- stands --

        /// <summary>
        /// Four stepped stands, the two along the touchlines and the two behind the goals
        /// running on into the corners. Each step is one long box from the grass up, so a
        /// stand is a staircase of solid blocks; the crowd sits on the steps.
        /// </summary>
        void Stands()
        {
            var steps = new List<CombineInstance>();
            var crowd = new List<CombineInstance>[Shirts.Length];
            for (int i = 0; i < crowd.Length; i++) crowd[i] = new List<CombineInstance>();
            var rng = new System.Random(20261022);

            float side = TacticalPitch.HalfW + standGapSide;
            float end = TacticalPitch.HalfL + standGapEnd;
            float deep = rows * rowDepth;

            // Along the touchlines: running in z, rising outward in x.
            Stand(new Vector3(-1f, 0f, 0f), side, -end, end, steps, crowd, rng);
            Stand(new Vector3(+1f, 0f, 0f), side, -end, end, steps, crowd, rng);
            // Behind the goals: running in x, out into the corners.
            Stand(new Vector3(0f, 0f, -1f), end, -side - deep, side + deep, steps, crowd, rng);
            Stand(new Vector3(0f, 0f, +1f), end, -side - deep, side + deep, steps, crowd, rng);

            Merged("Stands", steps, ProtoMat.Lit(concrete));
            for (int i = 0; i < crowd.Length; i++) Merged("Crowd " + i, crowd[i], ProtoMat.Lit(Shirts[i]));
        }

        /// <summary>
        /// One stand. `outward` points away from the pitch, `front` is how far out its first
        /// step is, and it runs from `from` to `to` along the other axis.
        /// </summary>
        void Stand(Vector3 outward, float front, float from, float to, List<CombineInstance> steps,
                   List<CombineInstance>[] crowd, System.Random rng)
        {
            Vector3 along = new Vector3(Mathf.Abs(outward.z), 0f, Mathf.Abs(outward.x));
            float length = to - from;
            float mid = (from + to) * 0.5f;

            for (int r = 0; r < rows; r++)
            {
                float out1 = front + (r + 0.5f) * rowDepth;
                float top = (r + 1) * rowRise;
                Vector3 c = outward * out1 + along * mid + Vector3.up * (top * 0.5f);
                Vector3 size = Abs(outward) * rowDepth + along * length + Vector3.up * top;
                steps.Add(Instance(cube, c, size));

                for (float s = from + seatPitch * 0.5f; s < to; s += seatPitch)
                {
                    if (rng.NextDouble() > fullness) continue;
                    float h = 0.85f + (float)rng.NextDouble() * 0.15f;
                    Vector3 p = outward * (out1 + 0.1f) + along * s + Vector3.up * (top + h * 0.5f);
                    crowd[rng.Next(Shirts.Length)].Add(Instance(cube, p, new Vector3(0.45f, h, 0.45f)));
                }
            }
        }

        static Vector3 Abs(Vector3 v) { return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z)); }

        static CombineInstance Instance(Mesh m, Vector3 centre, Vector3 size)
        {
            CombineInstance ci = new CombineInstance();
            ci.mesh = m;
            ci.transform = Matrix4x4.TRS(centre, Quaternion.identity, size);
            return ci;
        }

        void Merged(string label, List<CombineInstance> parts, Material mat)
        {
            if (parts.Count == 0) return;
            Mesh mesh = new Mesh { name = label, indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(parts.ToArray(), true, true);

            GameObject g = new GameObject(label);
            g.transform.SetParent(transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }
}
