using UnityEngine;

namespace Prototype
{
    public enum PassType
    {
        ToFeet,  // short, along the floor, to the team-mate nearest the cursor - left click / E
        Through, // long, along the floor, into the space under the cursor - mouse 4 / F
        Lob      // long, in the air, onto the grass under the cursor - wheel click
    }

    /// <summary>
    /// The match, not the drill any more.
    ///
    ///   Setup  - everyone on their formation slot, the ball at the centre-back's feet
    ///   Play   - the ball is in play and belongs to whoever last took it. Bots pass to
    ///            each other (<see cref="PassPlanner"/>), the defence tries to cut those
    ///            passes out (<see cref="TeamDefence"/>), and the human is one of eleven
    ///            options rather than the only one
    ///   Result - possession was lost, the ball went out, or it was put in the net
    ///
    /// WHAT WAS KEPT from the drill, and why:
    ///
    ///  - it still starts from a centre-back. A possession that begins with the ball
    ///    played into the pivot with his back to goal is the situation the whole vision
    ///    mechanic exists for, and starting anywhere else stops producing it
    ///  - the receiving rules, the carry, the tackle and the strikes are untouched
    ///  - the two numbers in the corner. Confirmed-vs-blind is the reason this prototype
    ///    exists (PROJECT.md §1) and it survives the rewrite, but it is now scored on
    ///    what actually happened to the possession after his touch rather than on a
    ///    six-second timer: his pass reaching a team-mate is keeping it, his pass being
    ///    intercepted is not
    ///
    /// WHAT WENT: the ball is no longer addressed to the human. He gets it when he is
    /// the best option, and the round can now be lost by somebody else entirely.
    /// </summary>
    public class DrillDirector : MonoBehaviour
    {
        [Header("Refs")]
        public FootballerController player;
        [Tooltip("Kept for the HUD's threat readout - the striker the human can see. Every away player defends now, not just this one.")]
        public DefenderAI defender;
        public Perceivable defenderPerc;
        [Tooltip("The centre-back the possession starts with.")]
        public Transform passer;
        public Ball ball;
        public TeamAttack attack;
        public TeamDefence defence;
        [Tooltip("Who has the ball. With it, winning the ball back hands the pitch over instead of ending the round - see Possession.")]
        public Possession possession;

        [Header("Layout")]
        public Vector3 startPos = Vector3.zero;
        public Vector3 passerHome = new Vector3(0f, 0f, -13f);
        public float pitchHalfX = 34f;
        public float pitchHalfZ = 52.5f;
        public float goalZ = 52.5f;
        public float goalHalfWidth = 3.66f;

        [Header("Stats (0..1) - drive error far more than power")]
        [Tooltip("Passing moved onto FootballerController - it is an attribute of the man, and the pass selector has to read the same one.")]
        [Range(0f, 1f)] public float playerCrossing = 0.70f;
        [Range(0f, 1f)] public float playerShooting = 0.70f;
        [Range(0f, 1f)] public float playerStrength = 0.60f;

        [Header("The human's pass")]
        [Tooltip("Length of a pass struck into an empty wedge - nobody was in the direction he pointed, so it goes nowhere in particular.")]
        public float blindPassDistance = 14f;
        [Tooltip("How long after passing he cannot collect the ball back himself.")]
        public float selfPassLock = 0.8f;
        [Tooltip("The short pass looks for a team-mate this close to the cursor and plays it to his feet - the nearest to the cursor if there are several. Nobody that close, and it goes where the cursor points: a pass to nobody.")]
        public float scanRadius = 10f;
        [Tooltip("Longest ball over the top. The ground passes stop at PassRules.groundRange.")]
        public float lobRange = 50f;

        [Header("Passing - rolled at kickoff")]
        [Tooltip("Give every player, the human included, a fresh short and long passing on the 1..100 scale when the match starts. Off: whatever the bodies already have.")]
        public bool rollPassing = true;
        [Tooltip("Inclusive range of the roll, 1..100 (Career.StatScale).")]
        public Vector2Int passingRoll = new Vector2Int(70, 85);

        [Header("Calling for it - his buttons while a team-mate has the ball")]
        [Tooltip("How long a call for the ball stands, and how long a shot, through ball or lob pressed without the ball is kept as his first touch. Ends sooner if he touches the ball or the other side wins it.")]
        public float requestHold = 4f;

        [Header("Tuning")]
        public float tightGap = 1.5f;
        public float mediumGap = 3.2f;
        public float freshInfoSeconds = 1.6f;
        public float passBuffer = 0.35f;
        public float touchRadius = 1.25f;
        [Tooltip("How near a loose ball a defender has to get to come away with it.")]
        public float interceptRadius = 0.7f;
        [Tooltip("Above this the ball is over everybody's head and nobody can take it.")]
        public float interceptHeight = 1.1f;
        [Tooltip("A ball has to get this far from the foot that struck it before anybody can claim it. Without it the man marking the passer owns every pass before it has left.")]
        public float passEscape = 1.4f;
        [Tooltip("Fraction of the ball's pace you must be running at to take a ball that is still running away from you.")]
        public float throughCatchFraction = 0.40f;
        [Tooltip("A possession nobody ends is restarted after this long.")]
        public float roundSeconds = 45f;
        [Tooltip("A ball nobody goes for is dead after this long.")]
        public float deadBallSeconds = 4f;

        enum Phase { Setup, Play, Result }

        Phase phase;
        float phaseT;

        bool incomingForHuman;
        int seenPassSerial = -1;
        float humanCollectAt = -99f;
        float deadSince = -1f;

        // what he knew at the moment he took the touch
        bool freshAtTouch;
        float gapAtTouch;
        bool armed;              // he touched it and the possession has not been settled yet
        bool awaitingHumanPass;

        enum Act { None, Shoot, Cross, Pass, Through, Lob }

        // what he asked for without the ball (WatchCalls)
        Act kept1st = Act.None;         // his first touch, already chosen
        float keptUntil = -1f;
        float calledAt = -99f;          // the pass press that was a call, not a pass
        int seenDrops;
        float seenPass = -99f, seenThrough = -99f, seenLob = -99f, seenShoot = -99f, seenCross = -99f;

        float exposureAtTackle;
        float lastAimErr, lastSpeedErr;
        string lastStrike = "-";

        string flash = "";
        float flashUntil = -1f;

        int rounds, kept, tackled, fouls;
        int homeGoals, awayGoals;
        Side kickOff = Side.Home;       // who restarts the next round
        int seenTurnovers;
        float homeHeldSince;            // when home last came into possession
        int freshTry, freshOk, staleTry, staleOk;

        string title = "";
        string body = "";
        Color titleCol = Color.white;

        LineRenderer passRing;          // where the ball in flight is going to land / arrive
        Vector3 flightMark;
        int flightSerial = -1;          // Ball.LaunchSerial the mark belongs to
        int seenHomePasses = -1, seenAwayPasses = -1;
        GUIStyle sBig, sMid, sSmall;

        static readonly Color FeetCol = new Color(1f, 1f, 1f, 1f);
        static readonly Color LoftCol = new Color(1f, 0.78f, 0.35f, 1f);
        // ------------------------------------------------------------- setup ----

        void Start()
        {
            Material dim = ProtoMat.UnlitFade(new Color(1f, 1f, 1f, 0.3f));
            passRing = MakeLine("PassTarget", dim, 0.08f, 33);

            RollPassing();
            if (GetComponent<MouseLegend>() == null) gameObject.AddComponent<MouseLegend>();
            if (TeamChemistry.Active == null) gameObject.AddComponent<TeamChemistry>();

            phase = Phase.Setup;
            phaseT = 0f;
        }

        /// <summary>
        /// Short and long passing for everybody, each its own whole number in passingRoll,
        /// rolled once per match. Replaces the per-role table that used to set them
        /// (Formation.PassingFor) - the user's call, 2026-10-18.
        /// </summary>
        void RollPassing()
        {
            if (!rollPassing) return;
            int lo = Mathf.Clamp(Mathf.Min(passingRoll.x, passingRoll.y), 1, 100);
            int hi = Mathf.Clamp(Mathf.Max(passingRoll.x, passingRoll.y), 1, 100);

            var bots = FindObjectsByType<AttackerAI>(FindObjectsSortMode.None);
            for (int i = 0; i < bots.Length; i++)
            {
                bots[i].shortPassing = Random.Range(lo, hi + 1);
                bots[i].longPassing = Random.Range(lo, hi + 1);
            }
            if (player != null)
            {
                player.shortPassing = Random.Range(lo, hi + 1);
                player.longPassing = Random.Range(lo, hi + 1);
            }
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
            if (ProtoInput.RestartPressed()) { kickOff = Side.Home; phase = Phase.Setup; phaseT = 0f; }

            phaseT += Time.deltaTime;

            switch (phase)
            {
                case Phase.Setup: DoSetup(); break;
                case Phase.Play: DoPlay(); break;
                case Phase.Result:
                    if (phaseT > 2.6f) { phase = Phase.Setup; phaseT = 0f; }
                    break;
            }
        }

        // ------------------------------------------------------------ phases ----

        void DoSetup()
        {
            if (player != null)
            {
                // Back to goal, facing the man who is about to play it into him. That
                // body position is the reason the shoulder check exists.
                player.Teleport(startPos, 180f);
                player.EndReceive();
                player.TrackBall = true;
            }

            // Every body back on its slot, through BOTH brains - each keeps its own idea
            // of where it is standing, and a brain that is switched off at the warp would
            // come back on still heading for wherever it last was.
            WarpAll(possession.homeAttack, possession.homeDefence);
            WarpAll(possession.awayAttack, possession.awayDefence);
            if (keepers == null) keepers = FindObjectsByType<GoalkeeperAI>(FindObjectsSortMode.None);
            for (int i = 0; i < keepers.Length; i++)
                if (keepers[i] != null) keepers[i].Warp(keepers[i].homeSlot);
            possession.Restart(kickOff);

            // A centre-back starts with it at his feet, and his side's TeamAttack takes it
            // from there. Home's is the drill's own passer; away's is whichever of theirs
            // plays left centre-back, the mirror of him.
            AttackerAI opener = kickOff == Side.Home
                ? (passer != null ? passer.GetComponent<AttackerAI>() : null)
                : OpenerOf(possession.awayAttack);
            if (ball != null)
            {
                if (opener != null) { ball.Attach(opener); opener.TakeBall(); }
                else if (passer != null) ball.Hold(passer.position);
            }

            title = "";
            body = "";
            armed = false;
            awaitingHumanPass = false;
            incomingForHuman = false;
            humanCollectAt = -99f;
            deadSince = -1f;
            kept1st = Act.None;
            seenPass = seenThrough = seenLob = seenShoot = seenCross = -99f;
            seenDrops = attack != null ? attack.RequestsDropped : 0;
            seenPassSerial = attack != null ? attack.PassSerial : -1;
            seenTurnovers = possession.Turnovers;
            homeHeldSince = Time.time;

            phase = Phase.Play;
            phaseT = 0f;
        }

        void DoPlay()
        {
            if (ball == null || player == null) return;

            WatchForNewPass();
            WatchForTurnover();
            DrawLive();

            bool humanHasIt = ball.Carried && ReferenceEquals(ball.Carrier, player);
            WatchCalls(humanHasIt);

            if (humanHasIt)
            {
                Carry();
            }
            else if (!ball.Carried)
            {
                LooseBall();
            }
            else
            {
                // A team-mate has taken it. If that was the ball the human played, his
                // touch worked: the possession survived it, which is the whole question
                // the confirmed-vs-blind number is asking.
                deadSince = -1f;
                bool ours = possession.SideOf(ball.CarrierTransform) == Side.Home;
                if (ours && awaitingHumanPass) Settle(true);
                if (incomingForHuman) { incomingForHuman = false; player.EndReceive(); }
            }

            // "Kept it" means HOME held it that long, unbroken - not that the clock ran
            // while the other side was attacking.
            bool heldLongEnough = possession.InPossession == Side.Home
                                  && Time.time - homeHeldSince > roundSeconds;
            if (heldLongEnough)
            {
                rounds++; kept++;
                Settle(true);
                EndRound();
                Finish("KEPT IT", string.Format("{0:0}초 동안 공을 지켰습니다.", roundSeconds),
                       new Color(0.45f, 0.9f, 0.5f));
            }
        }

        /// <summary>
        /// A pass has just been struck. If it was meant for the human he goes into the
        /// receiving rules; if it was not, he is free to move and can still go and get it.
        /// </summary>
        void WatchForNewPass()
        {
            if (attack == null || attack.PassSerial == seenPassSerial) return;
            seenPassSerial = attack.PassSerial;

            incomingForHuman = attack.IntendedReceiver == player.transform;

            if (incomingForHuman) player.BeginReceive();
            else player.EndReceive();

            // His call was answered. The touch he kept waits for this ball, however long
            // it is in the air.
            if (incomingForHuman && kept1st != Act.None) keptUntil = Time.time + requestHold;
        }

        /// <summary>
        /// Without the ball, while his side has it, his buttons ask for it (2026-10-20, the
        /// user's call):
        ///
        ///   pass                        "give it here" - the man on the ball plays it to him
        ///                               before anything else (TeamAttack.RequestBall)
        ///   shot / through ball / lob   the same call, and that button is also kept as his
        ///                               first touch when the ball reaches him, aimed wherever
        ///                               the cursor is at that moment
        ///
        /// The later button wins: a pass or a cross drops a kept touch and goes back to the
        /// ordinary passBuffer. The kept touch lapses when he touches the ball, when the
        /// other side wins it, or after requestHold. A pass pressed while a ball is already
        /// on its way to him is not a call - it is the first-time pass it always was.
        /// </summary>
        void WatchCalls(bool humanHasIt)
        {
            bool pass = Fresh(player.LastPassPress, ref seenPass);
            bool through = Fresh(player.LastThroughPress, ref seenThrough);
            bool lob = Fresh(player.LastLobPress, ref seenLob);
            bool shoot = Fresh(player.LastShootPress, ref seenShoot);
            bool cross = Fresh(player.LastCrossPress, ref seenCross);

            // The call ended without the ball coming to him - the man it went to has given
            // it to somebody else - and the touch he kept for it goes with it.
            if (attack != null && attack.RequestsDropped != seenDrops)
            {
                seenDrops = attack.RequestsDropped;
                kept1st = Act.None;
            }
            if (kept1st != Act.None && Time.time > keptUntil) kept1st = Act.None;
            if (humanHasIt || possession.InPossession != Side.Home) return;

            if (pass || cross) kept1st = Act.None;
            if (shoot) Keep(Act.Shoot);
            if (through) Keep(Act.Through);
            if (lob) Keep(Act.Lob);

            if ((pass || shoot || through || lob) && !incomingForHuman && attack != null)
            {
                attack.RequestBall(player.transform, requestHold);
                if (pass) calledAt = player.LastPassPress;
            }
        }

        void Keep(Act a)
        {
            kept1st = a;
            keptUntil = Time.time + requestHold;
        }

        static bool Fresh(float pressedAt, ref float seen)
        {
            if (pressedAt <= seen) return false;
            seen = pressedAt;
            return true;
        }

        /// <summary>
        /// The button he pressed last within passBuffer, if any. The latest wins, so a pass
        /// pressed after a shot is the pass. The press that called for the ball is not a
        /// pass - a short ball can arrive inside the buffer, and he asked to have it.
        /// </summary>
        Act Buffered()
        {
            Act a = Act.None;
            float t = Time.time - passBuffer;
            if (player.LastShootPress > t) { a = Act.Shoot; t = player.LastShootPress; }
            if (player.LastCrossPress > t) { a = Act.Cross; t = player.LastCrossPress; }
            if (player.LastPassPress > t && player.LastPassPress != calledAt) { a = Act.Pass; t = player.LastPassPress; }
            if (player.LastThroughPress > t) { a = Act.Through; t = player.LastThroughPress; }
            if (player.LastLobPress > t) { a = Act.Lob; }
            return a;
        }

        /// <summary>Strike it as `a` says. False if there was nothing to do.</summary>
        bool Do(Act a, bool firstTime)
        {
            switch (a)
            {
                case Act.Shoot: Shoot(firstTime); return true;
                case Act.Cross: Cross(firstTime); return true;
                case Act.Pass: PlayIt(PassType.ToFeet, firstTime); return true;
                case Act.Through: PlayIt(PassType.Through, firstTime); return true;
                case Act.Lob: PlayIt(PassType.Lob, firstTime); return true;
            }
            return false;
        }

        void LooseBall()
        {
            if (incomingForHuman)
                player.UpdateReceive(ball.transform.position, ball.Velocity, ball.InFlight);

            if (OffThePitch()) return;
            if (DefendingSideWonIt()) return;

            Vector3 bp = ball.transform.position; bp.y = 0f;
            Vector3 pp = player.transform.position; pp.y = 0f;

            if (!ball.Airborne && Time.time >= humanCollectAt
                && Vector3.Distance(bp, pp) < touchRadius && CanTouch())
            {
                TakeTouch();
                return;
            }

            // Nobody is going for it. Rather than let a dead ball sit in a corner, restart.
            if (!ball.InFlight)
            {
                if (deadSince < 0f) deadSince = Time.time;
                if (Time.time - deadSince > deadBallSeconds)
                {
                    rounds++;
                    Settle(false);
                    EndRound();
                    Finish("DEAD BALL", "아무도 잡지 못했습니다.", new Color(0.95f, 0.6f, 0.25f));
                }
            }
            else deadSince = -1f;
        }

        /// <summary>
        /// Did a defender get to the ball? He does not have to tackle anybody - a pass
        /// played across a man is his, and that is the entire point of the lane rule the
        /// passing side is now working to.
        /// </summary>
        /// <summary>
        /// Whichever side is defending gets to the loose ball first.
        ///
        /// This used to be DefenceTookIt, and it ended the round - "INTERCEPTED" - because
        /// the side that won it had no way to attack. Now it hands the pitch over: the
        /// man who got there has the ball, his side attacks, and play goes on. It also
        /// works in both directions, which it could not before: it asks whoever is
        /// defending NOW, and that is home as often as away.
        /// </summary>
        bool DefendingSideWonIt()
        {
            TeamDefence def = possession.Defending;
            if (def == null || def.members == null) return false;
            if (ball.transform.position.y > interceptHeight) return false;
            if (ball.InFlight && ball.Travelled < passEscape) return false;

            Vector3 bp = ball.transform.position; bp.y = 0f;

            for (int i = 0; i < def.members.Length; i++)
            {
                var d = def.members[i];
                if (d == null || !d.enabled || !d.active || d.Recovering) continue;

                Vector3 dp = d.transform.position; dp.y = 0f;
                if (Vector3.Distance(dp, bp) > interceptRadius) continue;

                bool humansBall = awaitingHumanPass;
                ball.Stop();

                Side winner = possession.InPossession == Side.Home ? Side.Away : Side.Home;
                possession.TurnOver(winner, d.transform);
                if (winner == Side.Away)
                    Flash(humansBall ? "내 패스가 끊겼습니다 — 원정 공격" : "공을 뺏겼습니다 — 원정 공격");
                return true;
            }
            return false;
        }

        /// <summary>
        /// The side on the ball changed. Nothing ends - the match carries on the other
        /// way - but the human's last touch has its answer now, and that answer is the
        /// number this prototype exists to measure (PROJECT.md 1).
        /// </summary>
        void WatchForTurnover()
        {
            if (possession.Turnovers == seenTurnovers) return;
            seenTurnovers = possession.Turnovers;

            if (possession.InPossession == Side.Away)
            {
                tackled++;              // 뺏김
                Settle(false);
                incomingForHuman = false;
                awaitingHumanPass = false;
                kept1st = Act.None;
                player.EndReceive();
                player.TrackBall = true;
            }
            else
            {
                homeHeldSince = Time.time;
                Flash("되찾았습니다 — 홈 공격");
            }
        }

        /// <summary>Is the ball live - not being set up for a restart, not showing a result.</summary>
        public bool InPlay { get { return phase == Phase.Play; } }

        GoalkeeperAI[] keepers;

        /// <summary>
        /// A keeper has it in his hands. He has no attacking brain to play it with, so this
        /// is a restart like any other: the round ends and his side starts again from its
        /// left centre-back - the rule a ball over the line already uses (PROJECT.md 3.26).
        /// </summary>
        public void KeeperCaught(GoalkeeperAI k)
        {
            if (phase != Phase.Play) return;
            rounds++;
            kickOff = k.MySide;
            Settle(k.MySide == Side.Home);
            EndRound();
            Finish("선방", k.MySide == Side.Away ? "원정 골키퍼가 잡았습니다 — 원정 공" : "홈 골키퍼가 잡았습니다 — 홈 공",
                   k.MySide == Side.Home ? new Color(0.45f, 0.9f, 0.5f) : new Color(0.95f, 0.6f, 0.25f));
        }

        /// <summary>A keeper has pushed it away. Play goes on; this only says so.</summary>
        public void KeeperParried(GoalkeeperAI k, bool roundThePost)
        {
            Flash(roundThePost ? "선방 — 골대 밖으로 쳐냈습니다" : "선방 — 쳐냈습니다");
        }

        /// <summary>The left centre-back of a side - who a restart is given to.</summary>
        static AttackerAI OpenerOf(TeamAttack side)
        {
            if (side == null || side.members == null) return null;
            for (int i = 0; i < side.members.Length; i++)
                if (side.members[i] != null && side.members[i].role == Role.LCB) return side.members[i];
            for (int i = 0; i < side.members.Length; i++)
                if (side.members[i] != null && Formation.IsCentreBack(side.members[i].role)) return side.members[i];
            return side.members.Length > 0 ? side.members[0] : null;
        }

        static void WarpAll(TeamAttack atk, TeamDefence def)
        {
            if (atk != null && atk.members != null)
                for (int i = 0; i < atk.members.Length; i++)
                    if (atk.members[i] != null) atk.members[i].Warp(atk.members[i].homeSlot);
            if (def != null && def.members != null)
                for (int i = 0; i < def.members.Length; i++)
                {
                    if (def.members[i] == null) continue;
                    def.members[i].Warp(def.members[i].homeSlot);
                }
        }

        bool OffThePitch()
        {
            Vector3 p = ball.transform.position;
            if (Mathf.Abs(p.z) <= pitchHalfZ && Mathf.Abs(p.x) <= pitchHalfX) return false;

            bool inMouth = Mathf.Abs(p.x) < goalHalfWidth;
            bool homeGoal = p.z > pitchHalfZ && inMouth;      // home attacks +Z
            bool awayGoal = p.z < -pitchHalfZ && inMouth;     // away attacks -Z

            ball.Stop();
            rounds++;

            // Who restarts. The side that conceded kicks off; a ball that simply went out
            // goes to whoever did NOT have it, which is what a throw-in or a goal kick is.
            if (homeGoal) { homeGoals++; kept++; kickOff = Side.Away; }
            else if (awayGoal) { awayGoals++; kickOff = Side.Home; }
            else kickOff = possession.InPossession == Side.Home ? Side.Away : Side.Home;

            Settle(homeGoal);
            EndRound();
            if (homeGoal)
                Finish("GOAL", string.Format("골망을 흔들었습니다.  {0} : {1}", homeGoals, awayGoals),
                       new Color(0.45f, 0.9f, 0.5f));
            else if (awayGoal)
                Finish("실점", string.Format("원정이 넣었습니다.  {0} : {1}", homeGoals, awayGoals),
                       new Color(0.95f, 0.35f, 0.35f));
            else
                Finish("OUT", kickOff == Side.Home ? "공이 라인을 벗어났습니다 — 홈 공" : "공이 라인을 벗어났습니다 — 원정 공",
                       new Color(0.95f, 0.6f, 0.25f));
            return true;
        }

        // ------------------------------------------------------------- touch ----

        /// <summary>
        /// A ball still running away from him has to be taken IN STRIDE - he needs to be
        /// travelling at a real fraction of its pace or he simply does not get it under
        /// control. A ball coming toward him has no such condition.
        ///
        /// This was the through-ball rule when the drill addressed every pass to him. It
        /// is the same rule, stated in terms of the ball rather than in terms of what the
        /// passer intended, which is all that was ever load-bearing about it.
        /// </summary>
        bool CanTouch()
        {
            if (!ball.InFlight) return true;

            Vector3 bv = ball.Velocity; bv.y = 0f;
            if (bv.sqrMagnitude < 0.01f) return true;

            Vector3 toBall = ball.transform.position - player.transform.position;
            toBall.y = 0f;
            if (Vector3.Dot(bv, toBall) <= 0f) return true;   // it is coming to him

            Vector3 pv = player.Velocity; pv.y = 0f;
            return Vector3.Dot(pv, bv.normalized) >= bv.magnitude * throughCatchFraction;
        }

        /// <summary>
        /// He has reached the ball. Either he strikes it first time, or he takes it into
        /// his stride and carries it.
        /// </summary>
        void TakeTouch()
        {
            var near = NearestDefender();
            gapAtTouch = near != null ? near.Gap(player.transform.position) : 99f;

            // Had he looked at THIS defender recently - not at the one the drill used to
            // hand him every round.
            Perceivable perc = near != null ? near.GetComponent<Perceivable>() : null;
            if (perc == null) perc = defenderPerc;
            float age = perc != null ? perc.InfoAge : 999f;
            freshAtTouch = age < freshInfoSeconds;
            armed = true;
            awaitingHumanPass = false;
            incomingForHuman = false;

            // He has it - the call is answered. A touch he kept while he waited for it
            // (WatchCalls) is newer than any pass or cross, which would have dropped it.
            if (attack != null) attack.CancelRequest();
            Act first = kept1st != Act.None ? kept1st : Buffered();
            kept1st = Act.None;
            if (Do(first, true)) return;

            EnterCarry();
        }

        void EnterCarry()
        {
            ball.Attach(player);
            player.EndReceive();
            player.TrackBall = false;      // head up, not staring at his own feet
            deadSince = -1f;
        }

        void Carry()
        {
            if (Do(Buffered(), false)) return;

            var d = NearestDefender();
            if (d == null || !d.WantsTackle(ball.transform.position, ball.Exposure)) return;

            d.BeganTackle();
            exposureAtTackle = ball.Exposure;

            Tackle.Input ti;
            ti.defenderPos = d.transform.position;
            ti.ballPos = ball.transform.position;
            ti.attackerPos = player.transform.position;
            ti.attackerFacing = player.BodyForward;
            // Moving or actively shielding counts. Standing still does not.
            ti.attackerResisting = player.InputDir.sqrMagnitude > 0.09f || player.Shielding;
            ti.tackling01 = d.tackling01;
            ti.strength01 = playerStrength;
            ti.reach = Tackle.Reach;

            string why;
            bool shielded;
            TackleResult r = Tackle.Resolve(ti, out why, out shielded);

            if (r == TackleResult.Won)
            {
                // The tackler has it, and the match carries on the other way - the same
                // rule as a bot being tackled (TeamDefence.TickTackle). WatchForTurnover
                // scores the touch as lost.
                Side them = possession.InPossession == Side.Home ? Side.Away : Side.Home;
                possession.TurnOver(them, d.transform);
                Flash(string.Format("태클당했습니다 — {0} (터치 거리 {1:0.00}m)", why, exposureAtTackle));
                return;
            }

            if (r == TackleResult.Foul)
            {
                ball.Release(player.BodyForward.x * Vector3.right + player.BodyForward.y * Vector3.forward, 1.5f);
                rounds++; fouls++; kept++;
                Settle(true);
                EndRound();
                Finish("FREE KICK", why + "\n몸으로 버티다 얻어낸 파울입니다.",
                       new Color(0.55f, 0.85f, 1f));
                return;
            }

            if (r == TackleResult.Lost || r == TackleResult.OutOfRange)
            {
                // He committed and got nothing either way.
                d.MissedTackle();
                Flash("태클 실패 — " + why);
            }
        }

        // ------------------------------------------------------------ strikes ---

        float PressureOf(float gap)
        {
            return Mathf.Clamp01(Mathf.InverseLerp(mediumGap, tightGap, gap));
        }

        float GapNow()
        {
            var d = NearestDefender();
            return d != null ? d.Gap(player.transform.position) : 99f;
        }

        /// <summary>
        /// Where the ball is being aimed: the patch of grass under the cursor.
        ///
        /// It used to pick the nearest team-mate inside a wedge instead (3.19), because
        /// an arrow key only says one of eight directions and striking one of those is
        /// bowling, not football. A cursor is not one of eight directions. It names a
        /// square metre, so it can be taken at its word - and taking it at its word is
        /// what makes the miss the player's own.
        ///
        /// Clamped to the longest ball anybody actually attempts. The reticle stops
        /// following the cursor past that, which is how the limit tells you about itself
        /// without a message.
        /// </summary>
        public Vector3 AimTarget()
        {
            return AimTarget(attack != null ? attack.pass.groundRange : 30f);
        }

        /// <summary>The same, clamped to `reach` instead - the ball over the top goes further.</summary>
        public Vector3 AimTarget(float reach)
        {
            Vector3 pp = player.transform.position;
            Vector3 flat = new Vector3(pp.x, 0f, pp.z);

            Vector3 want;
            if (player.HasAim) want = new Vector3(player.AimPoint.x, 0f, player.AimPoint.z);
            else
            {
                Vector2 f = player.BodyForward;
                want = flat + new Vector3(f.x, 0f, f.y) * blindPassDistance;
            }

            Vector3 d = want - flat;
            if (d.magnitude > reach) want = flat + d.normalized * reach;
            return want;
        }

        /// <summary>
        /// Who a short pass would go to right now: the team-mate nearest the cursor, if one
        /// is within scanRadius of it and within a ground pass of him. Null means nobody -
        /// the ball would go to the grass under the cursor. Asked every frame, so the
        /// reticle can mark him before the click (AimReticle).
        ///
        /// This puts back, for the short pass only, the snap to a man that 3.19 took out.
        /// The cursor still decides: it is the man nearest it, not nearest the passer.
        /// </summary>
        public Transform ShortPassReceiver()
        {
            if (player == null || attack == null || attack.mates == null) return null;

            Vector3 pp = player.transform.position; pp.y = 0f;
            Vector3 cursor = ScanCentre();
            float reach = attack.pass.groundRange;

            Transform best = null;
            float bd = scanRadius;
            for (int i = 0; i < attack.mates.Length; i++)
            {
                Transform m = attack.mates[i];
                if (m == null || m == player.transform) continue;
                Vector3 mp = m.position; mp.y = 0f;
                if ((mp - pp).magnitude > reach) continue;
                float d = (mp - cursor).magnitude;
                if (d < bd) { bd = d; best = m; }
            }
            return best;
        }

        /// <summary>The point the short pass scans around: the cursor itself, not clamped to any range.</summary>
        public Vector3 ScanCentre()
        {
            return player.HasAim ? new Vector3(player.AimPoint.x, 0f, player.AimPoint.z) : AimTarget();
        }

        /// <summary>Where a short pass would be aimed: his feet, led the way bots lead theirs - or the cursor.</summary>
        public Vector3 ShortPassTarget()
        {
            Transform r = ShortPassReceiver();
            if (r == null) return AimTarget();
            Vector3 t = PassPlanner.LeadPoint(player.transform.position, r, attack.pass, ball, false);
            t.y = 0f;
            return t;
        }

        /// <summary>
        /// The human's pass. Three of them:
        ///
        ///   short     to the feet of the team-mate nearest the cursor (ShortPassReceiver),
        ///             or - nobody there - where the cursor points. Short passing.
        ///   through   along the floor into the space under the cursor, kept onside.
        ///   lob       over the top onto the grass under the cursor, as high as a ball that
        ///             long needs to be (PassPlanner.LoftApex - the bots' long ball).
        ///
        /// The two long ones go where they are pointed whoever is there, and whoever of ours
        /// can get to it first goes after it (TeamAttack.TickChase). Both use long passing.
        /// How near the ball gets to where he wanted it is his problem: the spread grows
        /// with distance (3.9), so a ball asked to travel further is asked to be luckier.
        /// </summary>
        void PlayIt(PassType type, bool firstTime)
        {
            Vector3 pp = player.transform.position;
            PassRules rules = attack != null ? attack.pass : new PassRules();
            float diff = BallModel.Difficulty(PressureOf(GapNow()), 0f, firstTime, false);

            Transform receiver = null;
            Vector3 target;
            float stat;
            if (type == PassType.ToFeet)
            {
                receiver = ShortPassReceiver();
                target = ShortPassTarget();
                stat = player.passing;
                diff *= TeamChemistry.ErrorScale(player.transform, receiver);
            }
            else if (type == PassType.Through)
            {
                // He has already said where with the cursor, so there is no lead to add -
                // only the offside line to respect, which is what makes a run timed.
                target = AimTarget();
                if (attack != null)
                    target = Offside.KeepOnside(target, attack.OffsideLine,
                                                attack.attacksPositiveZ, rules.maxLead * 0.25f);
                stat = player.LongPassing;
            }
            else
            {
                target = AimTarget(lobRange);
                stat = player.LongPassing;
            }
            target.y = 0f;

            float dist = Vector3.Distance(new Vector3(pp.x, 0f, pp.z), target);
            BallModel.Strike st;
            if (type == PassType.Lob)
            {
                // The same as the bots' ball over the top (TeamAttack.PlayPass): Resolve gives
                // the sprayed direction, and the weight error becomes a LENGTH error, because
                // Loft solves the strike from the distance and the apex.
                st = BallModel.Resolve(pp, target, 0f, 0f, stat, diff);
                Vector3 landing = pp + st.direction * (dist * (1f + st.speedErrorPct * 0.01f));
                ball.Loft(pp, landing, PassPlanner.LoftApex(dist, rules));
                MarkFlight(landing);
            }
            else
            {
                float v0 = Ball.SpeedToReach(dist, rules.arrivePace, ball.rollDecel);
                st = BallModel.Resolve(pp, target, 0f, v0, stat, diff);
                ball.Release(st.direction, st.speed);
                MarkFlight(target);
            }

            lastAimErr = st.aimErrorM;
            lastSpeedErr = st.speedErrorPct;
            lastStrike = type == PassType.Lob ? "로빙" : type == PassType.Through ? "스루패스"
                       : receiver != null ? "패스" : "빈 곳 패스";

            if (attack != null) attack.HumanPlayed(receiver);

            player.EndReceive();
            player.TrackBall = true;
            humanCollectAt = Time.time + selfPassLock;
            awaitingHumanPass = true;
            incomingForHuman = false;

            float spread = BallModel.AimSpread(stat, dist, diff);
            Flash(string.Format("{0} {1:0.0}m · 오차 ±{2:0.0}m (실제 {3:+0.0;-0.0}m)",
                                lastStrike, dist, spread, st.aimErrorM));
        }

        /// <summary>
        /// Where the shot is aimed: the point on the goal line that the cursor's direction
        /// passes through.
        ///
        /// The DIRECTION comes from the cursor - the player points where he wants it - but
        /// the target is pushed out to the goal line rather than left at the cursor,
        /// because the goal line is where a shot is judged. That also makes the error
        /// honest: the spread is worked out at the distance the ball actually has to
        /// travel, so a shot from 30 m has a 30 m spread even if the cursor was resting
        /// 10 m in front of him.
        ///
        /// Aiming away from the goal still strikes it - down the line he is pointing - and
        /// the ball goes wherever that is. That is a choice, not something to correct.
        /// </summary>
        public Vector3 ShotTarget()
        {
            Vector3 pp = player.transform.position;
            Vector3 from = new Vector3(pp.x, 0f, pp.z);

            Vector3 dir;
            if (player.HasAim) dir = new Vector3(player.AimDir.x, 0f, player.AimDir.y);
            else dir = new Vector3(player.BodyForward.x, 0f, player.BodyForward.y);
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            dir.Normalize();

            if (dir.z > 0.05f)
                return from + dir * ((goalZ - from.z) / dir.z);
            return from + dir * 30f;
        }

        /// <summary>
        /// Strike it where he is aiming, and then LET IT GO.
        ///
        /// This used to decide ON TARGET or OFF TARGET the instant the ball left his foot,
        /// by checking whether the aim error was smaller than half the goal - and end the
        /// round there, with the ball still at his feet. That only worked because every
        /// shot was aimed at the centre of the goal. Aim at the post and the same check
        /// is measuring the error from the post, and calls a ball that goes straight in
        /// a miss.
        ///
        /// So there is no verdict here. The ball flies, and whatever it actually does is
        /// the answer: crosses between the posts is a goal, crosses outside them is out,
        /// hits a body on the way is blocked (OffThePitch, DefenceTookIt). A prediction
        /// that has to agree with the physics is a prediction that can disagree with it.
        /// </summary>
        void Shoot(bool firstTime)
        {
            Vector3 pp = player.transform.position;
            var sh = BallModel.Resolve(pp, ShotTarget(), BallModel.Base.Shot, 0f,
                                       playerShooting,
                                       BallModel.Difficulty(PressureOf(GapNow()), 0f, firstTime, false));
            lastAimErr = sh.aimErrorM; lastSpeedErr = sh.speedErrorPct;
            lastStrike = firstTime ? "첫 터치 슛" : "슛";
            ball.Release(sh.direction, sh.speed);

            humanCollectAt = Time.time + selfPassLock;
            awaitingHumanPass = false;
            player.EndReceive();
            player.TrackBall = true;

            Flash(string.Format("{0} — 오차 {1:+0.00;-0.00}m", lastStrike, sh.aimErrorM));
        }

        void Cross(bool firstTime)
        {
            Vector3 pp = player.transform.position;
            float side = Mathf.Abs(pp.x) < 0.1f ? 1f : Mathf.Sign(pp.x);
            Vector3 farPost = new Vector3(-side * 5.5f, 0f, goalZ - 3.5f);
            var cr = BallModel.Resolve(pp, farPost, BallModel.Base.Cross, 0f,
                                       playerCrossing,
                                       BallModel.Difficulty(PressureOf(GapNow()), 0f, firstTime, false));
            lastAimErr = cr.aimErrorM; lastSpeedErr = cr.speedErrorPct; lastStrike = "크로스";

            // A cross is by definition in the air - it has to clear the block it is
            // travelling over, which is the same thing the long ball uses.
            Vector3 landing = pp + cr.direction * Vector3.Distance(pp, farPost) * (1f + cr.speedErrorPct * 0.01f);
            ball.Loft(pp, landing, 4.5f);

            humanCollectAt = Time.time + selfPassLock;
            awaitingHumanPass = true;
            player.EndReceive();
            player.TrackBall = true;
            Flash("크로스 — 박스로 띄웠습니다");
        }

        // -------------------------------------------------------------- score ---

        /// <summary>
        /// Close the book on the human's touch. Only rounds he was actually involved in
        /// count, so a possession that broke down at the other end of the pitch does not
        /// quietly pollute the number this whole prototype exists to measure.
        /// </summary>
        void Settle(bool ok)
        {
            if (!armed) return;
            armed = false;
            awaitingHumanPass = false;
            if (freshAtTouch) { freshTry++; if (ok) freshOk++; }
            else { staleTry++; if (ok) staleOk++; }
        }

        /// <summary>
        /// Put the drawn lines away. Disabling this component stops it updating them but
        /// leaves whatever they were last showing frozen on the grass, which is exactly
        /// the stale picture a debug view must never leave behind - PassLab calls this
        /// when it takes the pitch over.
        /// </summary>
        public void HideLines()
        {
            if (passRing != null) passRing.enabled = false;
        }

        void EndRound()
        {
            player.EndReceive();
            player.TrackBall = true;
            incomingForHuman = false;
            kept1st = Act.None;
            passRing.enabled = false;
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

        DefenderAI NearestDefender()
        {
            if (defence == null || defence.members == null) return defender;

            Vector3 p = player.transform.position; p.y = 0f;
            DefenderAI best = null;
            float bd = float.MaxValue;
            for (int i = 0; i < defence.members.Length; i++)
            {
                var d = defence.members[i];
                if (d == null || !d.active) continue;
                Vector3 q = d.transform.position; q.y = 0f;
                float dist = Vector3.Distance(q, p);
                if (dist < bd) { bd = dist; best = d; }
            }
            return best != null ? best : defender;
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

        /// <summary>
        /// Where the ball in flight is going to land or arrive - the one mark left on the
        /// match screen besides the cursor and the players (user, 2026-10-18).
        ///
        ///   in the air     where it comes down - from the ball itself (Ball.Predict at
        ///                  Ball.LandingTime), so the spray of the strike is already in it
        ///   on the ground  where the pass was aimed: a bot's plan (TeamAttack.LastPlan), or
        ///                  the human's own target
        ///
        /// A mark belongs to one strike (Ball.LaunchSerial). A touch, a deflection or a save
        /// is a new strike, and the old mark goes.
        /// </summary>
        void DrawLive()
        {
            NoteBotPass(possession.homeAttack, ref seenHomePasses);
            NoteBotPass(possession.awayAttack, ref seenAwayPasses);

            bool lofted = ball.Airborne;
            bool have = false;
            Vector3 mark = Vector3.zero;
            if (ball.InFlight && !ball.Carried)
            {
                if (lofted) { mark = ball.Predict(ball.LandingTime()); have = true; }
                else if (flightSerial == ball.LaunchSerial) { mark = flightMark; have = true; }
            }

            if (!have) { passRing.enabled = false; return; }
            Color c = lofted ? LoftCol : FeetCol;
            c.a = 0.6f;
            SetRing(passRing, mark, lofted ? 1.1f : 0.75f, c, 32);
        }

        void NoteBotPass(TeamAttack side, ref int seen)
        {
            if (side == null) return;
            if (seen == side.PassSerial) return;
            seen = side.PassSerial;
            if (side.LastPlan.valid) MarkFlight(side.LastPlan.target);
        }

        /// <summary>The ball just struck is heading for `p`.</summary>
        void MarkFlight(Vector3 p)
        {
            flightMark = new Vector3(p.x, 0f, p.z);
            flightSerial = ball.LaunchSerial;
        }

        static Vector3 Flat(Vector3 v) { return new Vector3(v.x, 0.04f, v.z); }

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

        }

        /// <summary>What he has asked for without the ball, while it stands. The only thing on
        /// screen for it - one line of text (screen rule, PROJECT.md 3.19).</summary>
        string CallLine()
        {
            bool asked = attack != null && player != null && attack.Requested && attack.Requester == player.transform;
            string what = kept1st == Act.Shoot ? "슛" : kept1st == Act.Through ? "스루패스"
                        : kept1st == Act.Lob ? "로빙" : asked ? "패스" : null;
            if (what == null) return null;
            return (asked && attack.RequestRefused ? "요청 무시됨: " : "요청: ") + what;
        }

        void OnGUI()
        {
            EnsureStyles();
            int w = Screen.width, h = Screen.height;

            var near = player != null ? NearestDefender() : null;

            GUILayout.BeginArea(new Rect(14, 14, 348, 316), GUI.skin.box);
            GUILayout.Label(string.Format("홈 {0} : {1} 원정    ·    공: {2}",
                            homeGoals, awayGoals,
                            possession.InPossession == Side.Home ? "홈" : "원정"), sMid);
            GUILayout.Label(string.Format("라운드 {0}  ·  지킴 {1}  ·  뺏김 {2}  ·  파울 {3}",
                            rounds, kept, tackled, fouls), sSmall);
            GUILayout.Space(6);
            GUILayout.Label("── 확인이 결과를 바꾸는가 ──", sSmall);
            GUILayout.Label(string.Format("확인 후    {0}/{1}   ({2:0}%)", freshOk, freshTry, Pct(freshOk, freshTry)), sSmall);
            GUILayout.Label(string.Format("블라인드   {0}/{1}   ({2:0}%)", staleOk, staleTry, Pct(staleOk, staleTry)), sSmall);
            GUILayout.Space(6);

            bool humanHasIt = ball != null && ball.Carried && ReferenceEquals(ball.Carrier, player);
            if (humanHasIt)
            {
                GUILayout.Label(string.Format("드리블 {0:0.0}s   ·   내 터치 거리 {1:0.00}m",
                                phaseT, ball.Exposure), sSmall);
                if (near != null)
                {
                    float bg; bool inReach, shielded;
                    Tackle.Probe(near.transform.position, ball.transform.position,
                                 player.transform.position, Tackle.Reach, out bg, out inReach, out shielded);
                    GUILayout.Label(string.Format("① 수비-공 {0:0.00}m  {1}",
                                    bg, inReach ? "◀ 사거리 안" : "안전"), sSmall);
                    GUILayout.Label(string.Format("② 사이 막힘  {0}{1}",
                                    shielded ? "예 — 경합" : "아니오 ◀ 뺏김",
                                    near.Recovering ? "     [수비수 넘어짐]" : ""), sSmall);
                }
            }
            else if (attack != null && attack.LastPlan.valid)
            {
                PassPlan p = attack.LastPlan;
                GUILayout.Label(string.Format("{0} {1:0.0}m  →  {2}",
                                p.kind == PassKind.Lofted ? "롱패스(띄움)" : "패스",
                                p.distance,
                                p.receiver != null ? p.receiver.name : "-"), sSmall);
                GUILayout.Label(p.kind == PassKind.Lofted
                    ? string.Format("30m 안에 길이 없음 — 존 수비 {0}명 지역으로", p.cellDefenders)
                    : string.Format("레인 여유 {0:0.0}m   ·   가까운 후보 {1}명 건너뜀",
                                    p.clearance, p.rejected), sSmall);
            }
            else
            {
                GUILayout.Label("센터백이 공을 잡고 있습니다", sSmall);
            }

            string call = CallLine();
            if (call != null) GUILayout.Label(call, sSmall);

            GUILayout.Label(string.Format("{0}  오차 {1:+0.00;-0.00}m  ·  강약 {2:+0.0;-0.0}%",
                            lastStrike, lastAimErr, lastSpeedErr), sSmall);
            if (player != null)
                GUILayout.Label(string.Format("스캔 #{0}  {1}  각도 {2:0}°{3}",
                    player.ScanCount,
                    player.ScanPitchSide > 0 ? "오른쪽" : (player.ScanPitchSide < 0 ? "왼쪽" : "-"),
                    player.scanAngle, player.ScanClamped ? "  [라인 제한]" : ""), sSmall);
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(w - 316, 14, 302, 292), GUI.skin.box);
            GUILayout.Label("WASD / 좌스틱     이동", sSmall);
            GUILayout.Label("마우스 / 우스틱   시선 · 조준", sSmall);
            GUILayout.Label("좌클릭 / E / A    짧은 패스 · 레이오프", sSmall);
            GUILayout.Label("마우스4 / F / Y   스루패스", sSmall);
            GUILayout.Label("마우스5           로빙", sSmall);
            GUILayout.Label("R / B (○)         슛", sSmall);
            GUILayout.Label("C / X (□)         크로스", sSmall);
            GUILayout.Label("Q                 어깨 너머 확인", sSmall);
            GUILayout.Label("Shift / RB        스프린트", sSmall);
            GUILayout.Label("Space / LT        몸으로 지키기", sSmall);
            GUILayout.Label("G                 수비 범위 · 패스 레인 표시", sSmall);
            GUILayout.Label("P  패스 벤치   T  압박 드릴", sSmall);
            GUILayout.Label("Backspace 리셋   Tab  시야제한 " + (PerceptionSystem.xrayDebug ? "OFF" : "ON"), sSmall);
            GUILayout.EndArea();

            string prompt = "";
            if (humanHasIt)
            {
                prompt = ball.Exposure > 0.9f
                    ? "터치가 깁니다 — 속도를 줄이거나 몸을 넣으세요 (Space)"
                    : "드리블 중 — 커서로 겨냥하고 좌클릭 패스 · 마우스4 스루 · 마우스5 로빙 · R 슛";
            }
            else if (incomingForHuman && ball != null && ball.InFlight)
            {
                prompt = player.Mode == FootballerController.Receive.OnLine
                    ? "공이 몸쪽으로 옵니다   —   마중 나가거나, 흘리며 받으세요"
                    : "공이 앞 공간으로 갔습니다   —   자동으로 따라갑니다 (Shift로 가속)";
            }
            else if (phase == Phase.Play)
            {
                prompt = "팀이 공을 돌리고 있습니다   —   패스 길을 만들어 주세요";
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
