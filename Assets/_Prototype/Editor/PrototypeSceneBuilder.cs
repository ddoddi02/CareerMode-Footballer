using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Prototype;

/// <summary>
/// Builds the whole drill scene from code so it can be regenerated and tweaked
/// without hand-editing anything in the Hierarchy.
/// Menu: Tools > Football Prototype > Build Drill Scene
/// </summary>
public static class PrototypeSceneBuilder
{
    const string Root = "Assets/_Prototype";
    const string MatDir = Root + "/Materials";
    const string SceneDir = Root + "/Scenes";
    const string ScenePath = SceneDir + "/ScanAndTurnDrill.unity";

    [MenuItem("Tools/Football Prototype/Build Drill Scene")]
    public static void Build()
    {
        EnsureFolder(Root);
        EnsureFolder(MatDir);
        EnsureFolder(SceneDir);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.52f, 0.55f, 0.56f);
        RenderSettings.skybox = null;
        RenderSettings.fog = false;

        // ---------------------------------------------------------- materials ---
        Material mGrass = Mat("Pitch", new Color(0.16f, 0.38f, 0.19f));
        Material mLine = MatUnlit("Line", new Color(0.92f, 0.95f, 0.92f));
        Material mGrid = MatUnlitFade("Grid", new Color(0.75f, 0.85f, 0.75f, 0.16f));
        Material mPlayer = Mat("Player", new Color(0.95f, 0.82f, 0.20f));
        Material mNose = MatUnlit("Nose", new Color(1f, 1f, 1f));
        Material mDefender = Mat("Defender", new Color(0.86f, 0.22f, 0.22f));
        Material mPasser = Mat("Passer", new Color(0.25f, 0.55f, 0.92f));
        Material mBall = MatUnlit("Ball", new Color(1f, 1f, 1f));
        Material mPost = Mat("Post", new Color(0.9f, 0.9f, 0.92f));
        Material mOverlay = OverlayMaterial();

        // ------------------------------------------------------------- light ----
        var lightGo = new GameObject("Sun");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.05f;
        light.color = new Color(1f, 0.98f, 0.93f);
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(52f, -28f, 0f);

        // ------------------------------------------------------------- pitch ----
        var pitchRoot = new GameObject("Pitch").transform;

        var grass = GameObject.CreatePrimitive(PrimitiveType.Plane);
        grass.name = "Grass";
        grass.transform.SetParent(pitchRoot, false);
        grass.transform.localScale = new Vector3(4f, 1f, 3f);   // 40 x 30 m
        grass.GetComponent<Renderer>().sharedMaterial = mGrass;

        BuildMarkings(pitchRoot, mLine, mGrid, mPost);

        // ------------------------------------------------------------ player ----
        var player = new GameObject("Player");
        var pcc = player.AddComponent<CharacterController>();
        pcc.height = 1.8f;
        pcc.radius = 0.32f;
        pcc.center = new Vector3(0f, 0.9f, 0f);
        pcc.slopeLimit = 60f;
        pcc.stepOffset = 0.3f;

        AddCapsule(player.transform, "Body", new Vector3(0f, 0.9f, 0f),
                   new Vector3(0.62f, 0.9f, 0.62f), mPlayer);
        AddCube(player.transform, "TorsoNose", new Vector3(0f, 1.28f, 0.44f),
                new Vector3(0.22f, 0.2f, 0.5f), mNose);

        var headPivot = new GameObject("HeadPivot").transform;
        headPivot.SetParent(player.transform, false);
        AddCube(headPivot, "HeadMarker", new Vector3(0f, 1.86f, 0.26f),
                new Vector3(0.34f, 0.12f, 0.36f), mNose);

        var fc = player.AddComponent<FootballerController>();
        fc.headMarker = headPivot;

        // ---------------------------------------------------------- defender ----
        var defender = new GameObject("Defender");
        var dcc = defender.AddComponent<CharacterController>();
        dcc.height = 1.8f;
        dcc.radius = 0.32f;
        dcc.center = new Vector3(0f, 0.9f, 0f);
        defender.transform.position = new Vector3(0f, 0f, 5f);
        AddCapsule(defender.transform, "Body", new Vector3(0f, 0.9f, 0f),
                   new Vector3(0.62f, 0.9f, 0.62f), mDefender);
        AddCube(defender.transform, "TorsoNose", new Vector3(0f, 1.28f, 0.44f),
                new Vector3(0.2f, 0.18f, 0.44f), mNose);

        var dai = defender.AddComponent<DefenderAI>();
        dai.target = player.transform;

        var dPerc = defender.AddComponent<Perceivable>();
        dPerc.tint = new Color(0.86f, 0.22f, 0.22f);

        // ------------------------------------------------------------ passer ----
        var passer = new GameObject("Passer");
        passer.transform.position = new Vector3(0f, 0f, -13f);
        AddCapsule(passer.transform, "Body", new Vector3(0f, 0.9f, 0f),
                   new Vector3(0.62f, 0.9f, 0.62f), mPasser);
        var passerPerc = passer.AddComponent<Perceivable>();
        passerPerc.tint = new Color(0.25f, 0.55f, 0.92f);

        // -------------------------------------------------------------- ball ----
        var ballGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ballGo.name = "Ball";
        Object.DestroyImmediate(ballGo.GetComponent<Collider>());
        ballGo.transform.localScale = Vector3.one * 0.26f;
        ballGo.GetComponent<Renderer>().sharedMaterial = mBall;
        var ball = ballGo.AddComponent<Ball>();
        var ballPerc = ballGo.AddComponent<Perceivable>();
        ballPerc.tint = Color.white;
        ballPerc.ghostScale = Vector3.one * 0.9f;

        // ------------------------------------------------------------ camera ----
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 13f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 220f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.015f, 0.025f, 0.035f);
        camGo.AddComponent<AudioListener>();

        var pitchCam = camGo.AddComponent<PitchCamera>();
        pitchCam.target = player.transform;
        pitchCam.controller = fc;

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "VisionOverlayQuad";
        Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(camGo.transform, false);
        var qr = quad.GetComponent<Renderer>();
        qr.sharedMaterial = mOverlay;
        qr.shadowCastingMode = ShadowCastingMode.Off;
        qr.receiveShadows = false;

        var overlay = camGo.AddComponent<VisionOverlay>();
        overlay.cam = cam;
        overlay.viewer = fc;
        overlay.quad = quad.transform;

        fc.cam = cam;

        // ---------------------------------------------------------- director ----
        var director = new GameObject("GameDirector");
        var drill = director.AddComponent<DrillDirector>();
        drill.player = fc;
        drill.defender = dai;
        drill.defenderPerc = dPerc;
        drill.passer = passer.transform;
        drill.ball = ball;
        drill.startPos = Vector3.zero;
        drill.passerPos = new Vector3(0f, 0f, -13f);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[Prototype] Drill scene built at " + ScenePath +
                  "  —  press Play. WASD move, mouse = torso, hold RMB = free head, Q = quick scan.");
    }

    // ------------------------------------------------------------- markings ----

    static void BuildMarkings(Transform parent, Material line, Material grid, Material post)
    {
        var root = new GameObject("Markings").transform;
        root.SetParent(parent, false);

        const float y = 0.02f;
        const float hx = 20f, hz = 15f;

        // Faint 5 m grid. Without this the dark half of the screen has no anchor and
        // you lose all sense of where you are on the pitch.
        for (float x = -hx + 5f; x < hx; x += 5f)
            Line(root, "grid_x" + x, grid, 0.06f,
                 new Vector3(x, y, -hz), new Vector3(x, y, hz));
        for (float z = -hz + 5f; z < hz; z += 5f)
            Line(root, "grid_z" + z, grid, 0.06f,
                 new Vector3(-hx, y, z), new Vector3(hx, y, z));

        // Boundary
        Line(root, "boundary", line, 0.14f,
             new Vector3(-hx, y, -hz), new Vector3(hx, y, -hz),
             new Vector3(hx, y, hz), new Vector3(-hx, y, hz), new Vector3(-hx, y, -hz));

        // Halfway line + centre circle
        Line(root, "halfway", line, 0.14f, new Vector3(-hx, y, 0f), new Vector3(hx, y, 0f));
        Circle(root, "centre_circle", line, 0.14f, Vector3.zero, 5.5f, 56);

        // Penalty area and six-yard box at the attacking end
        Line(root, "penalty_box", line, 0.14f,
             new Vector3(-10f, y, hz), new Vector3(-10f, y, hz - 6.5f),
             new Vector3(10f, y, hz - 6.5f), new Vector3(10f, y, hz));
        Line(root, "six_yard", line, 0.14f,
             new Vector3(-4.5f, y, hz), new Vector3(-4.5f, y, hz - 2.4f),
             new Vector3(4.5f, y, hz - 2.4f), new Vector3(4.5f, y, hz));

        // Goal posts give an unmistakable "this way is forward" reference.
        AddCube(root, "PostL", new Vector3(-3.66f, 1.22f, hz), new Vector3(0.24f, 2.44f, 0.24f), post);
        AddCube(root, "PostR", new Vector3(3.66f, 1.22f, hz), new Vector3(0.24f, 2.44f, 0.24f), post);
        AddCube(root, "Crossbar", new Vector3(0f, 2.44f, hz), new Vector3(7.56f, 0.2f, 0.2f), post);
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
        lr.numCapVertices = 0;
        lr.numCornerVertices = 0;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
    }

    static void Circle(Transform parent, string name, Material mat, float width,
                       Vector3 centre, float radius, int segments)
    {
        var pts = new Vector3[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float a = (float)i / segments * Mathf.PI * 2f;
            pts[i] = centre + new Vector3(Mathf.Cos(a) * radius, 0.02f, Mathf.Sin(a) * radius);
        }
        Line(parent, name, mat, width, pts);
    }

    // --------------------------------------------------------------- helpers ---

    static GameObject AddCapsule(Transform parent, string name, Vector3 pos, Vector3 scale, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go;
    }

    static GameObject AddCube(Transform parent, string name, Vector3 pos, Vector3 scale, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int i = path.LastIndexOf('/');
        string parent = path.Substring(0, i);
        string leaf = path.Substring(i + 1);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    static Material Save(Material m, string name)
    {
        string path = MatDir + "/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            existing.shader = m.shader;
            existing.CopyPropertiesFromMaterial(m);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(m);
            return existing;
        }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static Material Mat(string name, Color c) { return Save(ProtoMat.Lit(c), name); }
    static Material MatUnlit(string name, Color c) { return Save(ProtoMat.Unlit(c), name); }
    static Material MatUnlitFade(string name, Color c) { return Save(ProtoMat.UnlitFade(c), name); }

    static Material OverlayMaterial()
    {
        var sh = Shader.Find("Prototype/VisionOverlay");
        if (sh == null)
        {
            Debug.LogError("[Prototype] Shader 'Prototype/VisionOverlay' not found. " +
                           "Let Unity finish importing, then run the menu item again.");
            return null;
        }
        string path = MatDir + "/VisionOverlay.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) { existing.shader = sh; EditorUtility.SetDirty(existing); return existing; }
        var m = new Material(sh);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
