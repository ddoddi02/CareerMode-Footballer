using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Prototype;

/// <summary>
/// Builds the drill scene from code, at real FIFA dimensions: a 105 x 68 m pitch
/// with correct markings, and 1.80 m players. Everything downstream - vision range,
/// pass weighting, camera framing - is tuned against these numbers, so they are the
/// single source of truth.
///
/// Menu: Tools > Football Prototype > Build Drill Scene
/// </summary>
public static class PrototypeSceneBuilder
{
    const string Root = "Assets/_Prototype";
    const string MatDir = Root + "/Materials";
    const string SceneDir = Root + "/Scenes";
    const string ScenePath = SceneDir + "/ScanAndTurnDrill.unity";

    // --- FIFA recommended dimensions ---------------------------------------
    const float HalfW = 34f;      // 68 m wide   (X)
    const float HalfL = 52.5f;    // 105 m long  (Z), goal to goal
    const float CircleR = 9.15f;
    const float PenDepth = 16.5f;
    const float PenHalfW = 20.16f;
    const float GoalAreaDepth = 5.5f;
    const float GoalAreaHalfW = 9.16f;
    const float PenSpot = 11f;
    const float GoalHalfW = 3.66f;   // 7.32 m
    const float GoalHeight = 2.44f;
    const float CornerR = 1f;

    // --- player ------------------------------------------------------------
    const float PlayerHeight = 1.80f;
    const float PlayerWidth = 0.55f;

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
        Material mGrid = MatUnlitFade("Grid", new Color(0.75f, 0.85f, 0.75f, 0.10f));
        Material mPlayer = Mat("Player", new Color(0.95f, 0.82f, 0.20f));
        Material mNose = MatUnlit("Nose", new Color(1f, 1f, 1f));
        Material mDefender = Mat("Defender", new Color(0.86f, 0.22f, 0.22f));
        Material mPasser = Mat("Passer", new Color(0.25f, 0.55f, 0.92f));
        Material mKeeper = Mat("Keeper", new Color(0.20f, 0.78f, 0.42f));
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
        // Plane primitive is 10 x 10 at scale 1, plus a margin of grass outside the lines.
        grass.transform.localScale = new Vector3((HalfW + 4f) * 2f / 10f, 1f, (HalfL + 4f) * 2f / 10f);
        grass.GetComponent<Renderer>().sharedMaterial = mGrass;

        BuildMarkings(pitchRoot, mLine, mGrid, mPost);

        // ------------------------------------------------------------ player ----
        var player = new GameObject("Player");
        var pcc = player.AddComponent<CharacterController>();
        pcc.height = PlayerHeight;
        pcc.radius = 0.28f;                                   // 0.56 m across
        pcc.center = new Vector3(0f, PlayerHeight * 0.5f, 0f);
        pcc.slopeLimit = 60f;
        pcc.stepOffset = 0.3f;

        // The capsule primitive is 2 units tall, so 0.90 scale == 1.80 m.
        AddCapsule(player.transform, "Body", new Vector3(0f, PlayerHeight * 0.5f, 0f),
                   new Vector3(PlayerWidth, PlayerHeight * 0.5f, PlayerWidth), mPlayer);
        AddCube(player.transform, "TorsoNose", new Vector3(0f, 1.25f, 0.36f),
                new Vector3(0.18f, 0.16f, 0.40f), mNose);

        var headPivot = new GameObject("HeadPivot").transform;
        headPivot.SetParent(player.transform, false);
        AddCube(headPivot, "HeadMarker", new Vector3(0f, 1.84f, 0.22f),
                new Vector3(0.30f, 0.10f, 0.32f), mNose);

        var fc = player.AddComponent<FootballerController>();
        fc.role = Role.DM;                        // 원 볼란치 - the slot the human plays
        fc.passing = Formation.PassingFor(Role.DM);
        fc.headMarker = headPivot;
        fc.pitchHalfX = HalfW;
        fc.pitchHalfZ = HalfL;
        fc.goalX = 0f;

        // ---------------------------------------------------------- defender ----
        var defender = new GameObject("Defender");
        var dcc = defender.AddComponent<CharacterController>();
        dcc.height = PlayerHeight;
        dcc.radius = 0.28f;
        dcc.center = new Vector3(0f, PlayerHeight * 0.5f, 0f);
        defender.transform.position = new Vector3(0f, 0f, 5f);
        AddCapsule(defender.transform, "Body", new Vector3(0f, PlayerHeight * 0.5f, 0f),
                   new Vector3(PlayerWidth, PlayerHeight * 0.5f, PlayerWidth), mDefender);
        AddCube(defender.transform, "TorsoNose", new Vector3(0f, 1.25f, 0.36f),
                new Vector3(0.16f, 0.14f, 0.38f), mNose);

        var dai = defender.AddComponent<DefenderAI>();
        dai.target = player.transform;

        var dPerc = defender.AddComponent<Perceivable>();
        dPerc.tint = new Color(0.86f, 0.22f, 0.22f);
        dPerc.ghostScale = new Vector3(PlayerWidth, PlayerHeight * 0.5f, PlayerWidth);
        fc.threat = dPerc;     // later shoulder checks favour the side he was seen on

        // ------------------------------------------------------------ passer ----
        // The centre-back the possession starts with. He used to be a fixed prop that
        // only ever passed to the human; he is an ordinary bot now and chooses his own
        // ball like everyone else. All that is left of the drill is that play starts at
        // his feet.
        var passer = new GameObject("Passer");
        passer.transform.position = new Vector3(0f, 0f, -13f);
        var passerCc = passer.AddComponent<CharacterController>();
        passerCc.height = PlayerHeight;
        passerCc.radius = 0.28f;
        passerCc.center = new Vector3(0f, PlayerHeight * 0.5f, 0f);
        passerCc.slopeLimit = 60f;
        passerCc.stepOffset = 0.3f;
        var passerAi = passer.AddComponent<AttackerAI>();
        AddCapsule(passer.transform, "Body", new Vector3(0f, PlayerHeight * 0.5f, 0f),
                   new Vector3(PlayerWidth, PlayerHeight * 0.5f, PlayerWidth), mPasser);
        var passerPerc = passer.AddComponent<Perceivable>();
        passerPerc.tint = new Color(0.25f, 0.55f, 0.92f);
        passerPerc.ghostScale = new Vector3(PlayerWidth, PlayerHeight * 0.5f, PlayerWidth);

        // ------------------------------------------------------------- squads ----
        // 4-1-2-3 on both sides, from Formation.cs. Bodies and positions only - no AI,
        // no marking, nobody moves. The three objects the drill already owns take their
        // own slots rather than being duplicated alongside a second copy:
        //     Player   -> home DM   (원 볼란치, the slot the human plays)
        //     Passer   -> home LCB  (the ball now comes out of the back line)
        //     Defender -> away ST   (the striker screening the single six)
        var homeRoot = new GameObject("HomeTeam").transform;
        var awayRoot = new GameObject("AwayTeam").transform;

        Color homeTint = new Color(0.25f, 0.55f, 0.92f);
        Color awayTint = new Color(0.86f, 0.22f, 0.22f);
        Color keeperTint = new Color(0.20f, 0.78f, 0.42f);
        Vector3 passerHome = new Vector3(0f, 0f, -13f);   // overwritten by the LCB slot

        var awayBots = new System.Collections.Generic.List<DefenderAI>();
        var homeBots = new System.Collections.Generic.List<AttackerAI>();
        var homeBodies = new System.Collections.Generic.List<Transform>();
        var awayBodies = new System.Collections.Generic.List<Transform>();
        // Everyone on the home side who can receive a pass. The keeper is deliberately
        // not in it: he has no brain to control the ball with, so a ball played back to
        // him would simply roll past and die.
        var homeMates = new System.Collections.Generic.List<Transform>();

        for (int t = 0; t < 2; t++)
        {
            bool away = t == 1;
            Transform root = away ? awayRoot : homeRoot;
            string prefix = away ? "Away_" : "Home_";

            foreach (var slot in Formation.F4123)
            {
                Vector3 pos = Formation.WorldPos(slot, away, HalfL);

                if (!away && slot.role == Role.DM)
                {
                    Slot(player, root, pos, away, "Player (Home DM)");
                    homeBodies.Add(player.transform);
                    homeMates.Add(player.transform);
                    continue;
                }
                if (!away && slot.role == Role.LCB)
                {
                    Slot(passer, root, pos, away, "Passer (Home LCB)");
                    passerAi.role = slot.role;
                    passerAi.homeSlot = pos;
                    passerAi.passing = Formation.PassingFor(slot.role);
                    homeBots.Add(passerAi);
                    homeBodies.Add(passer.transform);
                    homeMates.Add(passer.transform);
                    passerHome = pos;
                    continue;
                }
                if (away && slot.role == Role.ST)
                {
                    Slot(defender, root, pos, away, "Defender (Away ST)");
                    dai.role = slot.role;
                    dai.homeSlot = pos;
                    awayBots.Add(dai);
                    awayBodies.Add(defender.transform);
                    continue;
                }

                // Keepers have no brain yet. Away outfielders defend; home outfielders
                // move off the ball. The human and the drill's passer are hand-driven and
                // must not get one.
                bool gk = slot.role == Role.GK;
                int kind = gk ? 0 : (away ? 1 : 2);

                var body = SpawnSquadMember(root, prefix + slot.label, pos, away,
                                            gk ? mKeeper : (away ? mDefender : mPasser), mNose,
                                            gk ? keeperTint : (away ? awayTint : homeTint), kind);
                if (kind == 1)
                {
                    var b = body.GetComponent<DefenderAI>();
                    b.role = slot.role;
                    b.homeSlot = pos;
                    b.target = player.transform;
                    awayBots.Add(b);
                }
                else if (kind == 2)
                {
                    var a = body.GetComponent<AttackerAI>();
                    a.role = slot.role;
                    a.homeSlot = pos;
                    a.passing = Formation.PassingFor(slot.role);
                    homeBots.Add(a);
                }

                if (away) awayBodies.Add(body.transform);
                else
                {
                    homeBodies.Add(body.transform);
                    if (!gk) homeMates.Add(body.transform);
                }
            }
        }

        // Each side's shared picture and standing orders.
        var teamDef = awayRoot.gameObject.AddComponent<TeamDefence>();
        teamDef.defendsPositiveZ = true;               // away protects the goal at +Z
        teamDef.members = awayBots.ToArray();
        teamDef.opponents = homeBodies.ToArray();

        var teamAtk = homeRoot.gameObject.AddComponent<TeamAttack>();
        teamAtk.attacksPositiveZ = true;               // home attacks the goal at +Z
        teamAtk.members = homeBots.ToArray();
        teamAtk.mates = homeMates.ToArray();
        teamAtk.opponents = awayBodies.ToArray();      // keeper included - he is usually the last man

        // -------------------------------------------------------------- ball ----
        var ballGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ballGo.name = "Ball";
        Object.DestroyImmediate(ballGo.GetComponent<Collider>());
        ballGo.GetComponent<Renderer>().sharedMaterial = mBall;
        var ball = ballGo.AddComponent<Ball>();
        ball.radius = 0.11f;                                  // size 5, 69 cm around
        ball.ApplyScale();                                    // -> 0.22 m across
        fc.lookTarget = ballGo.transform;   // head watches the ball by default
        teamDef.ball = ballGo.transform;
        teamDef.ballBody = ball;            // he cuts passes out, so he reads its path
        teamAtk.ball = ballGo.transform;
        teamAtk.ballBody = ball;            // and this side actually plays it
        var ballPerc = ballGo.AddComponent<Perceivable>();
        ballPerc.tint = Color.white;
        ballPerc.ghostScale = Vector3.one * 0.5f;

        // ------------------------------------------------------------ camera ----
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 16f;        // ~32 m tall, close to the 30 m vision range
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 400f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.015f, 0.025f, 0.035f);
        camGo.AddComponent<AudioListener>();

        var pitchCam = camGo.AddComponent<PitchCamera>();
        pitchCam.target = player.transform;
        pitchCam.controller = fc;
        pitchCam.orthoSize = 16f;
        pitchCam.distance = 90f;

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
        drill.attack = teamAtk;
        drill.defence = teamDef;
        drill.startPos = Vector3.zero;
        drill.passerHome = passerHome;      // the LCB slot he now stands in
        drill.pitchHalfX = HalfW;    // the ball is in play until it crosses a real line
        drill.pitchHalfZ = HalfL;
        drill.goalZ = HalfL;
        drill.goalHalfWidth = 3.66f;

        // Reads the ranges off the components that own them and draws them on the grass.
        // An orthographic camera 90 m up has no depth cue, so "how close is he" is not a
        // question the screen can answer on its own. G toggles it.
        var view = director.AddComponent<PlayDebugView>();
        view.ball = ball;
        view.attack = teamAtk;
        view.defence = teamDef;
        view.player = fc;
        view.director = drill;

        // The pass bench (P). Freezes everybody and plays passes on command, so the
        // selector's own criteria can be read off a picture that is not moving.
        var lab = director.AddComponent<PassLab>();
        lab.ball = ball;
        lab.attack = teamAtk;
        lab.defence = teamDef;
        lab.player = fc;
        lab.director = drill;
        view.lab = lab;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(string.Format(
            "[Prototype] Match scene built at {0}  —  pitch {1} x {2} m, players {3:0.00} m, " +
            "two 4-1-2-3 squads. Home plays it out of the back among themselves; away " +
            "defends and cuts passes out. " +
            "WASD moves, the MOUSE looks and aims, E = pass, R = shoot, F = through, C = cross, " +
            "Q = shoulder check, Shift = sprint, Space = shield, G = ranges and lanes, " +
            "P = pass bench (freeze everybody, Return to pass).",
            ScenePath, HalfW * 2f, HalfL * 2f, PlayerHeight));
    }

    // ------------------------------------------------------------- markings ----

    static void BuildMarkings(Transform parent, Material line, Material grid, Material post)
    {
        var root = new GameObject("Markings").transform;
        root.SetParent(parent, false);

        const float y = 0.02f;
        const float w = 0.12f;

        // Faint 10 m grid. The middle of a full-size pitch is featureless, and in the
        // dark you need something to place yourself against.
        for (float x = -30f; x <= 30f; x += 10f)
            Line(root, "grid_x" + x, grid, 0.05f, new Vector3(x, y, -HalfL), new Vector3(x, y, HalfL));
        for (float z = -50f; z <= 50f; z += 10f)
            Line(root, "grid_z" + z, grid, 0.05f, new Vector3(-HalfW, y, z), new Vector3(HalfW, y, z));

        // Touchlines and goal lines
        Line(root, "boundary", line, w,
             new Vector3(-HalfW, y, -HalfL), new Vector3(HalfW, y, -HalfL),
             new Vector3(HalfW, y, HalfL), new Vector3(-HalfW, y, HalfL), new Vector3(-HalfW, y, -HalfL));

        Line(root, "halfway", line, w, new Vector3(-HalfW, y, 0f), new Vector3(HalfW, y, 0f));
        Circle(root, "centre_circle", line, w, Vector3.zero, CircleR, 72);
        Dot(root, "centre_spot", line, Vector3.zero);

        // Both ends
        for (int e = 0; e < 2; e++)
        {
            float s = e == 0 ? 1f : -1f;                 // +1 = the goal at +Z
            string tag = e == 0 ? "_far" : "_near";
            float gl = HalfL * s;                        // goal line

            Line(root, "penalty_area" + tag, line, w,
                 new Vector3(-PenHalfW, y, gl),
                 new Vector3(-PenHalfW, y, gl - PenDepth * s),
                 new Vector3(PenHalfW, y, gl - PenDepth * s),
                 new Vector3(PenHalfW, y, gl));

            Line(root, "goal_area" + tag, line, w,
                 new Vector3(-GoalAreaHalfW, y, gl),
                 new Vector3(-GoalAreaHalfW, y, gl - GoalAreaDepth * s),
                 new Vector3(GoalAreaHalfW, y, gl - GoalAreaDepth * s),
                 new Vector3(GoalAreaHalfW, y, gl));

            Vector3 spot = new Vector3(0f, 0f, gl - PenSpot * s);
            Dot(root, "penalty_spot" + tag, line, spot);

            // The D: the part of the 9.15 m arc that lies outside the penalty area.
            ArcOutsideBox(root, "penalty_arc" + tag, line, w, spot, CircleR,
                          gl - PenDepth * s, s);

            // Corner arcs
            for (int c = 0; c < 2; c++)
            {
                float cx = c == 0 ? -HalfW : HalfW;
                CornerArc(root, "corner" + tag + c, line, w, new Vector3(cx, 0f, gl), CornerR, s, cx > 0f);
            }

            // Goal frame
            AddCube(root, "PostL" + tag, new Vector3(-GoalHalfW, GoalHeight * 0.5f, gl),
                    new Vector3(0.12f, GoalHeight, 0.12f), post);
            AddCube(root, "PostR" + tag, new Vector3(GoalHalfW, GoalHeight * 0.5f, gl),
                    new Vector3(0.12f, GoalHeight, 0.12f), post);
            AddCube(root, "Crossbar" + tag, new Vector3(0f, GoalHeight, gl),
                    new Vector3(GoalHalfW * 2f + 0.12f, 0.12f, 0.12f), post);
        }
    }

    /// <summary>The arc of a circle that falls beyond the penalty area edge.</summary>
    static void ArcOutsideBox(Transform parent, string name, Material m, float width,
                              Vector3 centre, float radius, float boxEdgeZ, float side)
    {
        var pts = new System.Collections.Generic.List<Vector3>();
        for (int i = 0; i <= 96; i++)
        {
            float a = (float)i / 96f * Mathf.PI * 2f;
            Vector3 p = centre + new Vector3(Mathf.Cos(a) * radius, 0.02f, Mathf.Sin(a) * radius);
            bool outside = side > 0f ? p.z < boxEdgeZ : p.z > boxEdgeZ;
            if (outside) pts.Add(p);
        }
        if (pts.Count > 1) Line(parent, name, m, width, pts.ToArray());
    }

    static void CornerArc(Transform parent, string name, Material m, float width,
                          Vector3 corner, float radius, float side, bool rightSide)
    {
        var pts = new Vector3[13];
        for (int i = 0; i <= 12; i++)
        {
            float a = i / 12f * Mathf.PI * 0.5f;
            float dx = (rightSide ? -1f : 1f) * Mathf.Cos(a) * radius;
            float dz = -side * Mathf.Sin(a) * radius;
            pts[i] = corner + new Vector3(dx, 0.02f, dz);
        }
        Line(parent, name, m, width, pts);
    }

    static void Dot(Transform parent, string name, Material m, Vector3 centre)
    {
        Circle(parent, name, m, 0.22f, centre, 0.11f, 10);
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

    /// <summary>Move an object the drill already owns into its formation slot.</summary>
    static void Slot(GameObject go, Transform root, Vector3 pos, bool away, string name)
    {
        go.name = name;
        go.transform.SetParent(root, true);
        go.transform.position = pos;
        go.transform.rotation = Formation.Facing(away);
    }

    /// <summary>A body in a formation slot. Perceivable, but with no behaviour on it.</summary>
    static GameObject SpawnSquadMember(Transform root, string name, Vector3 pos, bool away,
                                       Material body, Material nose, Color tint, int kind)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.position = pos;
        go.transform.rotation = Formation.Facing(away);

        if (kind != 0)
        {
            var cc = go.AddComponent<CharacterController>();
            cc.height = PlayerHeight;
            cc.radius = 0.28f;
            cc.center = new Vector3(0f, PlayerHeight * 0.5f, 0f);
            cc.slopeLimit = 60f;
            cc.stepOffset = 0.3f;
            if (kind == 1) go.AddComponent<DefenderAI>();
            else go.AddComponent<AttackerAI>();
        }

        AddCapsule(go.transform, "Body", new Vector3(0f, PlayerHeight * 0.5f, 0f),
                   new Vector3(PlayerWidth, PlayerHeight * 0.5f, PlayerWidth), body);
        AddCube(go.transform, "TorsoNose", new Vector3(0f, 1.25f, 0.36f),
                new Vector3(0.16f, 0.14f, 0.38f), nose);

        var perc = go.AddComponent<Perceivable>();
        perc.tint = tint;
        perc.ghostScale = new Vector3(PlayerWidth, PlayerHeight * 0.5f, PlayerWidth);
        return go;
    }

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
