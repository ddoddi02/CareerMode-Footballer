using UnityEngine;

namespace Prototype
{
    public enum PassType
    {
        ToFeet,  // weak prediction - arrives at you, you receive without hunting for it
        Through  // strong prediction - thrown well ahead, you have to run onto it
    }

    /// <summary>
    /// One repetition: call for the ball, receive it, then keep it.
    ///
    ///   Wait   - your run tells the passer where to put it (S to feet, W through)
    ///   Flight - FC receiving rules: on the ball's axis, or chasing it
    ///   Carry  - the ball is glued a touch in front of you and the defender comes
    ///   Result
    ///
    /// The old version judged the first touch categorically ("turned", "shielded")
    /// and ended the round. It does not need to any more: with a real carry phase the
    /// outcome emerges instead. Turn into the defender and he takes it off you; sprint
    /// and the ball runs far enough ahead to be robbed. Nothing scores that - it just
    /// happens.
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
        public Vector3 startPos = Vector3.zero;
        public Vector3 passerHome = new Vector3(0f, 0f, -13f);
        public float pitchHalfX = 30f;
        public float pitchHalfZ = 40f;
        public float goalZ = 52.5f;

        [Header("Pass to feet - weak prediction")]
        public float feetLeadFactor = 0.40f;
        public float feetMaxLead = 2.0f;
        public float feetSpeedMin = 12f;
        public float feetSpeedMax = 16f;
        public float feetArrivePace = 3.5f;

        [Header("Through ball - strong prediction")]
        public float throughLeadFactor = 1.25f;
        public float throughAssumedSpeed = 4.5f;
        public float throughMinLead = 5f;
        public float throughMaxLead = 14f;
        public float throughSpeedMin = 16f;
        public float throughSpeedMax = 21f;
        public float throughArrivePace = 6f;
        [Tooltip("Fraction of the ball's pace you must be running at to take a through ball in stride.")]
        public float throughCatchFraction = 0.40f;

        [Header("Planted (LeftCtrl)")]
        public float holdAssumedSpeed = 3.4f;

        [Header("Stats (0..1) - drive error far more than power")]
        [Range(0f, 1f)] public float passerPassing = 0.70f;
        [Range(0f, 1f)] public float playerPassing = 0.70f;
        [Range(0f, 1f)] public float playerCrossing = 0.70f;
        [Range(0f, 1f)] public float playerShooting = 0.70f;
        [Range(0f, 1f)] public float playerStrength = 0.60f;

        [Header("Carry")]
        [Tooltip("Hold it this long and the repetition is a success.")]
        public float carrySeconds = 6f;

        [Header("Tuning")]
        public float tightGap = 1.5f;
        public float mediumGap = 3.2f;
        public float freshInfoSeconds = 1.6f;
        public float passBuffer = 0.35f;
        public float touchRadius = 1.25f;
        public float layoffSpeed = 13f;
        public float crossSpeed = 18f;
        public float shotSpeed = 22f;

        enum Phase { Setup, Wait, Flight, Carry, Result }

        Phase phase;
        float phaseT;
        float waitDur;

        PassType passType = PassType.ToFeet;
        float passSpeed = 14f;
        Vector3 passTarget;
        Vector3 runDir = Vector3.zero;
        float leadUsed;
        bool inStride;

        // what he knew at the moment he took the touch
        bool freshAtTouch;
        float gapAtTouch;

        float exposureAtTackle;
        float lastAimErr, lastSpeedErr;
        string lastStrike = "-";

        string flash = "";
        float flashUntil = -1f;

        Vector3 previewFeet, previewThrough;
        float previewFeetLead, previewThroughLead;

        int rounds, kept, tackled, fouls;
        int freshTry, freshOk, staleTry, staleOk;

        string title = "";
        string body = "";
        Color titleCol = Color.white;

        LineRenderer feetLine, feetRing, throughLine, throughRing, receiveGuide, aimRing, aimLine;
        GUIStyle sBig, sMid, sSmall;
        Texture2D texWhite;

        static readonly Color FeetCol = new Color(1f, 1f, 1f, 1f);
        static readonly Color ThroughCol = new Color(1f, 0.78f, 0.35f, 1f);
        static readonly Color AimCol = new Color(0.55f, 1f, 0.75f, 1f);

        // ------------------------------------------------------------- setup ----

        void Start()
        {
            Material dim = ProtoMat.UnlitFade(new Color(1f, 1f, 1f, 0.3f));
            feetLine = MakeLine("FeetLine", dim, 0.10f, 2);
            feetRing = MakeLine("FeetTarget", dim, 0.08f, 33);
            throughLine = MakeLine("ThroughLine", dim, 0.08f, 2);
            throughRing = MakeLine("ThroughTarget", dim, 0.07f, 33);
            receiveGuide = MakeLine("ReceiveGuide", dim, 0.07f, 2);
            aimRing = MakeLine("AimRing", dim, 0.06f, 25);
            aimLine = MakeLine("AimLine", dim, 0.04f, 2);

            phase = Phase.Setup;
            phaseT = 0f;
        }

        LineRenderer MakeLine(string name, Material m, float width, int points)
        {
            var go = new GameObject(name);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.sharedMaterial = m;
            lr.widthMultiplier = width;
            lr.positionCount = points;
            lr.numCapVertices = 0;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            return lr;
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
                case Phase.Carry: DoCarry(); break;
                case Phase.Result:
                    if (phaseT > 2.6f) { phase = Phase.Setup; phaseT = 0f; }
                    break;
            }

            if (aimRing != null) { aimRing.enabled = false; aimLine.enabled = false; }
        }

        // ------------------------------------------------------------ phases ----

        void DoSetup()
        {
            if (passer != null) passer.position = passerHome;   // he does not move. ever.

            if (player != null)
            {
                player.Teleport(startPos, 180f);
                player.EndReceive();
                player.TrackBall = true;
            }

            PressStyle a = (PressStyle)Random.Range(0, 3);
            PressStyle b = (PressStyle)Random.Range(0, 3);
            float lat = Random.Range(-1.3f, 1.3f);
            float switchDelay = Random.value < 0.66f ? Random.Range(0.7f, 2.1f) : -1f;

            if (defender != null)
            {
                defender.chase = null;
                defender.Warp(startPos + new Vector3(lat, 0f, Random.Range(3.5f, 7f)));
                defender.Begin(a, lat, switchDelay, b);
            }

            if (ball != null && passer != null) ball.Hold(passer.position);

            waitDur = Random.Range(1.8f, 3.6f);
            title = "";
            body = "";
            inStride = false;
            runDir = Vector3.zero;
            leadUsed = 0f;
            phase = Phase.Wait;
            phaseT = 0f;
        }

        void ReadCall(out Vector2 dirInput, out float speed)
        {
            if (player.Holding) { dirInput = player.CallDir; speed = holdAssumedSpeed; }
            else { dirInput = player.InputDir; speed = player.Speed; }
        }

        void DoWait()
        {
            if (player == null || passer == null) return;

            Vector3 pp = player.transform.position;
            Vector2 dirInput;
            float callSpeed;
            ReadCall(out dirInput, out callSpeed);

            Vector3 dirA, dirB;
            float v0;
            Weight(pp, dirInput, callSpeed, MidSpeed(PassType.ToFeet), PassType.ToFeet,
                   out dirA, out previewFeet, out v0);
            Weight(pp, dirInput, callSpeed, MidSpeed(PassType.Through), PassType.Through,
                   out dirB, out previewThrough, out v0);

            runDir = dirA != Vector3.zero ? dirA : dirB;
            previewFeetLead = Vector3.Distance(pp, previewFeet);
            previewThroughLead = Vector3.Distance(pp, previewThrough);

            DrawPreview();

            bool wantThrough = Time.time - player.LastThroughPress < 0.06f;
            bool wantFeet = Time.time - player.LastPassPress < 0.06f;
            if (phaseT >= waitDur || wantFeet || wantThrough)
                Launch(wantThrough ? PassType.Through : PassType.ToFeet);
        }

        float MidSpeed(PassType t)
        {
            return t == PassType.Through ? (throughSpeedMin + throughSpeedMax) * 0.5f
                                         : (feetSpeedMin + feetSpeedMax) * 0.5f;
        }

        float RollSpeed(PassType t)
        {
            return t == PassType.Through ? Random.Range(throughSpeedMin, throughSpeedMax)
                                         : Random.Range(feetSpeedMin, feetSpeedMax);
        }

        void Weight(Vector3 pp, Vector2 input, float speed, float baseSpeed, PassType type,
                    out Vector3 dir, out Vector3 target, out float v0)
        {
            dir = input.sqrMagnitude > 0.09f
                ? new Vector3(input.x, 0f, input.y).normalized
                : Vector3.zero;

            target = pp;
            float decel = ball != null ? ball.rollDecel : 5f;
            bool through = type == PassType.Through;
            float pace = through ? throughArrivePace : feetArrivePace;
            v0 = Mathf.Max(baseSpeed, Ball.SpeedToReach(Vector3.Distance(passer.position, pp), pace, decel));

            if (dir == Vector3.zero)
            {
                if (!through) return;

                Vector2 bf = player.BodyForward;
                dir = new Vector3(bf.x, 0f, bf.y);
                if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
                dir.Normalize();
            }

            float assumed = through ? Mathf.Max(speed, throughAssumedSpeed) : speed;
            float factor = through ? throughLeadFactor : feetLeadFactor;
            float lo = through ? throughMinLead : 0f;
            float hi = through ? throughMaxLead : feetMaxLead;

            for (int i = 0; i < 3; i++)
            {
                float D = Vector3.Distance(passer.position, target);
                v0 = Mathf.Max(baseSpeed, Ball.SpeedToReach(D, pace, decel));

                float T = Ball.TravelTime(D, v0, decel);
                if (float.IsInfinity(T) || T > 5f) T = 5f;

                float lead = Mathf.Clamp(assumed * factor * T, lo, hi);
                target = pp + dir * lead;
                target.x = Mathf.Clamp(target.x, -pitchHalfX, pitchHalfX);
                target.z = Mathf.Clamp(target.z, -pitchHalfZ, pitchHalfZ);
            }
        }

        void Launch(PassType type)
        {
            Vector3 pp = player.transform.position;
            passType = type;

            Vector2 dirInput;
            float callSpeed;
            ReadCall(out dirInput, out callSpeed);

            Vector3 dir, target;
            float v0;
            Weight(pp, dirInput, callSpeed, RollSpeed(type), type, out dir, out target, out v0);

            runDir = dir;
            passTarget = target;
            leadUsed = Vector3.Distance(pp, target);

            float diff = BallModel.Difficulty(0f, 0f, false, false);
            var strike = BallModel.Resolve(passer.position, target, 0f, v0, passerPassing, diff);
            lastAimErr = strike.aimErrorM;
            lastSpeedErr = strike.speedErrorPct;
            lastStrike = "패서 배급";
            passSpeed = strike.speed;
            ball.Launch(passer.position, strike.direction, strike.speed);

            player.BeginReceive();
            phase = Phase.Flight;
            phaseT = 0f;
        }

        void DoFlight()
        {
            if (ball == null || player == null) return;

            player.UpdateReceive(ball.transform.position, ball.Velocity, ball.InFlight);
            DrawLive();

            Vector3 bp = ball.transform.position; bp.y = 0f;
            Vector3 pp = player.transform.position; pp.y = 0f;

            if (Vector3.Distance(bp, pp) < touchRadius && CanTouch())
            {
                inStride = ball.InFlight;
                TakeTouch();
                return;
            }

            if (phaseT > 10f)
            {
                rounds++;
                EndRound();
                Finish("STALLED", "공에 닿지 못했습니다.", new Color(0.95f, 0.6f, 0.25f));
            }
        }

        bool CanTouch()
        {
            if (passType == PassType.ToFeet) return true;
            if (!ball.InFlight) return true;

            Vector3 bv = ball.Velocity; bv.y = 0f;
            if (bv.sqrMagnitude < 0.01f) return true;

            Vector3 pv = player.Velocity; pv.y = 0f;
            return Vector3.Dot(pv, bv.normalized) >= bv.magnitude * throughCatchFraction;
        }

        // ------------------------------------------------------------- touch ----

        /// <summary>
        /// He has reached the ball. Either he strikes it first time, or he takes it
        /// into his stride and we move into the carry.
        /// </summary>
        void TakeTouch()
        {
            gapAtTouch = defender != null ? defender.Gap(player.transform.position) : 99f;
            float age = defenderPerc != null ? defenderPerc.InfoAge : 999f;
            freshAtTouch = age < freshInfoSeconds;

            if (Time.time - player.LastShootPress < passBuffer) { Shoot(true); return; }
            if (Time.time - player.LastCrossPress < passBuffer) { Cross(true); return; }
            if (Time.time - player.LastPassPress < passBuffer
                || Time.time - player.LastThroughPress < passBuffer) { Layoff(true); return; }

            EnterCarry();
        }

        void EnterCarry()
        {
            ball.Attach(player);
            player.EndReceive();
            player.TrackBall = false;      // head up, not staring at his own feet
            if (defender != null) defender.chase = ball.transform;

            receiveGuide.enabled = false;
            feetLine.enabled = false;
            feetRing.enabled = false;

            phase = Phase.Carry;
            phaseT = 0f;
        }

        void DoCarry()
        {
            if (ball == null || player == null) return;

            if (Time.time - player.LastShootPress < passBuffer) { Shoot(false); return; }
            if (Time.time - player.LastCrossPress < passBuffer) { Cross(false); return; }
            if (Time.time - player.LastPassPress < passBuffer
                || Time.time - player.LastThroughPress < passBuffer) { Layoff(false); return; }

            if (defender != null && defender.WantsTackle(ball.transform.position, ball.Exposure))
            {
                defender.BeganTackle();
                exposureAtTackle = ball.Exposure;

                Tackle.Input ti;
                ti.defenderPos = defender.transform.position;
                ti.ballPos = ball.transform.position;
                ti.attackerPos = player.transform.position;
                ti.attackerFacing = player.BodyForward;
                // Moving or actively shielding counts. Standing still does not.
                ti.attackerResisting = player.InputDir.sqrMagnitude > 0.09f || player.Shielding;
                ti.tackling01 = defender.tackling01;
                ti.strength01 = playerStrength;
                ti.reach = Tackle.Reach;

                string why;
                bool shielded;
                TackleResult r = Tackle.Resolve(ti, out why, out shielded);

                if (r == TackleResult.Won)
                {
                    Vector3 away = ball.transform.position - defender.transform.position;
                    away.y = 0f;
                    ball.Release(away.sqrMagnitude > 0.01f ? away : Vector3.forward, 7f);
                    rounds++; tackled++;
                    Bucket(false);
                    EndRound();
                    Finish("TACKLED", string.Format("{0}\n내 터치 거리 {1:0.00}m — 공이 그만큼 몸에서 떨어져 있었습니다\n{2}",
                        why, exposureAtTackle,
                        exposureAtTackle > 0.9f
                            ? "터치가 길면 수비수가 공까지 닿기 쉬워집니다."
                            : "사이에 몸을 넣었어야 합니다."),
                        new Color(0.95f, 0.35f, 0.35f));
                    return;
                }

                if (r == TackleResult.Foul)
                {
                    ball.Release(player.BodyForward.x * Vector3.right + player.BodyForward.y * Vector3.forward, 1.5f);
                    rounds++; fouls++; kept++;
                    Bucket(true);
                    EndRound();
                    Finish("FREE KICK", why + "\n몸으로 버티다 얻어낸 파울입니다.",
                           new Color(0.55f, 0.85f, 1f));
                    return;
                }

                if (r == TackleResult.Lost || r == TackleResult.OutOfRange)
                {
                    // He committed and got nothing either way.
                    defender.MissedTackle();
                    Flash("태클 실패 — " + why);
                }
            }

            if (phaseT > carrySeconds)
            {
                rounds++; kept++;
                Bucket(true);
                EndRound();
                Finish("KEPT IT", string.Format("{0:0.0}초 동안 지켜냈습니다.\n평균 터치 거리를 짧게 유지하면 뺏기지 않습니다.", carrySeconds),
                       new Color(0.45f, 0.9f, 0.5f));
            }
        }

        // ------------------------------------------------------------ strikes ---

        float PressureOf(float gap)
        {
            return Mathf.Clamp01(Mathf.InverseLerp(mediumGap, tightGap, gap));
        }

        void Shoot(bool firstTime)
        {
            Vector3 pp = player.transform.position;
            float gap = defender != null ? defender.Gap(pp) : 99f;
            var sh = BallModel.Resolve(pp, new Vector3(0f, 0f, goalZ), BallModel.Base.Shot, 0f,
                                       playerShooting,
                                       BallModel.Difficulty(PressureOf(gap), 0f, firstTime, false));
            lastAimErr = sh.aimErrorM; lastSpeedErr = sh.speedErrorPct; lastStrike = "슛";
            ball.Release(sh.direction, sh.speed);

            float miss = Mathf.Abs(sh.aimErrorM);
            bool onTarget = miss < 3.66f;
            Score(onTarget, firstTime ? "첫 터치 슛" : "슛",
                  onTarget ? string.Format("골문 안으로 갔습니다. 조준 오차 {0:0.00}m", miss)
                           : string.Format("빗나갔습니다. 조준 오차 {0:0.00}m", miss));
        }

        void Cross(bool firstTime)
        {
            Vector3 pp = player.transform.position;
            float gap = defender != null ? defender.Gap(pp) : 99f;
            float side = Mathf.Abs(pp.x) < 0.1f ? 1f : Mathf.Sign(pp.x);
            Vector3 farPost = new Vector3(-side * 5.5f, 0f, goalZ - 3.5f);
            var cr = BallModel.Resolve(pp, farPost, BallModel.Base.Cross, 0f,
                                       playerCrossing,
                                       BallModel.Difficulty(PressureOf(gap), 0f, firstTime, false));
            lastAimErr = cr.aimErrorM; lastSpeedErr = cr.speedErrorPct; lastStrike = "크로스";
            ball.Release(cr.direction, cr.speed);

            bool good = Mathf.Abs(cr.aimErrorM) < 4f;
            Score(good, firstTime ? "첫 터치 크로스" : "크로스",
                  good ? "박스 안으로 감아 올렸습니다." : "너무 길게 넘어갔습니다.");
        }

        void Layoff(bool firstTime)
        {
            Vector3 pp = player.transform.position;
            float gap = defender != null ? defender.Gap(pp) : 99f;
            Vector2 aim = player.BodyForward;
            Vector3 lay = pp + new Vector3(aim.x, 0f, aim.y) * 12f;
            var lo = BallModel.Resolve(pp, lay, BallModel.Base.ShortPass, 0f,
                                       playerPassing,
                                       BallModel.Difficulty(PressureOf(gap), 0f, firstTime, false));
            lastAimErr = lo.aimErrorM; lastSpeedErr = lo.speedErrorPct; lastStrike = "패스";
            ball.Release(lo.direction, lo.speed);

            bool good = Mathf.Abs(lo.aimErrorM) < 2f;
            Score(good, firstTime ? "원터치 레이오프" : "패스",
                  good ? "정확하게 연결했습니다." : "받는 선수에게서 벗어났습니다.");
        }

        void Score(bool good, string what, string why)
        {
            rounds++;
            if (good) kept++;
            Bucket(good);
            EndRound();
            Finish(good ? "KEPT IT" : "GAVE IT AWAY",
                   string.Format("{0}\n수비수 {1:0.0}m · {2}\n{3}", what, gapAtTouch,
                                 freshAtTouch ? "터치 전에 확인함" : "확인하지 못함", why),
                   good ? new Color(0.45f, 0.9f, 0.5f) : new Color(0.95f, 0.35f, 0.35f));
        }

        void Bucket(bool ok)
        {
            if (freshAtTouch) { freshTry++; if (ok) freshOk++; }
            else { staleTry++; if (ok) staleOk++; }
        }

        void EndRound()
        {
            player.EndReceive();
            player.TrackBall = true;
            if (defender != null) defender.chase = null;
            feetLine.enabled = false;
            feetRing.enabled = false;
            throughLine.enabled = false;
            throughRing.enabled = false;
            receiveGuide.enabled = false;
        }

        void Finish(string t, string b, Color c)
        {
            title = t;
            body = b;
            titleCol = c;
            phase = Phase.Result;
            phaseT = 0f;
        }

        void Flash(string s)
        {
            flash = s;
            flashUntil = Time.time + 1.2f;
        }

        // ------------------------------------------------------------ guides ----

        void SetLine(LineRenderer lr, Vector3 a, Vector3 b, Color c)
        {
            lr.enabled = true;
            lr.positionCount = 2;
            lr.SetPosition(0, Flat(a));
            lr.SetPosition(1, Flat(b));
            lr.startColor = c;
            lr.endColor = c;
        }

        void SetRing(LineRenderer lr, Vector3 centre, float radius, Color c, int segs)
        {
            lr.enabled = true;
            lr.positionCount = segs + 1;
            for (int i = 0; i <= segs; i++)
            {
                float a = (float)i / segs * Mathf.PI * 2f;
                lr.SetPosition(i, Flat(centre) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
            }
            lr.startColor = c;
            lr.endColor = c;
        }

        void DrawPreview()
        {
            Color f = FeetCol; f.a = 0.26f;
            SetLine(feetLine, passer.position, previewFeet, f);
            SetRing(feetRing, previewFeet, 0.75f, f, 32);

            Color t = ThroughCol; t.a = 0.20f;
            SetLine(throughLine, passer.position, previewThrough, t);
            SetRing(throughRing, previewThrough, 0.95f, t, 32);

            receiveGuide.enabled = false;
        }

        void DrawLive()
        {
            bool through = passType == PassType.Through;
            Color c = through ? ThroughCol : FeetCol;
            c.a = 0.42f;

            SetLine(feetLine, passer.position, passTarget, c);
            SetRing(feetRing, passTarget, through ? 0.95f : 0.75f, c, 32);
            throughLine.enabled = false;
            throughRing.enabled = false;

            Vector3 bp = Flat(ball.transform.position);
            Vector3 pp = Flat(player.transform.position);

            if (player.Mode == FootballerController.Receive.OnLine)
            {
                Vector3 d = ball.Velocity; d.y = 0f;
                if (d.sqrMagnitude < 0.01f) d = pp - bp;
                d.Normalize();
                SetLine(receiveGuide, bp - d * 2f, bp + d * 14f, new Color(0.55f, 0.85f, 1f, 0.4f));
            }
            else
            {
                SetLine(receiveGuide, pp, bp, new Color(1f, 0.55f, 0.3f, 0.45f));
            }
        }

        static Vector3 Flat(Vector3 v) { return new Vector3(v.x, 0.04f, v.z); }

        static string Arrow(Vector3 d)
        {
            if (d.sqrMagnitude < 0.01f) return "·";
            float a = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            int i = Mathf.RoundToInt(a / 45f);
            if (i < 0) i += 8;
            string[] arrows = { "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" };
            return arrows[i % 8];
        }

        static float Pct(int ok, int tries) { return tries > 0 ? 100f * ok / tries : 0f; }

        // --------------------------------------------------------------- HUD ----

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
            sBig.fontSize = 40;
            sBig.fontStyle = FontStyle.Bold;
            sBig.alignment = TextAnchor.MiddleCenter;

            sMid = new GUIStyle(GUI.skin.label);
            sMid.fontSize = 17;
            sMid.alignment = TextAnchor.MiddleCenter;
            sMid.wordWrap = true;

            sSmall = new GUIStyle(GUI.skin.label);
            sSmall.fontSize = 14;
            sSmall.wordWrap = true;

            if (f != null) { sBig.font = f; sMid.font = f; sSmall.font = f; }

            texWhite = new Texture2D(1, 1);
            texWhite.SetPixel(0, 0, Color.white);
            texWhite.Apply();
        }

        void OnGUI()
        {
            EnsureStyles();
            int w = Screen.width, h = Screen.height;

            if (defender != null && player != null)
            {
                float gap = defender.Gap(player.transform.position);
                if (gap < 1.2f)
                {
                    float a = Mathf.InverseLerp(1.2f, 0.6f, gap) * (0.35f + 0.25f * Mathf.Sin(Time.time * 14f));
                    GUI.color = new Color(1f, 0.15f, 0.15f, Mathf.Clamp01(a));
                    GUI.DrawTexture(new Rect(0, 0, w, 14), texWhite);
                    GUI.DrawTexture(new Rect(0, h - 14, w, 14), texWhite);
                    GUI.DrawTexture(new Rect(0, 0, 14, h), texWhite);
                    GUI.DrawTexture(new Rect(w - 14, 0, 14, h), texWhite);
                    GUI.color = Color.white;
                }
            }

            GUILayout.BeginArea(new Rect(14, 14, 348, 316), GUI.skin.box);
            GUILayout.Label(string.Format("라운드 {0}  ·  지킴 {1}  ·  뺏김 {2}  ·  파울 {3}",
                            rounds, kept, tackled, fouls), sSmall);
            GUILayout.Space(6);
            GUILayout.Label("── 확인이 결과를 바꾸는가 ──", sSmall);
            GUILayout.Label(string.Format("확인 후    {0}/{1}   ({2:0}%)", freshOk, freshTry, Pct(freshOk, freshTry)), sSmall);
            GUILayout.Label(string.Format("블라인드   {0}/{1}   ({2:0}%)", staleOk, staleTry, Pct(staleOk, staleTry)), sSmall);
            GUILayout.Space(6);
            if (phase == Phase.Carry && ball != null)
            {
                GUILayout.Label(string.Format("드리블 {0:0.0}s / {1:0}s   ·   내 터치 거리 {2:0.00}m",
                                phaseT, carrySeconds, ball.Exposure), sSmall);
                if (defender != null)
                {
                    float bg; bool inReach, shielded;
                    Tackle.Probe(defender.transform.position, ball.transform.position,
                                 player.transform.position, Tackle.Reach, out bg, out inReach, out shielded);
                    GUILayout.Label(string.Format("① 수비-공 {0:0.00}m  {1}",
                                    bg, inReach ? "◀ 사거리 안" : "안전"), sSmall);
                    GUILayout.Label(string.Format("② 사이 막힘  {0}{1}",
                                    shielded ? "예 — 경합" : "아니오 ◀ 뺏김",
                                    defender.Recovering ? "     [수비수 넘어짐]" : ""), sSmall);
                }
            }
            else if (phase == Phase.Wait)
            {
                GUILayout.Label(string.Format("요청 {0}{1}   ·   발밑 {2:0.0}m / 스루 {3:0.0}m",
                                Arrow(runDir), player != null && player.Holding ? " [고정]" : "",
                                previewFeetLead, previewThroughLead), sSmall);
            }
            else
            {
                GUILayout.Label(string.Format("{0}   ·   리드 {1:0.0}m   ·   볼 {2:0.0} m/s",
                                passType == PassType.Through ? "스루패스" : "발밑 패스",
                                leadUsed, ball != null ? ball.SpeedNow : 0f), sSmall);
            }
            GUILayout.Label(string.Format("{0}  오차 {1:+0.00;-0.00}m  ·  강약 {2:+0.0;-0.0}%",
                            lastStrike, lastAimErr, lastSpeedErr), sSmall);
            if (player != null)
                GUILayout.Label(string.Format("스캔 #{0}  {1}  각도 {2:0}°{3}",
                    player.ScanCount,
                    player.ScanPitchSide > 0 ? "오른쪽" : (player.ScanPitchSide < 0 ? "왼쪽" : "-"),
                    player.scanAngle, player.ScanClamped ? "  [라인 제한]" : ""), sSmall);
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(w - 316, 14, 302, 292), GUI.skin.box);
            GUILayout.Label("방향키 / 좌스틱    이동", sSmall);
            GUILayout.Label("W / Y (△)         스루패스", sSmall);
            GUILayout.Label("A / X (□)         크로스", sSmall);
            GUILayout.Label("S / A (✕)         패스 · 레이오프", sSmall);
            GUILayout.Label("D / B (○)         슛", sSmall);
            GUILayout.Label("Q                 어깨 너머 확인", sSmall);
            GUILayout.Label("E / LT            몸으로 지키기", sSmall);
            GUILayout.Label("Shift / RB        스프린트", sSmall);
            GUILayout.Label("LeftCtrl / LB     제자리 고정", sSmall);
            GUILayout.Label("R  리셋      Tab  시야제한 " + (PerceptionSystem.xrayDebug ? "OFF" : "ON"), sSmall);
            GUILayout.EndArea();

            string prompt = "";
            if (phase == Phase.Wait)
            {
                prompt = string.Format("S  발밑 {0:0.0}m      ·      W  스루 {1:0.0}m",
                                       previewFeetLead, previewThroughLead);
            }
            else if (phase == Phase.Flight && ball != null && player != null)
            {
                if (passType == PassType.Through && ball.InFlight && !CanTouch())
                    prompt = "스루패스 — 페이스를 올려 따라잡으세요 (Shift)";
                else if (player.Mode == FootballerController.Receive.OnLine)
                    prompt = "공이 몸쪽으로 옵니다   —   마중 나가거나, 흘리며 받으세요";
                else
                    prompt = "공이 앞 공간으로 갔습니다   —   자동으로 따라갑니다 (Shift로 가속)";
            }
            else if (phase == Phase.Carry)
            {
                prompt = ball.Exposure > 0.9f
                    ? "터치가 깁니다 — 속도를 줄이거나 몸을 넣으세요 (E)"
                    : "드리블 중 — S 패스 · D 슛 · A 크로스로 마무리";
            }

            if (Time.time < flashUntil) prompt = flash;

            if (prompt.Length > 0)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.9f);
                GUI.Label(new Rect(0, h - 84, w, 28), prompt, sMid);
                GUI.color = Color.white;
            }

            if (phase == Phase.Result)
            {
                GUI.color = titleCol;
                GUI.Label(new Rect(0, h - 232, w, 52), title, sBig);
                GUI.color = new Color(1f, 1f, 1f, 0.92f);
                GUI.Label(new Rect(w * 0.5f - 320, h - 176, 640, 100), body, sMid);
                GUI.color = Color.white;
            }
        }
    }
}
