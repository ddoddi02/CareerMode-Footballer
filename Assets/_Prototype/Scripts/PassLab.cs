using System.Collections.Generic;
using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// A bench for the pass selector, and nothing else.
    ///
    /// The complaint it exists to answer is "the ball gets played straight through a
    /// defender". In a live match that sentence has at least four possible meanings and
    /// the picture moves too fast to tell them apart:
    ///
    ///   1  the selector scored the lane as open when a man was standing in it
    ///      - the geometry is wrong
    ///   2  the lane WAS open when it was chosen, and shut during the 0.9 s the passer
    ///      spent turning onto it (TeamAttack.maxTurnWait) - the decision is stale, not wrong
    ///   3  the lane was open and the STRIKE missed it, because the weight and direction
    ///      error (PROJECT.md 3.8) sprayed the ball off the corridor that was measured
    ///   4  every lane was shut, so it went long and lofted - which is allowed to cross
    ///      bodies, because height is what makes it legal (PROJECT.md 3.17)
    ///
    /// So: freeze everybody, and pass. Nothing moves, so nothing can go stale (2). The
    /// strike is exact by default, so the ball travels down the corridor that was
    /// actually measured (3). What is left on the screen is the selector's own judgement,
    /// next to the one number that settles the argument -
    ///
    ///     planned clearance   what LaneClearance() said before the ball was struck
    ///     actual clearance    how close the ball, below head height, really came to a defender
    ///
    /// If those two agree and the ball still went through somebody, the gate threshold is
    /// wrong. If they disagree, the geometry is wrong. If they agree and the ball missed
    /// everybody, the selector is right and the fault is upstream - turn the freeze off
    /// one piece at a time (`unfreezeDefence`, `perfectStrike`) until it comes back.
    ///
    ///   P      freeze / unfreeze the whole pitch
    ///   Return play the pass at the top of the list
    ///   B      put the ball back at the centre-back's feet
    ///
    /// Everybody is frozen where they stand, so you can drag players around in the Scene
    /// view between passes and watch the table rescore live.
    ///
    /// It decides nothing about the game. It disables the coordinators while it runs and
    /// puts them back exactly as it found them, and every number it prints is read from
    /// <see cref="PassPlanner"/> rather than recomputed here - a bench that does its own
    /// arithmetic is measuring itself.
    /// </summary>
    public class PassLab : MonoBehaviour
    {
        [Header("Refs - found automatically if left empty")]
        public Ball ball;
        public TeamAttack attack;
        public TeamDefence defence;
        public FootballerController player;
        public DrillDirector director;

        [Header("Bench")]
        [Tooltip("Running. P toggles it, and so does ticking it here - the takeover is driven off this flag rather than off the key, so the bench can be started from the Inspector while the game is paused.")]
        public bool running;
        [Tooltip("Play the next pass on a timer instead of waiting for Space.")]
        public bool auto = true;
        [Tooltip("Seconds on the ball before the next pass leaves. Long enough to read the table.")]
        public float dwell = 2.0f;

        [Header("What is held still")]
        [Tooltip("Let the defence move while the attack stays frozen. Turn this on once the frozen picture looks right - if the ball starts finding bodies again, the fault is a stale decision, not the criteria.")]
        public bool unfreezeDefence;
        [Tooltip("Strike it exactly down the chosen line, with no weight or direction error. Turn it off to see how much of the damage is the strike rather than the choice.")]
        public bool perfectStrike = true;

        [Header("Delivery")]
        [Tooltip("How near the aim point the ball has to get before the receiver is treated as having it. Nobody can move to meet it here, so it is delivered rather than collected.")]
        public float arriveRadius = 1.5f;
        [Tooltip("A ball still travelling after this long is abandoned and given to whoever it was aimed at.")]
        public float flightTimeout = 8f;

        [Header("Readout")]
        public bool showTable = true;
        [Tooltip("How many past passes to keep on screen.")]
        [Range(1, 12)] public int history = 6;

        /// <summary>The bench is holding the pitch still.</summary>
        public bool Running { get { return taken; } }

        /// <summary>Where the pass under consideration would be struck from.</summary>
        public Vector3 Origin { get { return origin; } }

        /// <summary>Every option, rescored this frame. The debug view draws these.</summary>
        public IList<PassCandidate> Candidates { get { return candidates; } }

        /// <summary>The option currently top of the list, or null if it is going long.</summary>
        public Transform Favourite { get { return live.valid ? live.receiver : null; } }

        /// <summary>One pass, as chosen and as it actually flew.</summary>
        struct Shot
        {
            public string receiver;
            public PassKind kind;
            public float distance;
            public float planned;     // LaneClearance() before it was struck
            public float actual;      // closest a defender came to the ball, below head height
            public string nearest;    // and who that was
            public float actualAt;    // how far down the pass that happened
            public int rejected;
        }

        Transform holder;
        Vector3 origin;
        PassPlan live;                 // rescored every frame while he is on the ball
        PassPlan struck;               // the one that was actually played
        readonly List<PassCandidate> candidates = new List<PassCandidate>();
        readonly List<Shot> shots = new List<Shot>();

        bool taken;                    // it has actually taken the pitch over
        bool inFlight;
        float flightT;
        float minGap;
        float minGapAt;
        Transform minGapWho;
        float holdUntil;

        int played, throughBodies, lofted;

        // what the pitch looked like before the bench took it over
        bool[] atkWasActive, defWasActive;
        bool atkWasOn, defWasOn, dirWasOn, playerWasOn;

        GUIStyle sHead, sRow;
        Texture2D texPanel;

        void Awake()
        {
            if (ball == null) ball = FindAnyObjectByType<Ball>();
            if (attack == null) attack = Possession.FindHomeAttack();
            if (defence == null) defence = Possession.FindAwayDefence();
            if (player == null) player = FindAnyObjectByType<FootballerController>();
            if (director == null) director = FindAnyObjectByType<DrillDirector>();
        }

        void Update()
        {
            if (ProtoInput.PassLabPressed()) running = !running;

            // Driven off the flag, not off the key, so ticking the box in the Inspector
            // does the same thing the key does.
            if (running && !taken) Begin();
            else if (!running && taken) Stop();
            if (!taken) return;

            if (ProtoInput.PassLabResetPressed()) GiveToOpener();

            if (inFlight) TickFlight();
            else TickHold();
        }

        void OnDisable() { if (taken) Stop(); }

        // ------------------------------------------------------------- freeze ----

        /// <summary>
        /// Take the pitch over. Everything is remembered rather than assumed, because a
        /// bench that hands back a different game than it borrowed is worse than no bench.
        /// </summary>
        void Begin()
        {
            if (ball == null || attack == null) { running = false; return; }

            atkWasOn = attack.enabled;
            defWasOn = defence != null && defence.enabled;
            dirWasOn = director != null && director.enabled;
            playerWasOn = player != null && player.enabled;

            // The coordinators are what would move people and play passes over the top of
            // the bench. The director would also restart the round and warp everybody home.
            attack.enabled = false;
            if (defence != null) defence.enabled = false;
            if (director != null) { director.enabled = false; director.HideLines(); }
            if (player != null) player.enabled = false;

            atkWasActive = FreezeAttack(true);
            defWasActive = FreezeDefence(!unfreezeDefence);

            shots.Clear();
            played = throughBodies = lofted = 0;
            taken = true;
            running = true;
            GiveToOpener();
        }

        void Stop()
        {
            taken = false;
            running = false;
            inFlight = false;

            RestoreAttack();
            RestoreDefence();

            if (attack != null) attack.enabled = atkWasOn;
            if (defence != null) defence.enabled = defWasOn;
            if (director != null) director.enabled = dirWasOn;
            if (player != null) player.enabled = playerWasOn;

            // Whoever the bench left on the ball is not who the match thinks has it, so
            // the possession is handed back rather than patched up.
            if (attack != null) attack.ResetPossession();
        }

        bool[] FreezeAttack(bool freeze)
        {
            if (attack == null || attack.members == null) return null;
            bool[] was = new bool[attack.members.Length];
            for (int i = 0; i < attack.members.Length; i++)
            {
                if (attack.members[i] == null) continue;
                was[i] = attack.members[i].active;
                attack.members[i].active = !freeze;
                attack.members[i].ClearChase();
            }
            return was;
        }

        bool[] FreezeDefence(bool freeze)
        {
            if (defence == null || defence.members == null) return null;
            bool[] was = new bool[defence.members.Length];
            for (int i = 0; i < defence.members.Length; i++)
            {
                if (defence.members[i] == null) continue;
                was[i] = defence.members[i].active;
                defence.members[i].active = !freeze;
            }
            return was;
        }

        void RestoreAttack()
        {
            if (attack == null || attack.members == null || atkWasActive == null) return;
            for (int i = 0; i < attack.members.Length && i < atkWasActive.Length; i++)
            {
                if (attack.members[i] == null) continue;
                attack.members[i].active = atkWasActive[i];
                attack.members[i].ReleaseBall();
            }
            atkWasActive = null;
        }

        void RestoreDefence()
        {
            if (defence == null || defence.members == null || defWasActive == null) return;
            for (int i = 0; i < defence.members.Length && i < defWasActive.Length; i++)
            {
                if (defence.members[i] == null) continue;
                defence.members[i].active = defWasActive[i];
            }
            defWasActive = null;
        }

        // ---------------------------------------------------------- on the ball ---

        /// <summary>Back to the centre-back the match itself starts from (PROJECT.md 4).</summary>
        void GiveToOpener()
        {
            Transform t = director != null && director.passer != null ? director.passer : null;
            if (t == null) t = NearestMate(ball.transform.position);
            Hand(t);
            inFlight = false;
        }

        /// <summary>Put the ball at his feet and start his clock.</summary>
        void Hand(Transform who)
        {
            holder = who;
            inFlight = false;
            holdUntil = Time.time + dwell;
            if (who == null) { ball.Stop(); return; }

            IBallCarrier c = who.GetComponent<IBallCarrier>();
            if (c != null)
            {
                ball.Attach(c);
                AttackerAI a = who.GetComponent<AttackerAI>();
                if (a != null) a.TakeBall();
            }
            else ball.Hold(who.position);
        }

        /// <summary>
        /// Rescore every frame he stands there. The table is meant to be watched while
        /// players are dragged around in the Scene view, and a table computed once at the
        /// moment of the pass would show the answer to a question you have since changed.
        /// </summary>
        void TickHold()
        {
            if (holder == null) { GiveToOpener(); return; }

            origin = ball.transform.position;
            live = PassPlanner.Choose(holder, origin, attack.mates, attack.opponents,
                                      attack.pass, attack.attacksPositiveZ, ball, candidates);

            bool go = ProtoInput.PassLabStepPressed() || (auto && Time.time >= holdUntil);
            if (go && live.valid) Strike();
            else if (go) holdUntil = Time.time + dwell;   // nothing on: wait and rescore
        }

        /// <summary>
        /// Hit it. Exactly down the chosen line unless perfectStrike is off, in which
        /// case it goes through the same error model a real pass does - which is the way
        /// to find out how much of the damage belongs to the strike rather than the choice.
        /// </summary>
        void Strike()
        {
            struck = live;
            Vector3 from = origin;

            AttackerAI a = holder.GetComponent<AttackerAI>();
            if (a != null) { a.FaceTarget(struck.target); a.ReleaseBall(); }

            if (struck.kind == PassKind.Lofted)
            {
                Vector3 landing = struck.target;
                if (!perfectStrike)
                {
                    BallModel.Strike st = BallModel.Resolve(from, struck.target, 0f, 0f,
                                                            PassPlanner.PassingOf(holder), 0.5f);
                    landing = from + st.direction * (struck.distance * (1f + st.speedErrorPct * 0.01f));
                }
                ball.Loft(from, landing, PassPlanner.LoftApex(struck.distance, attack.pass));
                struck.target = landing;
            }
            else
            {
                float v0 = Ball.SpeedToReach(struck.distance, attack.pass.arrivePace, ball.rollDecel);
                Vector3 dir = struck.target - from; dir.y = 0f; dir.Normalize();
                float speed = v0;
                if (!perfectStrike)
                {
                    BallModel.Strike st = BallModel.Resolve(from, struck.target, 0f, v0,
                                                            PassPlanner.PassingOf(holder), 0.5f);
                    dir = st.direction;
                    speed = st.speed;
                }
                ball.Launch(from, dir, speed);
            }

            inFlight = true;
            flightT = 0f;
            minGap = float.MaxValue;
            minGapAt = 0f;
            minGapWho = null;
        }

        // -------------------------------------------------------------- flight ---

        /// <summary>
        /// Watch it go, and measure the one thing the selector cannot measure for itself:
        /// how close the ball ACTUALLY came to a defender, sampled off the real trajectory
        /// rather than off the straight line the lane test assumed.
        ///
        /// Only below head height. A ball over the top is meant to cross bodies - that is
        /// the entire point of lifting it (PROJECT.md 3.17) - and counting those approaches
        /// would report every long ball as a failure.
        /// </summary>
        void TickFlight()
        {
            flightT += Time.deltaTime;

            float head = defence != null ? defence.interceptHeight
                       : (director != null ? director.interceptHeight : 1.1f);
            Vector3 bp = ball.transform.position;

            if (bp.y <= head)
            {
                float travelled = Flat(bp - struck.from).magnitude;
                Transform[] opp = attack.opponents;
                for (int i = 0; opp != null && i < opp.Length; i++)
                {
                    if (opp[i] == null) continue;
                    float d = Flat(opp[i].position - bp).magnitude;
                    if (d < minGap) { minGap = d; minGapAt = travelled; minGapWho = opp[i]; }
                }
            }

            bool arrived = Flat(bp - struck.target).magnitude <= arriveRadius;
            bool stopped = !ball.Airborne && ball.SpeedNow < ball.deadSpeed;
            if (!arrived && !stopped && flightT < flightTimeout) return;

            Record();
            Hand(struck.receiver != null ? struck.receiver : NearestMate(bp));
        }

        void Record()
        {
            Shot s = new Shot();
            s.receiver = struck.receiver != null ? Short(struck.receiver.name) : "(공간)";
            s.kind = struck.kind;
            s.distance = struck.distance;
            s.planned = struck.clearance;
            s.actual = minGap == float.MaxValue ? 99f : minGap;
            s.nearest = minGapWho != null ? Short(minGapWho.name) : "-";
            s.actualAt = minGapAt;
            s.rejected = struck.rejected;

            shots.Insert(0, s);
            while (shots.Count > history) shots.RemoveAt(shots.Count - 1);

            played++;
            if (struck.kind == PassKind.Lofted) lofted++;
            // The complaint, counted: a ball on the floor that passed inside the corridor
            // the gate says has to be empty.
            else if (s.actual < attack.pass.laneHalfWidth) throughBodies++;
        }

        // --------------------------------------------------------------- utils ---

        Transform NearestMate(Vector3 p)
        {
            Transform best = null;
            float bd = float.MaxValue;
            Transform[] m = attack != null ? attack.mates : null;
            for (int i = 0; m != null && i < m.Length; i++)
            {
                if (m[i] == null) continue;
                float d = Flat(m[i].position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = m[i]; }
            }
            return best;
        }

        static string Short(string n)
        {
            if (string.IsNullOrEmpty(n)) return "?";
            int i = n.IndexOf('(');
            if (i > 0) n = n.Substring(0, i);
            return n.Replace("Home ", "").Replace("Away ", "").Trim();
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        // ---------------------------------------------------------------- HUD ----

        void OnGUI()
        {
            if (!taken || !showTable) return;
            EnsureStyles();

            const float w = 560f;
            float h = 150f + (candidates.Count + shots.Count) * 17f;
            GUI.DrawTexture(new Rect(8f, 8f, w, h), texPanel);

            float y = 14f;
            PassRules r = attack.pass;

            GUI.Label(new Rect(16f, y, w, 20f), string.Format(
                "PASS LAB — 전원 정지 · 패스만  |  P 종료   Space 패스   B 리셋   (자동 {0})",
                auto ? "켜짐" : "꺼짐"), sHead);
            y += 20f;

            GUI.Label(new Rect(16f, y, w, 20f), string.Format(
                "게이트: 레인 ≥{0:0.0}m · 사거리 {1:0}~{2:0}m · 셀 수비 ≤{3}   (그림자 {4:0.0}m)",
                r.laneHalfWidth, r.minRange, r.groundRange, r.maxCellDefenders, r.carrierShadow), sRow);
            y += 18f;

            GUI.Label(new Rect(16f, y, w, 20f), string.Format(
                "공: {0}   패스 {1}회 · 롱볼 {2} · 몸으로 간 땅볼 {3}",
                holder != null ? Short(holder.name) : "-", played, lofted, throughBodies), sRow);
            y += 22f;

            GUI.Label(new Rect(16f, y, w, 20f),
                "후보              거리   레인   셀   위협  품질  여유  위험   합계", sHead);
            y += 18f;

            for (int i = 0; i < candidates.Count; i++)
            {
                PassCandidate c = candidates[i];
                bool win = live.valid && c.open && c.receiver == live.receiver;
                GUI.color = c.open ? (win ? new Color(0.55f, 1f, 0.7f) : new Color(0.85f, 0.9f, 0.95f))
                                   : new Color(1f, 0.55f, 0.5f);
                GUI.Label(new Rect(16f, y, w, 18f), string.Format(
                    "{0} {1,-10} {2,5:0.0}m {3,5:0.0} {4,4}   {5,4:0.00} {6,4:0.00} {7,4:0.00} {8,4:0.00}  {9,5:0.00}  {10}",
                    win ? "▶" : " ", Short(c.receiver.name), c.distance, c.clearance, c.cellDefenders,
                    c.threat, c.quality, c.freedom, c.risk, c.total,
                    c.open ? "" : c.blockedBy), sRow);
                y += 17f;
            }
            GUI.color = Color.white;
            y += 6f;

            if (!live.valid)
            {
                GUI.color = new Color(1f, 0.8f, 0.4f);
                GUI.Label(new Rect(16f, y, w, 18f), "통과한 후보 없음 — 롱볼로 넘어감", sRow);
                GUI.color = Color.white;
                y += 20f;
            }
            else if (live.kind == PassKind.Lofted)
            {
                GUI.color = new Color(1f, 0.8f, 0.4f);
                GUI.Label(new Rect(16f, y, w, 18f), string.Format(
                    "게이트 전멸 {0}명 → 롱볼: {1}", live.rejected,
                    live.receiver != null ? Short(live.receiver.name) : "-"), sRow);
                GUI.color = Color.white;
                y += 20f;
            }

            GUI.Label(new Rect(16f, y, w, 20f),
                "지난 패스        종류   거리   계획레인 → 실제최근접 (누구, 몇 m 지점)", sHead);
            y += 18f;

            for (int i = 0; i < shots.Count; i++)
            {
                Shot s = shots[i];
                bool bad = s.kind != PassKind.Lofted && s.actual < r.laneHalfWidth;
                GUI.color = bad ? new Color(1f, 0.45f, 0.45f) : new Color(0.85f, 0.9f, 0.95f);
                GUI.Label(new Rect(16f, y, w, 18f), string.Format(
                    "{0,-10} {1,-6} {2,5:0.0}m   {3,5:0.0} → {4,5:0.0}  ({5}, {6:0.0}m){7}",
                    s.receiver, s.kind == PassKind.Lofted ? "롱볼" : "땅볼", s.distance,
                    s.planned, s.actual, s.nearest, s.actualAt,
                    bad ? "  ← 몸을 통과" : ""), sRow);
                y += 17f;
            }
            GUI.color = Color.white;
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
