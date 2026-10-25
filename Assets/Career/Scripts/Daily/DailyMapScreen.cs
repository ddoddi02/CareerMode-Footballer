using System.Collections.Generic;
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
    /// picking an action puts it on today's list (<see cref="DailyLife"/>), which is spent on
    /// confirm. The list shows the time, points and energy it will leave, and a day's lists can
    /// be saved as a routine and laid over a later day (PROJECT.md 10.10, 10.11). What actions
    /// do besides energy, and money, are not built.
    ///
    /// uGUI built in code when the scene starts, like the creation screen; the scene holds the
    /// camera, the event system and this component. Placeholder art: coloured blocks for the
    /// districts and a lettered square for each place.
    /// </summary>
    public class DailyMapScreen : MonoBehaviour
    {
        [Tooltip("Day the clock starts on - a placeholder until there is a calendar and a save.")]
        public int day = 1;
        [Tooltip("Energy the last match left, %. No match schedule yet - a placeholder (PROJECT.md 10.11).")]
        [Range(0f, 100f)] public float energyAfterMatch = 40f;
        [Tooltip("Days of daily life before the next match. No match schedule yet - a placeholder.")]
        public int daysToMatch = 6;

        DailyLife life;
        bool savedPlayer;

        Font font;
        RectTransform panel, partsBox;
        Text panelName, panelBlurb, statusText, dateText, matchText, clockText, energyText, pointsText;
        Button confirmButton, routineFillButton;
        RectTransform actionList;
        readonly Dictionary<string, Image> frames = new Dictionary<string, Image>();
        readonly List<KeyValuePair<Button, PlaceAction>> actionButtons = new List<KeyValuePair<Button, PlaceAction>>();
        Place selected;

        static readonly Color Bar = new Color(0.09f, 0.11f, 0.14f, 1f);
        static readonly Color Row = new Color(0.16f, 0.19f, 0.23f, 1f);
        static readonly Color Dim = new Color(0.62f, 0.65f, 0.70f, 1f);
        static readonly Color Gold = new Color(0.95f, 0.82f, 0.45f, 1f);
        static readonly Color Bad = new Color(0.95f, 0.45f, 0.40f, 1f);
        static readonly string[] Weekdays = { "월", "화", "수", "목", "금", "토", "일" };

        public Place Selected { get { return selected; } }
        public DailyLife Life { get { return life; } }

        void Start()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            PlayerProfile player;
            savedPlayer = CharacterStore.TryLoad(out player);
            if (!savedPlayer) player = new PlayerProfile();
            life = new DailyLife(player, Squad.Provisional(player.displayName), energyAfterMatch, daysToMatch, Mathf.Max(1, day));
            BuildUi();
            Close();
            statusText.text = "경기 끝 체력 " + Mathf.RoundToInt(energyAfterMatch) + "% → 아침 " + Mathf.RoundToInt(life.Energy) + "%, 행동력 " + life.clock.points + "P. 장소를 눌러 할 일을 목록에 넣는다.";
            Refresh();
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
                Button b = Btn(actionList, "", 19, () => AddAction(selected.id, act.name));
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

        /// <summary>Puts an action on this part's list - nothing is spent until the list is confirmed.</summary>
        public void AddAction(string placeId, string actionName)
        {
            if (life.Add(placeId, actionName)) statusText.text = "목록에 넣었다: " + PlaceCatalog.Get(placeId).name + " · " + actionName;
            Refresh();
        }

        public void RemoveItem(DayPart part, int index)
        {
            life.Remove(part, index);
            Refresh();
        }

        public void ConfirmList()
        {
            statusText.text = life.Confirm();
            Refresh();
        }

        public void SkipPart()
        {
            statusText.text = life.SkipPart();
            Refresh();
        }

        public void SaveRoutine()
        {
            life.SaveRoutine();
            statusText.text = "오늘 목록을 루틴으로 저장했다.";
            Refresh();
        }

        public void FillRoutine()
        {
            statusText.text = life.FillFromRoutine() ? "루틴대로 채웠다. 포인트가 모자란 것은 뒤에서부터 뺐다." : "저장된 루틴이 없다.";
            Refresh();
        }

        // ------------------------------------------------------------ refresh --

        void Refresh()
        {
            DayClock c = life.clock;
            dateText.text = "시즌 1 · " + c.day + "일째 (" + Weekdays[(c.day - 1) % 7] + "요일)";
            matchText.text = life.IsMatchDay ? "경기 날" : "경기까지 " + life.DaysToMatch + "일";
            clockText.text = c.Time + "  " + DayClock.PartName(c.part);
            energyText.text = "체력 " + Mathf.RoundToInt(life.Energy) + "%";
            pointsText.text = "행동력 " + c.points + "P";

            foreach (KeyValuePair<Button, PlaceAction> ab in actionButtons)
            {
                PlaceAction a = ab.Value;
                int cost = a.Cost(life.FreePoints);
                string time = a.TakesRestOfPart ? "남은 시간 전부" : Duration(a.minutes);
                string energy = a.morningBonus > 0f ? "내일 아침 +" + a.morningBonus + "%" : Signed(a.energy) + "%";
                ab.Key.GetComponentInChildren<Text>().text = a.name + "  <size=15><color=#b8c0cc>" + time + " · " + cost + "P · 체력 " + energy + "</color></size>"
                    + (a.chemistry ? "  <size=14><color=#9fd7a8>친밀도</color></size>" : "");
                ab.Key.interactable = life.CanAdd(a);
            }
            RebuildPlan();
        }

        /// <summary>Today's four lists: done ones dim, this part's with its forecast, later ones with the points their energy will give.</summary>
        void RebuildPlan()
        {
            ClearChildren(partsBox);
            PartForecast[] fc = life.ForecastDay();
            int now = (int)life.clock.part;
            bool pending = false;
            for (int p = 0; p < 4; p++)
            {
                DayPart part = (DayPart)p;
                List<PlanItem> items = life.today[p].items;
                string head = DayClock.PartName(part) + " " + DayClock.StartHour(part).ToString("00") + "~" + (DayClock.StartHour(part) + 6).ToString("00");
                Color hc = Dim;
                if (life.IsMatchDay) head += "  ·  경기 날";
                else if (p < now) head += "  ·  끝";
                else if (p == now) { head = "▶ " + head + "  ·  " + fc[p].points + "P"; hc = Gold; }
                else head += "  ·  예상 " + fc[p].points + "P";
                Text h = Label(partsBox, head, 18, hc, TextAnchor.MiddleLeft);
                h.gameObject.AddComponent<LayoutElement>().minHeight = 28f;

                for (int i = 0; i < items.Count; i++)
                {
                    PlanItem it = items[i];
                    PlaceAction a;
                    PlaceCatalog.TryAction(it.place, it.action, out a);
                    string line;
                    Color col = Color.white;
                    if (it.done) { line = "✓ " + it.action; col = Dim; }
                    else
                    {
                        bool fits = p >= now && fc[p].fits != null && fc[p].fits[i];
                        line = it.action + "  " + (fits ? fc[p].costs[i] + "P · 체력 " + Signed(a.energy) + "%" : "포인트 부족");
                        if (!fits) col = Bad;
                        if (p == now) pending = true;
                    }
                    ItemRow(line, col, it.done ? null : (UnityEngine.Events.UnityAction)(() => RemoveItem(part, IndexOf(part, it))));
                }
                if (p == now && !life.IsMatchDay)
                {
                    int end = DayClock.StartHour(part) * 60 + fc[p].endMinutes;
                    Text f = Label(partsBox, "끝나면 " + (end / 60 % 24).ToString("00") + ":" + (end % 60).ToString("00") + " · 체력 "
                                   + Mathf.RoundToInt(fc[p].energyAfter) + "% · 남는 " + fc[p].pointsLeft + "P", 16, Gold, TextAnchor.MiddleLeft);
                    f.name = "Forecast";
                    f.gameObject.AddComponent<LayoutElement>().minHeight = 24f;
                }
            }
            confirmButton.interactable = pending;
            routineFillButton.interactable = life.routine != null && !life.IsMatchDay;
        }

        int IndexOf(DayPart part, PlanItem it) { return life.today[(int)part].items.IndexOf(it); }

        void ItemRow(string text, Color col, UnityEngine.Events.UnityAction remove)
        {
            var row = new GameObject("Item", typeof(RectTransform)).GetComponent<RectTransform>();
            row.SetParent(partsBox, false);
            row.gameObject.AddComponent<LayoutElement>().minHeight = 32f;
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6f; h.padding = new RectOffset(16, 0, 0, 0);
            // no forced height: a forced row reports itself flexible and soaks up the panel's spare space
            h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            Text t = Label(row, text, 17, col, TextAnchor.MiddleLeft);
            t.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            if (remove == null) return;
            Button x = Btn(row, "×", 18, remove);
            x.name = "Remove";
            LayoutElement xl = x.gameObject.AddComponent<LayoutElement>();
            xl.minWidth = 36f;
            xl.minHeight = 28f;
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
            Button skip = Btn(top, "다음 시간대로", 20, SkipPart);
            skip.name = "NextPart";
            Anchor(skip, new Vector2(0.67f, 0.18f), new Vector2(0.78f, 0.82f));
            Label(top, PlayerLine(), 18, Dim, TextAnchor.MiddleRight, new Vector2(0.78f, 0f), Vector2.one, Vector2.zero, new Vector2(-24f, 0f));

            statusText = Label(root, "", 18, Dim, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(1f, 0f), new Vector2(20f, 8f), new Vector2(-20f, 48f));

            // the map, three by two blocks between the roads, one district each
            RectTransform map = Box(root, "Map", Vector2.zero, Vector2.one, new Vector2(20f, 56f), new Vector2(-540f, -110f), new Color(0.07f, 0.08f, 0.08f));
            Zone(map, "운동 지구", new Vector2(0.01f, 0.48f), new Vector2(0.315f, 0.98f), new Color(0.10f, 0.20f, 0.11f));
            Zone(map, "도심", new Vector2(0.345f, 0.48f), new Vector2(0.645f, 0.98f), new Color(0.15f, 0.15f, 0.17f));
            Zone(map, "상업 지구", new Vector2(0.675f, 0.48f), new Vector2(0.99f, 0.98f), new Color(0.12f, 0.13f, 0.20f));
            Zone(map, "공원", new Vector2(0.01f, 0.02f), new Vector2(0.315f, 0.42f), new Color(0.09f, 0.17f, 0.10f));
            Zone(map, "광장 거리", new Vector2(0.345f, 0.02f), new Vector2(0.645f, 0.42f), new Color(0.20f, 0.20f, 0.21f));
            Zone(map, "주거 지구", new Vector2(0.675f, 0.02f), new Vector2(0.99f, 0.42f), new Color(0.19f, 0.16f, 0.11f));
            foreach (Place p in PlaceCatalog.All) Marker(map, p);

            // today's lists
            RectTransform plan = Box(root, "Plan", new Vector2(1f, 0f), Vector2.one, new Vector2(-520f, 56f), new Vector2(-20f, -110f), Bar);
            var pv = plan.gameObject.AddComponent<VerticalLayoutGroup>();
            pv.padding = new RectOffset(20, 20, 16, 16);
            pv.spacing = 6f;
            pv.childControlWidth = true; pv.childControlHeight = true; pv.childForceExpandHeight = false;
            Label(plan, "오늘 일정", 26, Color.white, TextAnchor.MiddleLeft).gameObject.AddComponent<LayoutElement>().minHeight = 36f;
            partsBox = new GameObject("Parts", typeof(RectTransform)).GetComponent<RectTransform>();
            partsBox.SetParent(plan, false);
            var bv = partsBox.gameObject.AddComponent<VerticalLayoutGroup>();
            bv.spacing = 4f;
            bv.childControlWidth = true; bv.childControlHeight = true; bv.childForceExpandHeight = false;
            var gap = new GameObject("Spacer", typeof(RectTransform)).AddComponent<LayoutElement>();
            gap.transform.SetParent(plan, false);
            gap.flexibleHeight = 1f;
            confirmButton = Btn(plan, "확정 — 목록대로 하기", 22, ConfirmList);
            confirmButton.name = "Confirm";
            confirmButton.gameObject.AddComponent<LayoutElement>().minHeight = 52f;
            var routineRow = new GameObject("RoutineRow", typeof(RectTransform)).GetComponent<RectTransform>();
            routineRow.SetParent(plan, false);
            routineRow.gameObject.AddComponent<LayoutElement>().minHeight = 44f;
            var rh = routineRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            rh.spacing = 8f;
            rh.childControlWidth = true; rh.childControlHeight = true; rh.childForceExpandWidth = true; rh.childForceExpandHeight = false;
            Button save = Btn(routineRow, "루틴 저장", 19, SaveRoutine);
            save.name = "SaveRoutine";
            save.gameObject.AddComponent<LayoutElement>().minHeight = 44f;
            routineFillButton = Btn(routineRow, "루틴대로", 19, FillRoutine);
            routineFillButton.name = "FillRoutine";
            routineFillButton.gameObject.AddComponent<LayoutElement>().minHeight = 44f;

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
            Label(panel, "할 수 있는 일 — 누르면 목록에 들어간다", 18, Gold, TextAnchor.MiddleLeft).gameObject.AddComponent<LayoutElement>().minHeight = 30f;
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
