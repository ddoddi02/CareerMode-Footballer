using System.Collections.Generic;
using System.Text;
using Prototype;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Career
{
    /// <summary>
    /// Shooting practice: one shooter, one keeper, a goal - for trying out shot power and
    /// finishing before they are wired into the match (user, 2026-10-14). Built by
    /// Tools > Career > Build Shot Drill Scene.
    ///
    /// Nothing in _Prototype is changed. The kick is the match's own (BallModel.Resolve with
    /// the speed passed in - its "required speed" path), the ball is the match's Ball, and
    /// the keeper is the match's GoalkeeperAI with alwaysLive on: he stands, judges, dives,
    /// lies down and gets up exactly as he does in a match. The drill only reads what he did
    /// (keeper.Reports / keeper.Last); the save probability on the HUD is GoalkeeperAI.Judge,
    /// the same closed form he decides with.
    ///
    /// The goal is at +Z. Manual: aim with the mouse, click to shoot. Auto: shot power x
    /// finishing grid, every spot in turn, aimed just inside a post, results to the HUD and
    /// the console.
    /// </summary>
    public class ShotDrill : MonoBehaviour
    {
        public enum FinishingMap
        {
            /// <summary>StatScale.To01: 60 -> 0.596. The one scale every attribute uses.</summary>
            Linear,
            /// <summary>60 -> 0.70, the match's present default shooting, then straight to 1 at 100.</summary>
            AverageIsMatchDefault,
        }

        public enum Outcome { Goal, Held, Parried, Wide, Short }

        [Header("The shooter - edit freely while it runs")]
        public PlayerProfile profile = new PlayerProfile();
        [Tooltip("Start with the character saved by the creation screen, if there is one (CharacterStore).")]
        public bool useSavedCharacter = true;
        [Tooltip("How finishing 1..100 becomes the match's 0..1 accuracy stat.")]
        public FinishingMap finishingMap = FinishingMap.Linear;

        [Header("Shot power -> m/s. PROVISIONAL, and the match side's to own (PROJECT.md 10.4)")]
        public float speedAtOne = 22.1f;
        [Tooltip("An ordinary player - shot power `averagePower` - strikes it at the match's present shot speed.")]
        public float speedAtAverage = 26f;
        public float averagePower = 60f;
        [Tooltip("From 40 m out, at the goal in a second, on the 5 m/s^2 rolling ball (PROJECT.md 3.6).")]
        public float speedAtHundred = 42.5f;
        [Tooltip("Past 100 (traits only) the line carries on rather than stopping at the top speed. PROVISIONAL - asked.")]
        public bool extendPastHundred = true;

        [Header("Refs - set by the builder")]
        public Ball ball;
        public GoalkeeperAI keeper;
        public Transform shooter;
        public LineRenderer aimLine;
        public Camera cam;

        [Header("Where he shoots from: x across, metres out from the goal line")]
        public Vector2[] spots =
        {
            new Vector2(0f, 11f), new Vector2(0f, 18f), new Vector2(-10f, 17f),
            new Vector2(10f, 17f), new Vector2(0f, 25f), new Vector2(0f, 40f),
        };

        [Header("Auto mode")]
        public int[] autoPowers = { 40, 60, 80, 100 };
        public int[] autoFinishing = { 40, 60, 85 };
        [Tooltip("Shots per power x finishing pair, the spots taken in turn.")]
        public int shotsPerPair = 12;
        public float autoTimeScale = 6f;
        [Tooltip("Auto aims this far inside a post, the side picked at random.")]
        public float cornerInset = 0.6f;
        [Tooltip("Seconds (game time) to look at the result before the next ball. A diving keeper stays down for his getUp.")]
        public float pauseAfter = 1.2f;

        // ------------------------------------------------------------- state --
        enum Phase { Ready, Flying, Done }
        Phase phase = Phase.Ready;
        int spot;
        float doneAt;

        // the shot in the air
        Vector3 shotFrom, shotDir;
        GoalkeeperAI.Verdict verdict;
        int keeperReports;

        struct ShotLog
        {
            public float power, finishing, accuracy01, speed, timeToLine, goalChance, onTargetShare;
            public bool onTarget;
            public Outcome outcome;
        }

        ShotLog last;
        bool hasLast;
        readonly int[] tally = new int[5];
        int shots;

        // auto
        bool auto;
        int pairIndex, pairShot;
        float savedTimeScale = 1f;
        readonly List<ShotLog> pairLogs = new List<ShotLog>();
        readonly List<string> results = new List<string>();
        float autoPower = -1f, autoFinish = -1f;

        static float GoalZ { get { return TacticalPitch.HalfL; } }

        bool fromSave;

        void Start()
        {
            if (cam == null) cam = Camera.main;
            PlayerProfile saved;
            if (useSavedCharacter && CharacterStore.TryLoad(out saved)) { profile = saved; fromSave = true; }
            ResetTo(0);
        }

        void OnDisable()
        {
            if (auto) StopAuto(false);
        }

        // ------------------------------------------------------ the numbers --

        float Power { get { return autoPower >= 0f ? autoPower : ShotPower.Attribute(profile); } }
        float Finishing { get { return autoFinish >= 0f ? autoFinish : profile.finishing; } }

        /// <summary>Shot power -> ball speed. Two straight lines meeting at the ordinary player.</summary>
        public float SpeedFor(float power)
        {
            return ShotPower.ProvisionalSpeed(power, speedAtOne, speedAtAverage, averagePower, speedAtHundred, extendPastHundred);
        }

        public float Accuracy01(float finishing)
        {
            if (finishingMap == FinishingMap.Linear) return StatScale.To01(finishing);
            const float avg = 60f, avg01 = 0.70f;
            if (finishing <= avg) return avg01 * Mathf.InverseLerp(StatScale.Min, avg, finishing);
            return Mathf.Lerp(avg01, 1f, Mathf.InverseLerp(avg, StatScale.Max, finishing));
        }

        Vector3 SpotPos(int i)
        {
            Vector2 s = spots[Mathf.Clamp(i, 0, spots.Length - 1)];
            return new Vector3(s.x, 0f, GoalZ - s.y);
        }

        // ------------------------------------------------------------ loop --

        void Update()
        {
            profile.traits = BodyModel.NextTraits(profile.traits, profile.BodyFatPercent, profile.MusclePercent);

            switch (phase)
            {
                case Phase.Ready:
                    if (auto) AutoShot();
                    else Manual();
                    break;
                case Phase.Flying:
                    Fly();
                    break;
                case Phase.Done:
                    if (ball.transform.position.z > GoalZ + 1.5f) ball.Stop();   // the net
                    if (Time.time >= doneAt + pauseAfter) Next();
                    break;
            }
            Keys();
        }

        void Keys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.aKey.wasPressedThisFrame) { if (auto) StopAuto(true); else StartAuto(); }
            if (auto) return;
            for (int i = 0; i < spots.Length && i < 9; i++)
                if (kb[Key.Digit1 + i].wasPressedThisFrame) ResetTo(i);
            if (kb.spaceKey.wasPressedThisFrame) ResetTo((spot + 1) % spots.Length);
            if (kb.rKey.wasPressedThisFrame) { System.Array.Clear(tally, 0, tally.Length); shots = 0; hasLast = false; }
            if (kb.fKey.wasPressedThisFrame)
                finishingMap = finishingMap == FinishingMap.Linear ? FinishingMap.AverageIsMatchDefault : FinishingMap.Linear;
        }

        void Manual()
        {
            Vector3 aim;
            bool has = MouseAim(out aim);
            if (aimLine != null)
            {
                aimLine.enabled = has;
                if (has)
                {
                    aimLine.SetPosition(0, ball.transform.position);
                    aimLine.SetPosition(1, new Vector3(aim.x, 0.05f, aim.z));
                }
            }
            var mouse = Mouse.current;
            if (has && mouse != null && mouse.leftButton.wasPressedThisFrame) Strike(aim);
        }

        /// <summary>Where the mouse points on the grass, carried on to the goal line.</summary>
        bool MouseAim(out Vector3 aim)
        {
            aim = Vector3.zero;
            var mouse = Mouse.current;
            if (cam == null || mouse == null) return false;
            Ray r = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (r.direction.y > -1e-4f) return false;
            Vector3 hit = r.origin + r.direction * (-r.origin.y / r.direction.y);
            Vector3 from = Flat(ball.transform.position);
            Vector3 d = Flat(hit - from);
            if (d.sqrMagnitude < 0.01f) return false;
            d.Normalize();
            aim = d.z > 0.05f ? from + d * ((GoalZ - from.z) / d.z) : from + d * 30f;
            return true;
        }

        // ---------------------------------------------------------- strike --

        void Strike(Vector3 aim)
        {
            Vector3 from = Flat(ball.transform.position);
            float power = Power;
            float v0 = SpeedFor(power);
            float acc = Accuracy01(Finishing);
            float diff = BallModel.Difficulty(0f, 0f, false, false);
            float spread = BallModel.AimSpread(acc, Flat(aim - from).magnitude, diff);
            Vector3 k = Flat(keeper.transform.position);
            GoalkeeperAI.Model m = keeper.Params;

            ShotLog log = new ShotLog();
            log.power = power;
            log.finishing = Finishing;
            log.accuracy01 = acc;
            log.goalChance = GoalkeeperAI.GoalChance(from, aim, v0, ball.rollDecel, spread, k, true, m, 0f,
                                                     out log.onTargetShare);

            BallModel.Strike st = BallModel.Resolve(from, aim, 0f, v0, acc, diff);
            // The keeper tells one strike from the next by where it was struck from
            // (Ball.LaunchOrigin), so a second ball from the same spot would look like the first
            // and he would not move. A millimetre either way keeps every strike new.
            Vector3 launch = from + new Vector3(Random.Range(-1e-3f, 1e-3f), 0f, Random.Range(-1e-3f, 1e-3f));
            ball.Launch(launch, st.direction, st.speed);
            shotFrom = from;
            shotDir = Flat(st.direction).normalized;
            log.speed = st.speed;
            float toLine = shotDir.z > 1e-3f ? (GoalZ - from.z) / shotDir.z : Mathf.Infinity;
            log.timeToLine = Ball.TravelTime(toLine, st.speed, ball.rollDecel);

            // For the HUD only: the same verdict he is about to reach himself. He rolls his own dice.
            GoalkeeperAI.Line line;
            line.from = from;
            line.dir = shotDir;
            line.speed = st.speed;
            line.decel = ball.rollDecel;
            verdict = GoalkeeperAI.Judge(line, k, true, m, 0f);
            log.onTarget = verdict.onTarget;

            keeperReports = keeper.Reports;
            last = log;
            hasLast = false;
            phase = Phase.Flying;
            if (aimLine != null) aimLine.enabled = false;
        }

        void Fly()
        {
            // He went for it and it is settled: saved (held or pushed away), or past him.
            if (keeper.Reports != keeperReports)
            {
                keeperReports = keeper.Reports;
                GoalkeeperAI.Report r = keeper.Last;
                if (r.saved) { Finish(r.held ? Outcome.Held : Outcome.Parried); return; }
            }

            if (ball.transform.position.z >= GoalZ)
            {
                float lineX = shotFrom.x + shotDir.x * ((GoalZ - shotFrom.z) / Mathf.Max(shotDir.z, 1e-3f));
                Finish(Mathf.Abs(lineX) < TacticalPitch.GoalHalfW ? Outcome.Goal : Outcome.Wide);
                return;
            }
            if (!ball.InFlight) Finish(Outcome.Short);
        }

        void Finish(Outcome o)
        {
            last.outcome = o;
            hasLast = true;
            tally[(int)o]++;
            shots++;
            if (auto) pairLogs.Add(last);
            phase = Phase.Done;
            doneAt = Time.time;
        }

        void Next()
        {
            if (auto) { AdvanceAuto(); return; }
            ResetTo(spot);
        }

        void ResetTo(int i)
        {
            spot = Mathf.Clamp(i, 0, spots.Length - 1);
            Vector3 p = SpotPos(spot);
            ball.Hold(p);
            Vector3 toGoal = Flat(new Vector3(0f, 0f, GoalZ) - p).normalized;
            if (shooter != null)
            {
                shooter.position = p - toGoal * 0.55f;
                shooter.rotation = Quaternion.LookRotation(toGoal, Vector3.up);
            }
            keeper.Warp(keeper.SetPoint());
            phase = Phase.Ready;
        }

        // ------------------------------------------------------------ auto --

        void StartAuto()
        {
            auto = true;
            pairIndex = 0;
            pairShot = 0;
            pairLogs.Clear();
            results.Clear();
            savedTimeScale = Time.timeScale;
            Time.timeScale = autoTimeScale;
            SetPair();
            ResetTo(0);
        }

        void StopAuto(bool reset)
        {
            auto = false;
            autoPower = autoFinish = -1f;
            Time.timeScale = savedTimeScale;
            if (reset) ResetTo(spot);
        }

        public bool AutoRunning { get { return auto; } }

        void SetPair()
        {
            autoPower = autoPowers[pairIndex / autoFinishing.Length];
            autoFinish = autoFinishing[pairIndex % autoFinishing.Length];
        }

        void AutoShot()
        {
            float side = Random.value < 0.5f ? -1f : 1f;
            Strike(new Vector3(side * (TacticalPitch.GoalHalfW - cornerInset), 0f, GoalZ));
        }

        void AdvanceAuto()
        {
            pairShot++;
            if (pairShot >= shotsPerPair)
            {
                results.Add(Summarise());
                pairLogs.Clear();
                pairShot = 0;
                pairIndex++;
                if (pairIndex >= autoPowers.Length * autoFinishing.Length)
                {
                    var sb = new StringBuilder("[ShotDrill] auto results (" + shotsPerPair + " shots per pair, spots in turn, aimed "
                                               + cornerInset + " m inside a post)\n");
                    sb.AppendLine("power | finishing (acc01) | v0 m/s | 40m s | goal / held / parried / wide / short | expected goal | on target");
                    foreach (string r in results) sb.AppendLine(r);
                    Debug.Log(sb.ToString());
                    StopAuto(true);
                    return;
                }
                SetPair();
            }
            ResetTo(pairShot % spots.Length);
        }

        string Summarise()
        {
            int[] c = new int[5];
            float exp = 0f, onT = 0f;
            foreach (var l in pairLogs) { c[(int)l.outcome]++; exp += l.goalChance; onT += l.onTarget ? 1f : 0f; }
            int n = Mathf.Max(pairLogs.Count, 1);
            float v0 = SpeedFor(autoPower);
            return string.Format("{0,3} | {1,3} ({2:0.00}) | {3:0.0} | {4:0.00} | {5} / {6} / {7} / {8} / {9} | {10:0%} | {11:0%}",
                autoPower, autoFinish, Accuracy01(autoFinish), v0, Ball.TravelTime(40f, v0, ball.rollDecel),
                c[0], c[1], c[2], c[3], c[4], exp / n, onT / n);
        }

        // ------------------------------------------------------------- HUD --

        static readonly string[] OutcomeName = { "골", "선방 (잡기)", "선방 (쳐내기)", "빗나감", "못 미침" };
        GUIStyle sMid, sSmall;

        void OnGUI()
        {
            if (sMid == null)
            {
                sMid = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };
                sSmall = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
            }

            ShotPower.Parts sp = ShotPower.Explain(profile);
            float power = Power;
            float v0 = SpeedFor(power);
            Vector2 s = spots[spot];
            float dist = new Vector2(s.x, s.y).magnitude;

            GUILayout.BeginArea(new Rect(14, 14, 380, 360), GUI.skin.box);
            GUILayout.Label(string.Format("<b>슛 훈련</b> — {0}{1} · 재능 {2} · 오버롤 {3:0}", profile.displayName, fromSave ? " (생성한 선수)" : "",
                TalentCatalog.Get(profile.talent).name, CreationRules.Overall(profile)), sMid);
            if (profile.perks != null && profile.perks.Count > 0)
            {
                var names = new List<string>();
                foreach (string id in profile.perks) { Perk pk = PerkCatalog.Get(id); if (pk != null) names.Add(pk.name); }
                GUILayout.Label("특전: " + string.Join(", ", names.ToArray()), sSmall);
            }
            GUILayout.Label(string.Format("몸무게 {0:0.0} kg · 체지방 {1:0.0}% · 근육 {2:0.0} kg ({3:0.0}%)",
                profile.WeightKg, profile.BodyFatPercent, profile.muscleKg, profile.MusclePercent), sSmall);
            GUILayout.Label(string.Format("유연성 {0} · 마무리 {1} · 특성 {2}", profile.flexibility, profile.finishing,
                profile.traits == BodyTrait.None ? "없음" : profile.traits.ToString()), sSmall);
            GUILayout.Space(4);
            GUILayout.Label(string.Format("슛 파워 = 기본 {0:0} + 근육 {1:+0.0;-0.0;0} + 유연성 {2:+0.0;-0.0;0} → {3:0.0}{4}",
                sp.baseValue, sp.muscle, sp.flexibility, sp.capped,
                sp.traits != 0f ? string.Format(" + 특전 {0:+0;-0} = {1:0.0}", sp.traits, sp.total) : ""), sSmall);
            if (auto) GUILayout.Label(string.Format("<color=#ffd24a>자동: 슛 파워 {0:0}, 마무리 {1:0}으로 덮어씀</color>", autoPower, autoFinish), sSmall);
            GUILayout.Label(string.Format("<b>{0:0.0} m/s</b> ({1:0} km/h) · 40m 도달 {2:0.00}s", v0, v0 * 3.6f,
                Ball.TravelTime(40f, v0, ball.rollDecel)), sMid);
            float acc = Accuracy01(Finishing);
            GUILayout.Label(string.Format("정확도 {0:0.000} ({1}) · 이 자리 오차 반경 {2:0.00} m",
                acc, finishingMap == FinishingMap.Linear ? "1~100 직선" : "60→0.70", BallModel.AimSpread(acc, dist, 1f)), sSmall);
            GUILayout.Space(4);
            GUILayout.Label(string.Format("자리 {0}/{1}: 골라인에서 {2:0} m, 옆으로 {3:+0;-0;0} m", spot + 1, spots.Length, s.y, s.x), sSmall);
            GUILayout.Label("마우스 조준 · 클릭 슛 · 1~" + spots.Length + " 자리 · Space 다음 자리 · F 정확도 변환 · R 집계 초기화 · A 자동", sSmall);
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(Screen.width - 414, 14, 400, auto || results.Count > 0 ? 470 : 230), GUI.skin.box);
            if (hasLast)
            {
                GUILayout.Label(string.Format("<b>{0}</b>", OutcomeName[(int)last.outcome]), sMid);
                GUILayout.Label(string.Format("{0:0.0} m/s · 골라인까지 {1}", last.speed,
                    float.IsInfinity(last.timeToLine) ? "못 감" : last.timeToLine.ToString("0.00") + "s"), sSmall);
                GUILayout.Label(string.Format("포스트 안 {0} · 키퍼 {1}{2}", last.onTarget ? "예" : "아니오",
                    !verdict.his ? "안 감" : verdict.reachable ? "닿음" : "못 닿음",
                    verdict.his && verdict.reachable ? string.Format(" (막을 확률 {0:0%})", verdict.save) : ""), sSmall);
                GUILayout.Label(string.Format("차기 전 예상: 골 {0:0%} · 포스트 안 {1:0%}", last.goalChance, last.onTargetShare), sSmall);
            }
            GUILayout.Label(string.Format("{0}개: 골 {1} · 잡기 {2} · 쳐내기 {3} · 빗나감 {4} · 못 미침 {5}",
                shots, tally[0], tally[1], tally[2], tally[3], tally[4]), sSmall);
            if (auto)
                GUILayout.Label(string.Format("자동 {0}/{1} 조합, {2}/{3}개", pairIndex + 1,
                    autoPowers.Length * autoFinishing.Length, pairShot + 1, shotsPerPair), sSmall);
            if (results.Count > 0)
            {
                GUILayout.Label("파워 | 마무리 | m/s | 40m | 골/잡기/쳐내기/빗나감/못미침 | 예상 | 포스트 안", sSmall);
                foreach (string r in results) GUILayout.Label(r, sSmall);
            }
            GUILayout.EndArea();
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
