using Prototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Career.EditorTools
{
    /// <summary>
    /// Builds the shooting practice scene from code, the way the match scene is built
    /// (PROJECT.md 4): the scene is not edited by hand. One end of the pitch, a goal, a ball,
    /// a keeper and a shooter, and the ShotDrill that runs them.
    ///
    /// The match's saved materials are only read, never written - the match builder owns them.
    ///
    /// Menu: Tools > Career > Build Shot Drill Scene
    /// </summary>
    public static class ShotDrillBuilder
    {
        const string Root = "Assets/Career";
        const string SceneDir = Root + "/Scenes";
        const string ScenePath = SceneDir + "/ShotDrill.unity";
        const string MatDir = "Assets/_Prototype/Materials";

        const float HalfW = TacticalPitch.HalfW;
        const float GoalZ = TacticalPitch.HalfL;
        const float PenDepth = TacticalPitch.PenDepth;
        const float PenHalfW = TacticalPitch.PenHalfW;
        const float GoalAreaDepth = 5.5f;
        const float GoalAreaHalfW = 9.16f;
        const float GoalHalfW = TacticalPitch.GoalHalfW;
        const float GoalHeight = 2.44f;
        const float PlayerHeight = 1.80f;
        const float PlayerWidth = 0.55f;

        [MenuItem("Tools/Career/Build Shot Drill Scene")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(SceneDir)) AssetDatabase.CreateFolder(Root, "Scenes");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.52f, 0.55f, 0.56f);
            RenderSettings.skybox = null;
            RenderSettings.fog = false;

            Material mGrass = Load("Pitch"), mLine = Load("Line"), mPlayer = Load("Player"), mNose = Load("Nose");
            Material mKeeper = Load("Keeper"), mBall = Load("Ball"), mPost = Load("Post");

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.05f;
            sun.color = new Color(1f, 0.98f, 0.93f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(52f, -28f, 0f);

            // ---- the attacking third and the goal at +Z ------------------------------
            var pitch = new GameObject("Pitch").transform;
            var grass = GameObject.CreatePrimitive(PrimitiveType.Plane);
            grass.name = "Grass";
            Object.DestroyImmediate(grass.GetComponent<Collider>());
            grass.transform.SetParent(pitch, false);
            const float fromZ = GoalZ - 50f, toZ = GoalZ + 4f;
            grass.transform.position = new Vector3(0f, 0f, (fromZ + toZ) * 0.5f);
            grass.transform.localScale = new Vector3((HalfW + 4f) * 2f / 10f, 1f, (toZ - fromZ) / 10f);
            grass.GetComponent<Renderer>().sharedMaterial = mGrass;

            const float y = 0.02f, w = 0.12f;
            Line(pitch, "goal_line", mLine, w, new Vector3(-HalfW, y, GoalZ), new Vector3(HalfW, y, GoalZ));
            Line(pitch, "touch_L", mLine, w, new Vector3(-HalfW, y, GoalZ), new Vector3(-HalfW, y, fromZ));
            Line(pitch, "touch_R", mLine, w, new Vector3(HalfW, y, GoalZ), new Vector3(HalfW, y, fromZ));
            Line(pitch, "penalty_area", mLine, w,
                 new Vector3(-PenHalfW, y, GoalZ), new Vector3(-PenHalfW, y, GoalZ - PenDepth),
                 new Vector3(PenHalfW, y, GoalZ - PenDepth), new Vector3(PenHalfW, y, GoalZ));
            Line(pitch, "goal_area", mLine, w,
                 new Vector3(-GoalAreaHalfW, y, GoalZ), new Vector3(-GoalAreaHalfW, y, GoalZ - GoalAreaDepth),
                 new Vector3(GoalAreaHalfW, y, GoalZ - GoalAreaDepth), new Vector3(GoalAreaHalfW, y, GoalZ));
            // distance marks every 10 m, for reading where a spot is
            for (float d = 10f; d <= 40f; d += 10f)
                Line(pitch, "mark_" + d, mLine, 0.05f, new Vector3(-1.5f, y, GoalZ - d), new Vector3(1.5f, y, GoalZ - d));

            Cube(pitch, "PostL", new Vector3(-GoalHalfW, GoalHeight * 0.5f, GoalZ), new Vector3(0.12f, GoalHeight, 0.12f), mPost);
            Cube(pitch, "PostR", new Vector3(GoalHalfW, GoalHeight * 0.5f, GoalZ), new Vector3(0.12f, GoalHeight, 0.12f), mPost);
            Cube(pitch, "Crossbar", new Vector3(0f, GoalHeight, GoalZ), new Vector3(GoalHalfW * 2f + 0.12f, 0.12f, 0.12f), mPost);
            Line(pitch, "net_back", mLine, 0.04f, new Vector3(-GoalHalfW, y, GoalZ + 1.5f), new Vector3(GoalHalfW, y, GoalZ + 1.5f));

            // ---- ball -------------------------------------------------------------
            var ballGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ballGo.name = "Ball";
            Object.DestroyImmediate(ballGo.GetComponent<Collider>());
            ballGo.GetComponent<Renderer>().sharedMaterial = mBall;
            var ball = ballGo.AddComponent<Ball>();
            ball.radius = 0.11f;
            ball.ApplyScale();
            ballGo.transform.position = new Vector3(0f, ball.radius, GoalZ - 18f);

            // ---- keeper -----------------------------------------------------------
            var keeperGo = Body("Keeper", new Vector3(0f, 0f, GoalZ - 2f), mKeeper, mNose);
            keeperGo.transform.rotation = Formation.Facing(true);
            var keeper = keeperGo.AddComponent<GoalkeeperAI>();
            keeper.defendsPositiveZ = true;
            keeper.homeSlot = keeperGo.transform.position;
            keeper.ball = ball;
            keeper.alwaysLive = true;            // no DrillDirector here - he goes for every ball at his goal

            // ---- shooter ----------------------------------------------------------
            var shooter = Body("Shooter", new Vector3(0f, 0f, GoalZ - 18.55f), mPlayer, mNose);

            // ---- aim line ---------------------------------------------------------
            var aimGo = new GameObject("AimLine");
            var aim = aimGo.AddComponent<LineRenderer>();
            aim.useWorldSpace = true;
            aim.sharedMaterial = mLine;
            aim.widthMultiplier = 0.06f;
            aim.positionCount = 2;
            aim.shadowCastingMode = ShadowCastingMode.Off;
            aim.receiveShadows = false;

            // ---- camera: behind and above, the whole 40 m in view ----------------
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.015f, 0.025f, 0.035f);
            camGo.transform.position = new Vector3(0f, 30f, GoalZ - 52f);
            camGo.transform.LookAt(new Vector3(0f, 0f, GoalZ - 14f));
            camGo.AddComponent<AudioListener>();

            // ---- goal cam: the keeper close up, bottom left ----------------------
            // From the main camera a 1.8 m keeper 50 m away is twenty pixels tall - a dive
            // reads as a smudge. This one sits on the edge of the box, so the lean is plain.
            var goalCamGo = new GameObject("GoalCam");
            var goalCam = goalCamGo.AddComponent<Camera>();
            goalCam.rect = new Rect(0.015f, 0.02f, 0.345f, 0.40f);   // under the left HUD box, clear of the results
            goalCam.depth = cam.depth + 1;
            goalCam.fieldOfView = 40f;
            goalCam.nearClipPlane = 0.3f;
            goalCam.farClipPlane = 120f;
            goalCam.clearFlags = CameraClearFlags.SolidColor;
            goalCam.backgroundColor = cam.backgroundColor;
            goalCamGo.transform.position = new Vector3(0f, 4.5f, GoalZ - 15f);
            goalCamGo.transform.LookAt(new Vector3(0f, 0.8f, GoalZ - 1f));

            // ---- drill ------------------------------------------------------------
            var drill = new GameObject("ShotDrill").AddComponent<ShotDrill>();
            drill.ball = ball;
            drill.keeper = keeper;
            drill.shooter = shooter.transform;
            drill.aimLine = aim;
            drill.cam = cam;

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            Debug.Log("[ShotDrillBuilder] built " + ScenePath);
        }

        static Material Load(string name)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/" + name + ".mat");
            if (m == null) Debug.LogWarning("[ShotDrillBuilder] missing " + MatDir + "/" + name + ".mat - build the match scene once first.");
            return m;
        }

        static GameObject Body(string name, Vector3 pos, Material body, Material nose)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            Capsule(go.transform, "Body", new Vector3(0f, PlayerHeight * 0.5f, 0f),
                    new Vector3(PlayerWidth, PlayerHeight * 0.5f, PlayerWidth), body);
            Cube(go.transform, "TorsoNose", new Vector3(0f, 1.25f, 0.36f), new Vector3(0.16f, 0.14f, 0.38f), nose);
            return go;
        }

        static void Line(Transform parent, string name, Material mat, float width, params Vector3[] pts)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.sharedMaterial = mat;
            lr.widthMultiplier = width;
            lr.positionCount = pts.Length;
            lr.SetPositions(pts);
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
        }

        static void Capsule(Transform parent, string name, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
        }

        static void Cube(Transform parent, string name, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
        }
    }
}
