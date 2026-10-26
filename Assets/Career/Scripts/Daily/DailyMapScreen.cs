using System.Collections.Generic;
using Prototype;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Career
{
    /// <summary>
    /// The daily scene (user, 2026-10-25): a map of the town with the places to go - training
    /// ground, club, home, cafe, restaurant, plaza, department store, car showroom
    /// (<see cref="PlaceCatalog"/>). A place opens a small panel with what can be done there;
    /// picking an action does it at once (<see cref="DailyLife"/>) - points, time and energy go,
    /// and a special event may open a result page. The right column logs what was done today and
    /// what the night's sleep would give. What actions do besides energy, and money, are not
    /// built (PROJECT.md 10.10 - 10.12). Days come from the season's calendar (<see cref="Season"/>):
    /// the next match - date, rival, competition - is shown under the top bar, with a panel for
    /// the coming matches and the league table. On match day there are two buttons: simulate it
    /// (a result drawn from the clubs' strength) or play it by hand - that opens the match
    /// scene and takes the result back (<see cref="MatchHandoff"/>, PROJECT.md 10.13, 10.14).
    ///
    /// uGUI built in code when the scene starts, like the creation screen; the scene holds the
    /// camera, the event system and this component. Placeholder art: coloured blocks for the
    /// districts and a lettered square for each place.
    /// </summary>
    public class DailyMapScreen : MonoBehaviour
    {
        [Tooltip("Season day the clock starts on. 0 or 1 = the day after the club's first match (there is no save yet).")]
        public int day = 1;
        [Tooltip("Energy a match leaves, %. What a match leaves is not connected to the match yet - a placeholder (PROJECT.md 10.11).")]
        [Range(0f, 100f)] public float energyAfterMatch = 40f;
        [Tooltip("Place in last season's table of the club to start at, when there is no saved character (a saved one picked its club at creation, PROJECT.md 10.14).")]
        [Range(1, 20)] public int lastSeasonRank = 9;
        [Tooltip("Seed of the draws and drawn results, so a run can be repeated.")]
        public int seasonSeed = 2026;

        const string MatchScene = "Assets/_Prototype/Scenes/ScanAndTurnDrill.unity";
        const string ReturnScene = "Assets/Career/Scenes/DailyMap.unity";

        DailyLife life;
        Season season;
        bool showStandings;
        bool savedPlayer;

        Font font;
        RectTransform panel, partsBox, nightChoice, eventPanel, schedulePanel, scheduleBody, matchBox;
        Text panelName, panelBlurb, statusText, dateText, matchText, clockText, energyText, pointsText, nightText, nightNowLabel, nightOnLabel, sleepText, eventTitle, eventBody, nextMatchText, scheduleHead;
        Button skipButton, standingsTab, fixturesTab;
        RectTransform actionList;
        readonly Dictionary<string, Image> frames = new Dictionary<string, Image>();
        readonly List<KeyValuePair<Button, PlaceAction>> actionButtons = new List<KeyValuePair<Button, PlaceAction>>();
        Place selected;

        static readonly Color Bar = new Color(0.09f, 0.11f, 0.14f, 1f);
        static readonly Color Row = new Color(0.16f, 0.19f, 0.23f, 1f);
        static readonly Color Dim = new Color(0.62f, 0.65f, 0.70f, 1f);
        static readonly Color Gold = new Color(0.95f, 0.82f, 0.45f, 1f);
        static readonly Color Bad = new Color(0.95f, 0.45f, 0.40f, 1f);

        public Place Selected { get { return selected; } }
        public DailyLife Life { get { return life; } }
        /// <summary>The result page of an event is up and waits for a click.</summary>
        public bool EventOpen { get { return eventPanel != null && eventPanel.gameObject.activeSelf; } }
        public Season Season { get { return season; } }
        public bool ScheduleOpen { get { return schedulePanel != null && schedulePanel.gameObject.activeSelf; } }

        void Start()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            if (!CareerSession.Active) NewCareer();
            life = CareerSession.life;
            season = CareerSession.season;
            savedPlayer = CareerSession.savedPlayer;
            BuildUi();
            Close();
            string back = ReturnFromMatch();
            statusText.text = back.Length > 0 ? back
                : "경기 끝 체력 " + Mathf.RoundToInt(energyAfterMatch) + "% → 아침 " + Mathf.RoundToInt(life.Energy) + "%, 행동력 " + life.clock.points + "P. 장소를 눌러 할 일을 고른다.";
            Refresh();
        }

        /// <summary>The first start: the saved character (or a new one) at the club it picked, a season, daily life from the day after its first match.</summary>
        void NewCareer()
        {
            PlayerProfile player;
            bool saved = CharacterStore.TryLoad(out player);
            if (!saved) { player = new PlayerProfile(); player.clubRank = lastSeasonRank; }
            Squad squad = Squad.Provisional(player.displayName);
            Season first = Season.First(player.clubRank, seasonSeed);
            first.SetPlayerClub(CreationRules.Overall(player), squad.TeamChemistry);
            // the game opens the day after the club's first match, whose result is drawn
            int start = day > 1 ? day : first.NextFixture(ClubPool.PlayerId, 1).day + 1;
            first.AdvanceTo(start - 1);
            Fixture next = first.NextFixture(ClubPool.PlayerId, start);
            CareerSession.season = first;
            CareerSession.life = new DailyLife(player, squad, energyAfterMatch, next.day - start, start);
            CareerSession.savedPlayer = saved;
        }

        /// <summary>Team strength is the squad's overall plus its chemistry, read fresh before any result is drawn (user, 2026-10-26).</summary>
        void SyncStrength()
        {
            season.SetPlayerClub(CreationRules.Overall(life.player), life.squad.TeamChemistry);
        }

        /// <summary>Back from the match scene with a result: it goes into the season, and daily life goes on from the next day.</summary>
        string ReturnFromMatch()
        {
            Fixture f = CareerSession.pendingFixture;
            if (f == null || !MatchHandoff.finished) return "";
            CareerSession.pendingFixture = null;
            bool home = f.home == ClubPool.PlayerId;
            int homeGoals = home ? MatchHandoff.clubGoals : MatchHandoff.opponentGoals;
            int awayGoals = home ? MatchHandoff.opponentGoals : MatchHandoff.clubGoals;
            MatchHandoff.Clear();
            SyncStrength();
            season.AdvanceTo(f.day - 1);
            season.SetResult(f, homeGoals, awayGoals);
            return FinishMatch(f);
        }

        // ------------------------------------------------------------ actions --

        /// <summary>Opens the panel for a place (what a click on its marker does).</summary>
        public void Open(string placeId)
        {
            Place p = PlaceCatalog.Get(placeId);
            if (p == null) return;
            selected = p;
            panel.gameObject.SetActive(true);
            // on the far side of the map from the place, so it never covers its own marker
            bool right = p.mapPos.x < 0.5f;
            panel.anchorMin = new Vector2(right ? 1f : 0f, 0f);
            panel.anchorMax = new Vector2(right ? 1f : 0f, 1f);
            panel.offsetMin = right ? new Vector2(-500f, 16f) : new Vector2(16f, 16f);
            panel.offsetMax = right ? new Vector2(-16f, -16f) : new Vector2(500f, -16f);
            panelName.text = p.name;
            panelBlurb.text = p.blurb;
            ClearChildren(actionList);
            actionButtons.Clear();
            foreach (PlaceAction a in p.actions)
            {
                PlaceAction act = a;
                Button b = Btn(actionList, "", 19, () => DoAction(selected.id, act.name));
                b.name = "Action_" + act.name;
                b.gameObject.AddComponent<LayoutElement>().minHeight = 48f;
                actionButtons.Add(new KeyValuePair<Button, PlaceAction>(b, act));
            }
            foreach (KeyValuePair<string, Image> f in frames) f.Value.enabled = f.Key == p.id;
            Refresh();
        }

        public void Close()
        {
            selected = null;
            panel.gameObject.SetActive(false);
            actionButtons.Clear();
            foreach (Image f in frames.Values) f.enabled = false;
        }

        /// <summary>Does an action now. A special event opens its page over the map.</summary>
        public void DoAction(string placeId, string actionName)
        {
            if (EventOpen) return;
            string msg = life.Do(placeId, actionName);
            if (msg.Length > 0) statusText.text = msg;
            if (life.LastEvent != null) ShowEvent(life.LastEvent);
            Refresh();
        }

        /// <summary>Match day, the first button: the match is drawn from the clubs' strength, and daily life goes on.</summary>
        public void SimulateMatch()
        {
            if (EventOpen || ScheduleOpen || !life.IsMatchDay) return;
            Fixture f = season.NextFixture(ClubPool.PlayerId, life.clock.day);
            if (f != null)
            {
                SyncStrength();
                season.AdvanceTo(f.day);
            }
            statusText.text = FinishMatch(f);
            Refresh();
        }

        /// <summary>
        /// Match day, the second button: hands the match to the match scene and opens it. The
        /// scene writes the result into <see cref="MatchHandoff"/> and returns here (its part).
        /// </summary>
        public void PlayMatchLive()
        {
            if (EventOpen || ScheduleOpen || !life.IsMatchDay) return;
            Fixture f = season.NextFixture(ClubPool.PlayerId, life.clock.day);
            if (f == null) return;
            CareerSession.season = season;
            CareerSession.life = life;
            CareerSession.savedPlayer = savedPlayer;
            CareerSession.pendingFixture = f;
            MatchHandoff.Request(season.ClubName(f.Other(ClubPool.PlayerId)), Competitions.Name(f.comp), f.home == ClubPool.PlayerId, ReturnScene);
            // the match side goes back through this, so it never calls the career code (Request clears it)
            MatchHandoff.returnToCaller = () => CareerSession.Open(ReturnScene);
            MatchHandoff.returnToCaller = () => CareerSession.Open(ReturnScene);
            CareerSession.Open(MatchScene);
        }

        /// <summary>
        /// After a match (drawn or played): says what it was, then starts daily life again the
        /// day after. When the club has no match left the season is finished and the next one opens
        /// at its day 1, the new places in hand.
        /// </summary>
        string FinishMatch(Fixture f)
        {
            string me = ClubPool.PlayerId;
            string result = "";
            if (f != null)
                result = Competitions.Name(f.comp) + " " + f.round + ": " + season.ClubName(f.home) + " " + f.homeGoals + "-" + f.awayGoals + " " + season.ClubName(f.away)
                         + (f.penWinner != null ? " (승부차기 " + season.ClubName(f.penWinner) + ")" : "") + ".  ";
            int firstDay = life.clock.day + 1;
            Fixture next = season.NextFixture(me, firstDay);
            if (next == null)
            {
                for (int i = 0; i < 20 && !season.IsOver; i++) season.AdvanceTo(season.LastDay);
                int ended = season.number;
                season = season.Next(seasonSeed + ended);
                SyncStrength();
                firstDay = 1;
                next = season.NextFixture(me, firstDay);
                result += "시즌 " + ended + " 끝. ";
            }
            life = new DailyLife(life.player, life.squad, energyAfterMatch, next.day - firstDay, firstDay);
            CareerSession.season = season;
            CareerSession.life = life;
            return result + "다음 경기까지 " + life.DaysToMatch + "일.";
        }

        /// <summary>Opens the panel of the coming matches (or the league table).</summary>
        public void OpenSchedule(bool standings)
        {
            showStandings = standings;
            schedulePanel.gameObject.SetActive(true);
            Refresh();
        }

        public void CloseSchedule()
        {
            schedulePanel.gameObject.SetActive(false);
            Refresh();
        }

        public void SkipPart()
        {
            if (EventOpen || ScheduleOpen) return;
            statusText.text = life.SkipPart();
            Refresh();
        }

        /// <summary>The answer at the end of the evening: true = straight to bed, false = on into the night.</summary>
        public void ChooseNight(bool sleepNow)
        {
            statusText.text = life.ChooseNight(sleepNow);
            Refresh();
        }

        /// <summary>Closes the result page; the evening's question, if it was waiting behind it, shows next.</summary>
        public void CloseEvent()
        {
            eventPanel.gameObject.SetActive(false);
            Refresh();
        }

        void ShowEvent(EventResult r)
        {
            eventTitle.text = r.title;
            string body = string.Join("\n\n", r.pages.ToArray());
            if (r.effects.Count > 0) body += "\n\n<color=#f2a09a>" + string.Join("   ", r.effects.ToArray()) + "</color>";
            eventBody.text = body;
            eventPanel.gameObject.SetActive(true);
        }

        // ------------------------------------------------------------ refresh --

        void Refresh()
        {
            DayClock c = life.clock;
            // everything before today is played; the calendar may have drawn a round, so the match day is read again
            SyncStrength();
            season.AdvanceTo(c.day - 1);
            Fixture nextMatch = season.NextFixture(ClubPool.PlayerId, c.day);
            if (nextMatch != null && nextMatch.day != life.matchDay) life.SetMatchDay(nextMatch.day);
            dateText.text = "시즌 " + season.number + " · " + season.DateText(c.day);
            matchText.text = life.IsMatchDay ? "경기 날" : "경기까지 " + life.DaysToMatch + "일";
            nextMatchText.text = nextMatch == null ? "남은 경기 없음" : "다음 경기  " + season.Describe(nextMatch, ClubPool.PlayerId);
            clockText.text = c.Time + "  " + DayClock.PartName(c.part);
            energyText.text = "체력 " + Mathf.RoundToInt(life.Energy) + "%";
            pointsText.text = "행동력 " + c.points + "P";
            skipButton.interactable = !life.AwaitingNightChoice && !life.IsMatchDay && !EventOpen && !ScheduleOpen;
            matchBox.gameObject.SetActive(life.IsMatchDay);
            nightChoice.gameObject.SetActive(life.AwaitingNightChoice && !EventOpen);
            if (ScheduleOpen) RebuildSchedule();
            if (life.AwaitingNightChoice)
            {
                nightText.text = "저녁 행동력을 다 썼다. 지금 " + life.clock.Time + ", 체력 " + Mathf.RoundToInt(life.Energy) + "%.";
                nightNowLabel.text = "바로 잔다\n<size=15>수면 " + DailyLife.Hours(life.SleepHoursIfNow) + " → 아침 회복 +" + Mathf.RoundToInt(life.MorningRecovery(life.SleepHoursIfNow)) + "%p</size>";
                nightOnLabel.text = "새벽으로 넘어간다\n<size=15>새벽 " + DayClock.PointsFor(life.Energy) + "P</size>";
            }

            foreach (KeyValuePair<Button, PlaceAction> ab in actionButtons)
            {
                PlaceAction a = life.Effective(selected.id, ab.Value);
                bool blocked = a.minutes != ab.Value.minutes || a.energy != ab.Value.energy;
                string detail;
                if (a.sleeps)
                {
                    float h = life.SleepHoursIfNow;
                    detail = "잠든 시각부터 06:00까지 · 지금 자면 " + DailyLife.Hours(h) + " → 아침 회복 +" + Mathf.RoundToInt(life.MorningRecovery(h)) + "%p"
                             + (life.CanDo(selected.id, a) ? "" : a.name == PlaceCatalog.SleepNight ? " (새벽에만)" : " (저녁에만)");
                }
                else
                {
                    float after = Mathf.Clamp(life.Energy + a.energy, 0f, 100f);
                    detail = Duration(a.minutes) + " · " + a.Cost + "P · 체력 " + Signed(a.energy) + "% → 끝나면 " + Mathf.RoundToInt(after) + "%"
                             + (a.morningBonus > 0f ? " · 내일 아침 +" + a.morningBonus + "%p" : "");
                }
                ab.Key.GetComponentInChildren<Text>().text = a.name + "  <size=15><color=#b8c0cc>" + detail + "</color></size>"
                    + (blocked ? "  <size=14><color=#f2a09a>입장 불가</color></size>" : "")
                    + (a.chemistry ? "  <size=14><color=#9fd7a8>친밀도</color></size>" : "");
                ab.Key.interactable = !EventOpen && life.CanDo(selected.id, ab.Value);
            }
            RebuildToday();
        }

        /// <summary>The panel's body: the club's next matches, or the league table with the club's row in gold.</summary>
        void RebuildSchedule()
        {
            ClearChildren(scheduleBody);
            string me = ClubPool.PlayerId;
            var names = new List<string>();
            foreach (CompetitionId id in season.CompetitionsOf(me)) names.Add(Competitions.Name(id));
            scheduleHead.text = "시즌 " + season.number + " · 참가: " + string.Join(", ", names.ToArray());
            fixturesTab.GetComponent<Image>().color = showStandings ? Row : new Color(0.25f, 0.45f, 0.75f);
            standingsTab.GetComponent<Image>().color = showStandings ? new Color(0.25f, 0.45f, 0.75f) : Row;
            if (!showStandings)
            {
                int n = 0;
                foreach (Fixture f in season.Upcoming(me, life.clock.day, 12))
                {
                    TableLine(scheduleBody, new[] { season.DateText(f.day), Competitions.Name(f.comp), f.round, f.neutral ? "중립" : f.home == me ? "홈" : "원정", season.ClubName(f.Other(me)) },
                        new[] { 120f, 170f, 150f, 70f, 330f }, n == 0 ? Gold : Color.white, 30f);
                    n++;
                }
                if (n == 0) Label(scheduleBody, "남은 경기가 없다.", 20, Dim, TextAnchor.MiddleLeft);
                Label(scheduleBody, "토너먼트 라운드는 앞 라운드가 끝나야 상대가 정해져 일정에 나타난다.", 15, Dim, TextAnchor.MiddleLeft)
                    .gameObject.AddComponent<LayoutElement>().minHeight = 26f;
                return;
            }
            TableLine(scheduleBody, new[] { "#", "구단", "경기", "승", "무", "패", "득실", "승점" }, new[] { 40f, 330f, 70f, 60f, 60f, 60f, 110f, 80f }, Dim, 28f);
            int rank = 1;
            foreach (TableRow r in season.Table(CompetitionId.League))
            {
                TableLine(scheduleBody, new[] { rank.ToString(), season.ClubName(r.club), r.played.ToString(), r.won.ToString(), r.drawn.ToString(), r.lost.ToString(),
                    r.goalsFor + "-" + r.goalsAgainst, r.Points.ToString() },
                    new[] { 40f, 330f, 70f, 60f, 60f, 60f, 110f, 80f }, r.club == me ? Gold : Color.white, 26f);
                rank++;
            }
        }

        void TableLine(RectTransform parent, string[] cells, float[] widths, Color col, float height)
        {
            var row = new GameObject("Line", typeof(RectTransform)).GetComponent<RectTransform>();
            row.SetParent(parent, false);
            row.gameObject.AddComponent<LayoutElement>().minHeight = height;
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6f;
            h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            for (int i = 0; i < cells.Length; i++)
            {
                Text t = Label(row, cells[i], 18, col, TextAnchor.MiddleLeft);
                LayoutElement le = t.gameObject.AddComponent<LayoutElement>();
                le.minWidth = le.preferredWidth = widths[i];
                le.minHeight = height;
            }
        }

        /// <summary>Today's four parts with what was done in each, and what sleeping now would give.</summary>
        void RebuildToday()
        {
            ClearChildren(partsBox);
            int now = (int)life.clock.part;
            for (int p = 0; p < 4; p++)
            {
                DayPart part = (DayPart)p;
                string head = DayClock.PartName(part) + " " + DayClock.StartHour(part).ToString("00") + "~" + (DayClock.StartHour(part) + 6).ToString("00");
                Color hc = Dim;
                if (life.IsMatchDay) head += "  ·  경기 날";
                else if (p < now) head += "  ·  끝";
                else if (p == now) { head = "▶ " + head + "  ·  " + life.clock.points + "P"; hc = Gold; }
                Text h = Label(partsBox, head, 18, hc, TextAnchor.MiddleLeft);
                h.gameObject.AddComponent<LayoutElement>().minHeight = 28f;
                foreach (string line in life.log[p])
                {
                    Text t = Label(partsBox, "✓ " + line, 17, Dim, TextAnchor.MiddleLeft);
                    t.gameObject.AddComponent<LayoutElement>().minHeight = 26f;
                }
            }
            if (life.IsMatchDay) { sleepText.text = ""; return; }
            float hours = life.SleepHoursIfNow;
            if (life.clock.part == DayPart.Morning || life.clock.part == DayPart.Afternoon)
            {
                sleepText.text = "잠은 저녁부터 잘 수 있다.\n지난밤 수면 " + DailyLife.Hours(life.LastSleepHours)
                                 + (life.RecoveryBonus > 0f ? "\n내일 아침 회복 훈련 +" + life.RecoveryBonus + "%p" : "");
                return;
            }
            sleepText.text = "지금 자면  " + DailyLife.Hours(hours) + " · 수면 ×" + DailyLife.SleepMultiplier(hours).ToString("0.00")
                             + "\n아침 회복 +" + Mathf.RoundToInt(life.MorningRecovery(hours)) + "%p"
                             + (life.RecoveryBonus > 0f ? "  (회복 훈련 +" + life.RecoveryBonus + "%p 포함)" : "")
                             + "\n지난밤 수면 " + DailyLife.Hours(life.LastSleepHours);
        }

        static string Duration(int minutes)
        {
            int h = minutes / 60, m = minutes % 60;
            if (h == 0) return m + "분";
            return m == 0 ? h + "시간" : h + "시간 " + m + "분";
        }

        static string Signed(float v) { return v > 0f ? "+" + v : v.ToString(); }

        static void ClearChildren(Transform t)
        {
            // hidden at once so a Find in the same frame does not see them, gone at the end of it
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                GameObject g = t.GetChild(i).gameObject;
                g.SetActive(false);
                g.transform.SetParent(null, false);
                Destroy(g);
            }
        }

        // ------------------------------------------------------------ layout --

        void BuildUi()
        {
            var canvasGo = new GameObject("DailyCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            RectTransform root = canvasGo.GetComponent<RectTransform>();

            // top bar: when, how much is left, who
            RectTransform top = Box(root, "TopBar", new Vector2(0f, 1f), Vector2.one, new Vector2(20f, -96f), new Vector2(-20f, -16f), Bar);
            Label(top, "일상", 30, Color.white, TextAnchor.MiddleLeft, Vector2.zero, new Vector2(0.07f, 1f), new Vector2(24f, 0f), Vector2.zero);
            dateText = Label(top, "", 22, Color.white, TextAnchor.MiddleCenter, new Vector2(0.07f, 0f), new Vector2(0.24f, 1f), Vector2.zero, Vector2.zero);
            matchText = Label(top, "", 20, Dim, TextAnchor.MiddleCenter, new Vector2(0.24f, 0f), new Vector2(0.34f, 1f), Vector2.zero, Vector2.zero);
            clockText = Label(top, "", 26, Gold, TextAnchor.MiddleCenter, new Vector2(0.34f, 0f), new Vector2(0.46f, 1f), Vector2.zero, Vector2.zero);
            energyText = Label(top, "", 24, Color.white, TextAnchor.MiddleCenter, new Vector2(0.46f, 0f), new Vector2(0.56f, 1f), Vector2.zero, Vector2.zero);
            pointsText = Label(top, "", 24, Color.white, TextAnchor.MiddleCenter, new Vector2(0.56f, 0f), new Vector2(0.66f, 1f), Vector2.zero, Vector2.zero);
            skipButton = Btn(top, "다음 시간대로", 20, SkipPart);
            skipButton.name = "NextPart";
            Anchor(skipButton, new Vector2(0.67f, 0.18f), new Vector2(0.78f, 0.82f));
            Label(top, PlayerLine(), 18, Dim, TextAnchor.MiddleRight, new Vector2(0.78f, 0f), Vector2.one, Vector2.zero, new Vector2(-24f, 0f));

            // the next match, and the calendar behind it
            RectTransform info = Box(root, "NextMatch", new Vector2(0f, 1f), Vector2.one, new Vector2(20f, -150f), new Vector2(-20f, -104f), Bar);
            nextMatchText = Label(info, "", 22, Gold, TextAnchor.MiddleLeft, Vector2.zero, new Vector2(0.72f, 1f), new Vector2(24f, 0f), Vector2.zero);
            nextMatchText.name = "NextMatchText";
            Button sched = Btn(info, "일정", 20, () => OpenSchedule(false));
            sched.name = "OpenSchedule";
            Anchor(sched, new Vector2(0.74f, 0.1f), new Vector2(0.86f, 0.9f));
            Button table = Btn(info, "순위표", 20, () => OpenSchedule(true));
            table.name = "OpenStandings";
            Anchor(table, new Vector2(0.87f, 0.1f), new Vector2(0.99f, 0.9f));

            statusText = Label(root, "", 18, Dim, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(1f, 0f), new Vector2(20f, 8f), new Vector2(-20f, 48f));

            // the map, three by two blocks between the roads, one district each
            RectTransform map = Box(root, "Map", Vector2.zero, Vector2.one, new Vector2(20f, 56f), new Vector2(-540f, -160f), new Color(0.07f, 0.08f, 0.08f));
            Zone(map, "운동 지구", new Vector2(0.01f, 0.48f), new Vector2(0.315f, 0.98f), new Color(0.10f, 0.20f, 0.11f));
            Zone(map, "도심", new Vector2(0.345f, 0.48f), new Vector2(0.645f, 0.98f), new Color(0.15f, 0.15f, 0.17f));
            Zone(map, "상업 지구", new Vector2(0.675f, 0.48f), new Vector2(0.99f, 0.98f), new Color(0.12f, 0.13f, 0.20f));
            Zone(map, "공원", new Vector2(0.01f, 0.02f), new Vector2(0.315f, 0.42f), new Color(0.09f, 0.17f, 0.10f));
            Zone(map, "광장 거리", new Vector2(0.345f, 0.02f), new Vector2(0.645f, 0.42f), new Color(0.20f, 0.20f, 0.21f));
            Zone(map, "주거 지구", new Vector2(0.675f, 0.02f), new Vector2(0.99f, 0.42f), new Color(0.19f, 0.16f, 0.11f));
            foreach (Place p in PlaceCatalog.All) Marker(map, p);

            // today: what was done, and the sleep
            RectTransform plan = Box(root, "Plan", new Vector2(1f, 0f), Vector2.one, new Vector2(-520f, 56f), new Vector2(-20f, -160f), Bar);
            var pv = plan.gameObject.AddComponent<VerticalLayoutGroup>();
            pv.padding = new RectOffset(20, 20, 16, 16);
            pv.spacing = 6f;
            pv.childControlWidth = true; pv.childControlHeight = true; pv.childForceExpandHeight = false;
            Label(plan, "오늘", 26, Color.white, TextAnchor.MiddleLeft).gameObject.AddComponent<LayoutElement>().minHeight = 36f;
            partsBox = new GameObject("Parts", typeof(RectTransform)).GetComponent<RectTransform>();
            partsBox.SetParent(plan, false);
            var bv = partsBox.gameObject.AddComponent<VerticalLayoutGroup>();
            bv.spacing = 4f;
            bv.childControlWidth = true; bv.childControlHeight = true; bv.childForceExpandHeight = false;
            var gap = new GameObject("Spacer", typeof(RectTransform)).AddComponent<LayoutElement>();
            gap.transform.SetParent(plan, false);
            gap.flexibleHeight = 1f;
            // match day: simulate it or play it by hand
            matchBox = new GameObject("MatchBox", typeof(RectTransform)).GetComponent<RectTransform>();
            matchBox.SetParent(plan, false);
            var mv = matchBox.gameObject.AddComponent<VerticalLayoutGroup>();
            mv.spacing = 8f;
            mv.childControlWidth = true; mv.childControlHeight = true; mv.childForceExpandHeight = false;
            Label(matchBox, "경기 날", 22, Gold, TextAnchor.MiddleLeft).gameObject.AddComponent<LayoutElement>().minHeight = 32f;
            Button sim = Btn(matchBox, "시뮬레이션 돌리기", 22, SimulateMatch);
            sim.name = "Simulate";
            sim.gameObject.AddComponent<LayoutElement>().minHeight = 52f;
            Button live = Btn(matchBox, "직접 플레이하기", 22, PlayMatchLive);
            live.name = "PlayLive";
            live.gameObject.AddComponent<LayoutElement>().minHeight = 52f;
            sleepText = Label(plan, "", 18, Gold, TextAnchor.UpperLeft);
            sleepText.name = "Sleep";
            sleepText.gameObject.AddComponent<LayoutElement>().minHeight = 84f;

            // the panel a place opens, inside the map
            panel = Box(map, "PlacePanel", new Vector2(1f, 0f), Vector2.one, new Vector2(-500f, 16f), new Vector2(-16f, -16f), Bar);
            var v = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(24, 24, 20, 20);
            v.spacing = 10f;
            v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandHeight = false;
            panelName = Label(panel, "", 30, Color.white, TextAnchor.MiddleLeft);
            panelName.gameObject.AddComponent<LayoutElement>().minHeight = 40f;
            panelBlurb = Label(panel, "", 18, Dim, TextAnchor.UpperLeft);
            panelBlurb.gameObject.AddComponent<LayoutElement>().minHeight = 30f;
            Label(panel, "할 수 있는 일 — 누르면 바로 한다", 18, Gold, TextAnchor.MiddleLeft).gameObject.AddComponent<LayoutElement>().minHeight = 30f;
            actionList = new GameObject("Actions", typeof(RectTransform)).GetComponent<RectTransform>();
            actionList.SetParent(panel, false);
            var av = actionList.gameObject.AddComponent<VerticalLayoutGroup>();
            av.spacing = 8f;
            av.childControlWidth = true; av.childControlHeight = true; av.childForceExpandHeight = false;
            Note(panel, "30분에 1P. 체력 말고 다른 효과와 돈은 아직 없다.");
            var spacer = new GameObject("Spacer", typeof(RectTransform)).AddComponent<LayoutElement>();
            spacer.transform.SetParent(panel, false);
            spacer.flexibleHeight = 1f;
            Button close = Btn(panel, "닫기", 20, Close);
            close.name = "ClosePanel";
            close.gameObject.AddComponent<LayoutElement>().minHeight = 48f;

            // the end of the evening: bed or the night (user), over everything until answered
            nightChoice = Box(root, "NightChoice", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.55f));
            RectTransform card = Box(nightChoice, "Card", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-380f, -150f), new Vector2(380f, 150f), Bar);
            Label(card, "저녁이 끝났다", 30, Gold, TextAnchor.MiddleCenter, new Vector2(0f, 0.72f), Vector2.one, Vector2.zero, new Vector2(0f, -10f));
            nightText = Label(card, "", 20, Color.white, TextAnchor.MiddleCenter, new Vector2(0f, 0.52f), new Vector2(1f, 0.72f), Vector2.zero, Vector2.zero);
            Button now = Btn(card, "", 22, () => ChooseNight(true));
            now.name = "SleepNow";
            nightNowLabel = now.GetComponentInChildren<Text>();
            Anchor(now, new Vector2(0.04f, 0.08f), new Vector2(0.48f, 0.46f));
            Button on = Btn(card, "", 22, () => ChooseNight(false));
            on.name = "IntoNight";
            Anchor(on, new Vector2(0.52f, 0.08f), new Vector2(0.96f, 0.46f));
            nightOnLabel = on.GetComponentInChildren<Text>();

            // the coming matches and the league table
            schedulePanel = Box(root, "SchedulePanel", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.6f));
            RectTransform sc = Box(schedulePanel, "Card", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-480f, -400f), new Vector2(480f, 400f), Bar);
            var sv = sc.gameObject.AddComponent<VerticalLayoutGroup>();
            sv.padding = new RectOffset(28, 28, 22, 22);
            sv.spacing = 8f;
            sv.childControlWidth = true; sv.childControlHeight = true; sv.childForceExpandHeight = false;
            scheduleHead = Label(sc, "", 22, Color.white, TextAnchor.MiddleLeft);
            scheduleHead.gameObject.AddComponent<LayoutElement>().minHeight = 34f;
            var tabs = new GameObject("Tabs", typeof(RectTransform)).GetComponent<RectTransform>();
            tabs.SetParent(sc, false);
            tabs.gameObject.AddComponent<LayoutElement>().minHeight = 44f;
            var th = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
            th.spacing = 8f;
            th.childControlWidth = true; th.childControlHeight = true; th.childForceExpandWidth = true; th.childForceExpandHeight = false;
            fixturesTab = Btn(tabs, "다음 경기", 20, () => OpenSchedule(false));
            fixturesTab.name = "TabFixtures";
            fixturesTab.gameObject.AddComponent<LayoutElement>().minHeight = 44f;
            standingsTab = Btn(tabs, "리그 순위", 20, () => OpenSchedule(true));
            standingsTab.name = "TabStandings";
            standingsTab.gameObject.AddComponent<LayoutElement>().minHeight = 44f;
            scheduleBody = new GameObject("Body", typeof(RectTransform)).GetComponent<RectTransform>();
            scheduleBody.SetParent(sc, false);
            var sb = scheduleBody.gameObject.AddComponent<VerticalLayoutGroup>();
            sb.spacing = 4f;
            sb.childControlWidth = true; sb.childControlHeight = true; sb.childForceExpandHeight = false;
            var sspace = new GameObject("Spacer", typeof(RectTransform)).AddComponent<LayoutElement>();
            sspace.transform.SetParent(sc, false);
            sspace.flexibleHeight = 1f;
            Button sclose = Btn(sc, "닫기", 20, CloseSchedule);
            sclose.name = "CloseSchedule";
            sclose.gameObject.AddComponent<LayoutElement>().minHeight = 48f;
            schedulePanel.gameObject.SetActive(false);

            // the result of an event (user: one page of visual-novel text): a window along the bottom
            eventPanel = Box(root, "EventPanel", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.45f));
            RectTransform win = Box(eventPanel, "Window", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-760f, 60f), new Vector2(760f, 400f), new Color(0.07f, 0.09f, 0.12f, 0.96f));
            RectTransform tag = Box(win, "NameTag", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -22f), new Vector2(284f, 22f), new Color(0.25f, 0.45f, 0.75f));
            eventTitle = Label(tag, "", 24, Color.white, TextAnchor.MiddleCenter);
            Stretch(eventTitle.rectTransform, Vector2.zero, Vector2.zero);
            eventBody = Label(win, "", 24, Color.white, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(40f, 70f), new Vector2(-40f, -40f));
            eventBody.name = "EventBody";
            Button next = Btn(win, "계속 ▶", 22, CloseEvent);
            next.name = "EventNext";
            RectTransform nrt = next.GetComponent<RectTransform>();
            nrt.anchorMin = nrt.anchorMax = new Vector2(1f, 0f);
            nrt.pivot = new Vector2(1f, 0f);
            nrt.sizeDelta = new Vector2(160f, 48f);
            nrt.anchoredPosition = new Vector2(-24f, 16f);
            eventPanel.gameObject.SetActive(false);
        }

        string PlayerLine()
        {
            if (!savedPlayer) return "신인 · 저장된 선수 없음";
            return life.player.displayName + " · " + TalentCatalog.Get(life.player.talent).name + " · " + life.player.age + "세";
        }

        void Zone(RectTransform map, string label, Vector2 aMin, Vector2 aMax, Color col)
        {
            RectTransform z = Box(map, "Zone_" + label, aMin, aMax, Vector2.zero, Vector2.zero, col);
            Label(z, label, 16, new Color(1f, 1f, 1f, 0.45f), TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(12f, 0f), new Vector2(0f, -8f))
                .raycastTarget = false;
            z.GetComponent<Image>().raycastTarget = false;
        }

        /// <summary>A lettered square for the place, with its name under it.</summary>
        void Marker(RectTransform map, Place p)
        {
            var holder = new GameObject("Place_" + p.id, typeof(RectTransform)).GetComponent<RectTransform>();
            holder.SetParent(map, false);
            holder.anchorMin = holder.anchorMax = p.mapPos;
            holder.sizeDelta = new Vector2(84f, 84f);

            Image frame = new GameObject("Frame", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            frame.transform.SetParent(holder, false);
            frame.color = Gold;
            frame.raycastTarget = false;
            Stretch(frame.rectTransform, new Vector2(-6f, -6f), new Vector2(6f, 6f));
            frames[p.id] = frame;

            Button b = Btn(holder, p.icon, 34, () => Open(p.id));
            b.name = "Marker";
            b.GetComponent<Image>().color = p.color;
            Stretch(b.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero);

            Text name = Label(holder, p.name, 20, Color.white, TextAnchor.UpperCenter);
            name.raycastTarget = false;
            name.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            name.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            name.rectTransform.pivot = new Vector2(0.5f, 1f);
            name.rectTransform.sizeDelta = new Vector2(200f, 30f);
            name.rectTransform.anchoredPosition = new Vector2(0f, -10f);
            name.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
        }

        // ------------------------------------------------------------ widgets --

        RectTransform Box(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, Color col)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = col;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
            return rt;
        }

        static void Anchor(Component c, Vector2 aMin, Vector2 aMax)
        {
            var rt = c.GetComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        void Note(RectTransform parent, string s)
        {
            Label(parent, s, 14, Dim, TextAnchor.UpperLeft).gameObject.AddComponent<LayoutElement>().minHeight = 22f;
        }

        Text Label(Transform parent, string s, int size, Color col, TextAnchor a)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font; t.fontSize = size; t.color = col; t.alignment = a; t.text = s;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        Text Label(Transform parent, string s, int size, Color col, TextAnchor a, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            Text t = Label(parent, s, size, col, a);
            t.rectTransform.anchorMin = aMin; t.rectTransform.anchorMax = aMax;
            t.rectTransform.offsetMin = oMin; t.rectTransform.offsetMax = oMax;
            return t;
        }

        Button Btn(Transform parent, string label, int size, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = Row;
            var b = go.GetComponent<Button>();
            ColorBlock cb = b.colors;
            cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            cb.pressedColor = new Color(0.85f, 0.85f, 0.85f);
            cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.45f);
            b.colors = cb;
            b.onClick.AddListener(onClick);
            Text t = Label(go.transform, label, size, Color.white, TextAnchor.MiddleCenter);
            t.raycastTarget = false;
            Stretch(t.rectTransform, Vector2.zero, Vector2.zero);
            return b;
        }

        static void Stretch(RectTransform rt, Vector2 oMin, Vector2 oMax)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = oMin; rt.offsetMax = oMax;
        }
    }
}
