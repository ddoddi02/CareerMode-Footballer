using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// The whole vertical slice: a pass arrives, a defender may or may not be on your
    /// back, and the angle your TORSO is facing at the first touch decides what
    /// happens. Everything else in the game is downstream of whether this loop is fun.
    ///
    /// The stats panel is the actual experiment: it compares your success rate when
    /// you turned with FRESH information against when you turned BLIND. If those two
    /// numbers are the same, the vision mechanic is decoration and needs redesigning.
    /// </summary>
    public class DrillDirector : MonoBehaviour
    {
        [Header("Refs")]
        public FootballerController player;
        public DefenderAI defender;
        public Perceivable defenderPerc;
        public Transform passer;
        public Ball ball;

        [Header("Layout")]
        public Vector3 startPos = new Vector3(0f, 0f, 0f);
        public Vector3 passerPos = new Vector3(0f, 0f, -13f);

        [Header("Tuning")]
        public float tightGap = 1.45f;
        public float mediumGap = 3.1f;
        public float freshInfoSeconds = 1.6f;
        public float heavyTouchSpeed = 5.2f;

        enum Phase { Setup, Wait, Flight, Result }

        Phase phase = Phase.Setup;
        float phaseT;
        float waitDur;

        // --- experiment counters -------------------------------------------------
        int rounds, shields;
        int turnTry, turnOk;
        int freshTry, freshOk;
        int staleTry, staleOk;

        // --- last resolution -----------------------------------------------------
        string title = "";
        string body = "";
        Color titleCol = Color.white;
        float lastOpen, lastGap, lastAge;

        GUIStyle sBig, sMid, sSmall;
        Texture2D texRed;

        void Start()
        {
            phase = Phase.Setup;
            phaseT = 0f;
        }

        void Update()
        {
            if (ProtoInput.XrayPressed()) PerceptionSystem.xrayDebug = !PerceptionSystem.xrayDebug;
            if (ProtoInput.RestartPressed()) { phase = Phase.Setup; phaseT = 0f; }

            phaseT += Time.deltaTime;

            switch (phase)
            {
                case Phase.Setup: DoSetup(); break;
                case Phase.Wait: DoWait(); break;
                case Phase.Flight: DoFlight(); break;
                case Phase.Result: if (phaseT > 2.4f) { phase = Phase.Setup; phaseT = 0f; } break;
            }
        }

        void DoSetup()
        {
            if (player != null) player.Teleport(startPos, 180f);
            if (passer != null) passer.position = passerPos;

            PressStyle a = (PressStyle)Random.Range(0, 3);
            PressStyle b = (PressStyle)Random.Range(0, 3);
            float lat = Random.Range(-1.3f, 1.3f);
            // Two thirds of the time the defender CHANGES his mind mid-round, so an
            // early scan can be stale by the time the ball arrives.
            float switchDelay = Random.value < 0.66f ? Random.Range(0.7f, 2.1f) : -1f;

            if (defender != null)
            {
                defender.Warp(startPos + new Vector3(lat, 0f, Random.Range(3.5f, 7f)));
                defender.Begin(a, lat, switchDelay, b);
            }

            if (ball != null) ball.Hold(passerPos);

            waitDur = Random.Range(1.7f, 3.3f);
            title = "";
            body = "";
            phase = Phase.Wait;
            phaseT = 0f;
        }

        void DoWait()
        {
            if (phaseT < waitDur) return;
            if (ball == null || player == null || passer == null) return;

            float travel = Random.Range(0.95f, 1.4f);
            float peak = Random.value < 0.25f ? Random.Range(0.9f, 1.9f) : 0.05f;
            ball.Launch(passer.position, player.transform.position, travel, peak);

            phase = Phase.Flight;
            phaseT = 0f;
        }

        void DoFlight()
        {
            if (ball == null || player == null) return;

            Vector3 bp = ball.transform.position; bp.y = 0f;
            Vector3 pp = player.transform.position; pp.y = 0f;

            if (Vector3.Distance(bp, pp) < 1.15f) { Resolve(); return; }

            if (phaseT > 5f)
            {
                rounds++;
                Finish("MISCONTROL", "공을 놓쳤습니다. 패스 쪽으로 움직여서 받으세요.",
                       new Color(0.95f, 0.55f, 0.2f));
            }
        }

        void Resolve()
        {
            ball.Stop();
            rounds++;

            Vector3 toPasser = passer.position - player.transform.position;
            toPasser.y = 0f;
            Vector2 tp = new Vector2(toPasser.x, toPasser.z).normalized;

            // 0deg   = torso square to the ball  -> closed, shielding
            // 180deg = torso pointing at goal    -> fully turned
            float open = Vector2.Angle(player.BodyForward, tp);
            float gap = defender != null ? defender.Gap(player.transform.position) : 99f;
            float age = defenderPerc != null ? defenderPerc.InfoAge : 99f;
            bool fresh = age < freshInfoSeconds;
            bool heavy = player.Speed > heavyTouchSpeed;

            lastOpen = open;
            lastGap = gap;
            lastAge = age;

            bool tight = gap < tightGap;
            bool medium = gap < mediumGap;

            string stance;
            bool success;

            if (open < 55f)
            {
                stance = "등지고 받기 · SHIELD";
                success = !(heavy && tight);
                if (success) shields++;
            }
            else if (open < 118f)
            {
                stance = "하프 턴 · HALF-TURN";
                turnTry++;
                success = !tight && !(heavy && medium);
            }
            else
            {
                stance = "풀 턴 · TURN";
                turnTry++;
                if (tight) success = false;
                else if (medium) success = !heavy && Random.value < 0.5f;
                else success = true;
            }

            if (open >= 55f)
            {
                if (success) turnOk++;
                if (fresh) { freshTry++; if (success) freshOk++; }
                else { staleTry++; if (success) staleOk++; }
            }

            string pressure = tight ? "밀착 압박" : (medium ? "적당한 거리" : "완전히 자유");
            string info = fresh
                ? string.Format("확인 후 판단 ({0:0.0}초 전 목격)", age)
                : (age > 90f ? "한 번도 못 봄 · 블라인드" : string.Format("정보가 낡음 ({0:0.0}초 전)", age));

            string detail = string.Format(
                "{0}\n수비수 {1:0.0}m · {2}\n{3}{4}",
                stance, gap, pressure, info, heavy ? "\n속도가 너무 빨라 터치가 길어짐" : "");

            if (success)
                Finish(open < 55f ? "SAFE" : "TURNED", detail, new Color(0.45f, 0.9f, 0.5f));
            else
                Finish("DISPOSSESSED", detail, new Color(0.95f, 0.35f, 0.35f));
        }

        void Finish(string t, string b, Color c)
        {
            title = t;
            body = b;
            titleCol = c;
            phase = Phase.Result;
            phaseT = 0f;
        }

        // ---------------------------------------------------------------- HUD ----

        void EnsureStyles()
        {
            if (sBig != null) return;

            Font f = null;
            try
            {
                f = Font.CreateDynamicFontFromOSFont(
                    new string[] { "Malgun Gothic", "맑은 고딕", "Gulim", "Dotum", "Arial" }, 16);
            }
            catch { }

            sBig = new GUIStyle(GUI.skin.label);
            sBig.fontSize = 42;
            sBig.fontStyle = FontStyle.Bold;
            sBig.alignment = TextAnchor.MiddleCenter;

            sMid = new GUIStyle(GUI.skin.label);
            sMid.fontSize = 18;
            sMid.alignment = TextAnchor.MiddleCenter;
            sMid.wordWrap = true;

            sSmall = new GUIStyle(GUI.skin.label);
            sSmall.fontSize = 14;
            sSmall.wordWrap = true;

            if (f != null) { sBig.font = f; sMid.font = f; sSmall.font = f; }

            texRed = new Texture2D(1, 1);
            texRed.SetPixel(0, 0, Color.white);
            texRed.Apply();
        }

        static float Pct(int ok, int tries)
        {
            return tries > 0 ? 100f * ok / tries : 0f;
        }

        void OnGUI()
        {
            EnsureStyles();
            int w = Screen.width, h = Screen.height;

            // Contact warning: the defender is physically on your back but is behind
            // your vision cone. Without this the darkness would just feel unfair.
            if (defender != null && player != null)
            {
                float gap = defender.Gap(player.transform.position);
                if (gap < 1.15f)
                {
                    float a = Mathf.InverseLerp(1.15f, 0.6f, gap) * (0.35f + 0.25f * Mathf.Sin(Time.time * 14f));
                    GUI.color = new Color(1f, 0.15f, 0.15f, Mathf.Clamp01(a));
                    GUI.DrawTexture(new Rect(0, 0, w, 14), texRed);
                    GUI.DrawTexture(new Rect(0, h - 14, w, 14), texRed);
                    GUI.DrawTexture(new Rect(0, 0, 14, h), texRed);
                    GUI.DrawTexture(new Rect(w - 14, 0, 14, h), texRed);
                    GUI.color = Color.white;
                }
            }

            // Stats - the point of the whole prototype.
            GUILayout.BeginArea(new Rect(14, 14, 330, 240), GUI.skin.box);
            GUILayout.Label(string.Format("라운드 {0}", rounds), sSmall);
            GUILayout.Label(string.Format("등지고 받기 성공 {0}", shields), sSmall);
            GUILayout.Label(string.Format("턴 시도 {0} · 성공 {1} ({2:0}%)", turnTry, turnOk, Pct(turnOk, turnTry)), sSmall);
            GUILayout.Space(6);
            GUILayout.Label("── 스캔이 의미가 있는가 ──", sSmall);
            GUILayout.Label(string.Format("확인 후 턴  {0}/{1}  ({2:0}%)", freshOk, freshTry, Pct(freshOk, freshTry)), sSmall);
            GUILayout.Label(string.Format("블라인드 턴 {0}/{1}  ({2:0}%)", staleOk, staleTry, Pct(staleOk, staleTry)), sSmall);
            GUILayout.Space(6);
            if (player != null)
                GUILayout.Label(string.Format("몸통 개방각 {0:0}°  |  시선 {1}",
                    Vector2.Angle(player.BodyForward, new Vector2(0f, -1f)),
                    player.Scanning ? "스캔 중" : "정면"), sSmall);
            if (defenderPerc != null)
            {
                float age = defenderPerc.InfoAge;
                string s = age > 90f ? "미확인" : string.Format("{0:0.0}초 전", age);
                GUILayout.Label("수비수 정보: " + s, sSmall);
            }
            GUILayout.EndArea();

            // Controls
            GUILayout.BeginArea(new Rect(w - 264, 14, 250, 168), GUI.skin.box);
            GUILayout.Label("WASD  이동", sSmall);
            GUILayout.Label("마우스  몸통 방향", sSmall);
            GUILayout.Label("우클릭(홀드)  고개 돌리기", sSmall);
            GUILayout.Label("Q  퀵 스캔 (어깨 너머)", sSmall);
            GUILayout.Label("R  라운드 리셋", sSmall);
            GUILayout.Label("Tab  시야 제한 끄기" + (PerceptionSystem.xrayDebug ? "  [OFF]" : ""), sSmall);
            GUILayout.EndArea();

            // Phase prompt / ETA
            string prompt = "";
            if (phase == Phase.Wait) prompt = "준비 — 뒤를 확인하세요";
            else if (phase == Phase.Flight)
                prompt = string.Format("공이 옵니다   {0:0.0}s   — 몸통을 만드세요", ball != null ? ball.Eta : 0f);

            if (prompt.Length > 0)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.9f);
                GUI.Label(new Rect(0, h - 92, w, 30), prompt, sMid);
                GUI.color = Color.white;
            }

            // Result
            if (phase == Phase.Result)
            {
                GUI.color = titleCol;
                GUI.Label(new Rect(0, h - 232, w, 54), title, sBig);
                GUI.color = new Color(1f, 1f, 1f, 0.92f);
                GUI.Label(new Rect(w * 0.5f - 300, h - 174, 600, 100), body, sMid);
                GUI.color = Color.white;
            }
        }
    }
}
