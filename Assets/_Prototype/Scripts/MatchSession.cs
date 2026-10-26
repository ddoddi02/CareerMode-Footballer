using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// The match as the career asked for it (<see cref="MatchHandoff"/>): a clock, a one-line
    /// scoreboard, an end - at the whistle or when he gives up with Esc - and the way back. Not
    /// there when the scene is opened by hand, so the drill is what it was. The play itself is
    /// still <see cref="DrillDirector"/>; this only counts its goals.
    /// </summary>
    public class MatchSession : MonoBehaviour
    {
        [Tooltip("Real seconds of play. A placeholder - the real length is not decided (manager, 2026-10-26).")]
        public float matchSeconds = 360f;
        [Tooltip("How long the final score stays on screen before going back, real seconds.")]
        public float endBannerSeconds = 3f;

        DrillDirector director;
        float played;
        bool confirming, ended;
        float endedAt;
        GUIStyle big, mid;

        /// <summary>The chemistry the match earned goes to the career side as it happens (MatchHandoff.chemistryDeltas).</summary>
        public static void Earn(Transform a, Transform b, int delta)
        {
            if (!MatchHandoff.requested || a == null || b == null || a == b) return;
            MatchHandoff.chemistryDeltas.Add(new ChemistryDelta { slotA = SlotOf(a), slotB = SlotOf(b), delta = delta });
        }

        static string SlotOf(Transform t)
        {
            return t.GetComponent<GoalkeeperAI>() != null ? "GK" : Formation.RoleOf(t).ToString();
        }

        // who had the ball before the man who has it now, if he is one of ours and nobody else touched it between
        Transform shooter, passer;
        float passedAt = -99f;
        int seenGoals;

        /// <summary>A goal of ours: the man who shot and the one who gave it to him get +2 (user: "골 합작").</summary>
        void WatchCombination()
        {
            if (director == null || director.ball == null || director.possession == null) return;
            Transform now = director.ball.CarrierTransform;
            if (now != null && now != shooter)
            {
                Side? side = director.possession.SideOf(now);
                if (side == Side.Home)
                {
                    bool chain = shooter != null && director.possession.SideOf(shooter) == Side.Home;
                    passer = chain ? shooter : null;
                    passedAt = Time.time;
                }
                else passer = null;
                shooter = now;
            }
            if (director.HomeGoals > seenGoals)
            {
                if (shooter != null && passer != null && Time.time - passedAt < 10f) Earn(shooter, passer, 2);
                shooter = passer = null;
            }
            seenGoals = director.HomeGoals;
        }

        public float Played { get { return played; } }
        public bool Ended { get { return ended; } }

        void Start()
        {
            director = GetComponent<DrillDirector>();
            Time.timeScale = 1f;
        }

        void Update()
        {
            if (ended)
            {
                if (Time.realtimeSinceStartup - endedAt >= endBannerSeconds) GoBack();
                return;
            }
            if (ProtoInput.EscapePressed()) SetConfirming(!confirming);
            if (confirming) return;

            WatchCombination();
            played += Time.deltaTime;
            if (played >= matchSeconds) End();
        }

        void SetConfirming(bool on)
        {
            confirming = on;
            Time.timeScale = on ? 0f : 1f;
        }

        /// <summary>Writes the score the match stands at into the handoff - the human's side is home.</summary>
        void End()
        {
            if (ended) return;
            ended = true;
            endedAt = Time.realtimeSinceStartup;
            confirming = false;
            Time.timeScale = 0f;
            MatchHandoff.clubGoals = director != null ? director.HomeGoals : 0;
            MatchHandoff.opponentGoals = director != null ? director.AwayGoals : 0;
            MatchHandoff.finished = true;
        }

        void GoBack()
        {
            Time.timeScale = 1f;
            System.Action back = MatchHandoff.returnToCaller;
            enabled = false;
            if (back != null) back();
        }

        void OnDestroy()
        {
            // leaving the scene by any road must not leave the game frozen
            Time.timeScale = 1f;
        }

        void OnGUI()
        {
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                mid = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
                big.normal.textColor = mid.normal.textColor = Color.white;
            }
            int w = Screen.width, h = Screen.height;
            int me = director != null ? director.HomeGoals : 0, they = director != null ? director.AwayGoals : 0;
            int left = Mathf.Max(0, Mathf.CeilToInt(matchSeconds - played));

            GUI.Box(new Rect(w / 2f - 230, 8, 460, 70), GUIContent.none);
            GUI.Label(new Rect(w / 2f - 230, 10, 460, 34),
                      string.Format("우리 {0} : {1} {2}   {3}:{4:00}", me, they, MatchHandoff.opponentName, left / 60, left % 60), big);
            GUI.Label(new Rect(w / 2f - 230, 46, 460, 26),
                      string.Format("{0}  ·  {1}  ·  Esc 경기 끝내기", MatchHandoff.competition, MatchHandoff.isHome ? "홈" : "원정"), mid);

            if (confirming)
            {
                var box = new Rect(w / 2f - 190, h / 2f - 70, 380, 140);
                GUI.Box(box, GUIContent.none);
                GUI.Label(new Rect(box.x, box.y + 10, box.width, 50),
                          string.Format("지금 점수로 경기를 끝낼까요?\n우리 {0} : {1} {2}", me, they, MatchHandoff.opponentName), mid);
                if (GUI.Button(new Rect(box.x + 30, box.y + 80, 150, 40), "끝내기")) End();
                if (GUI.Button(new Rect(box.x + 200, box.y + 80, 150, 40), "계속")) SetConfirming(false);
            }
            else if (ended)
            {
                GUI.Box(new Rect(w / 2f - 200, h / 2f - 50, 400, 100), GUIContent.none);
                GUI.Label(new Rect(w / 2f - 200, h / 2f - 40, 400, 80),
                          string.Format("경기 종료\n우리 {0} : {1} {2}", MatchHandoff.clubGoals, MatchHandoff.opponentGoals, MatchHandoff.opponentName), big);
            }
        }
    }
}
