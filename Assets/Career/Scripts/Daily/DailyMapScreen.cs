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
    /// (<see cref="PlaceCatalog"/>). A place opens a small panel with what can be done there.
    /// Every action takes time and costs action points (<see cref="DayClock"/>, PROJECT.md 10.11);
    /// what it does is not built, nor money (PROJECT.md 10.10).
    ///
    /// uGUI built in code when the scene starts, like the creation screen; the scene holds the
    /// camera, the event system and this component. Placeholder art: coloured blocks for the
    /// districts and a lettered square for each place.
    /// </summary>
    public class DailyMapScreen : MonoBehaviour
    {
        [Tooltip("Day the clock starts on - a placeholder until there is a calendar and a save.")]
        public int day = 1;

        readonly DayClock clock = new DayClock();
        PlayerProfile player;
        bool savedPlayer;
        Squad squad;

        Font font;
        RectTransform panel;
        Text panelName, panelBlurb, statusText, dateText, clockText, pointsText;
        RectTransform actionList;
        readonly Dictionary<string, Image> frames = new Dictionary<string, Image>();
        readonly List<KeyValuePair<Button, PlaceAction>> actionButtons = new List<KeyValuePair<Button, PlaceAction>>();
        Place selected;

        static readonly Color Bar = new Color(0.09f, 0.11f, 0.14f, 0.96f);
        static readonly Color Row = new Color(0.16f, 0.19f, 0.23f, 1f);
        static readonly Color Dim = new Color(0.62f, 0.65f, 0.70f, 1f);
        static readonly Color Gold = new Color(0.95f, 0.82f, 0.45f, 1f);
        static readonly string[] Weekdays = { "월", "화", "수", "목", "금", "토", "일" };

        public Place Selected { get { return selected; } }
        public DayClock Clock { get { return clock; } }

        void Start()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            savedPlayer = CharacterStore.TryLoad(out player);
            if (!savedPlayer) player = new PlayerProfile();
            squad = Squad.Provisional(player.displayName);
            clock.day = Mathf.Max(1, day);
            clock.Begin(DayClock.PointsFor(player));
            BuildUi();
            Close();
            RefreshClock();
        }

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
            panel.offsetMin = right ? new Vector2(-560f, 80f) : new Vector2(44f, 80f);
            panel.offsetMax = right ? new Vector2(-44f, -134f) : new Vector2(560f, -134f);
            panelName.text = p.name;
            panelBlurb.text = p.blurb;
            for (int i = actionList.childCount - 1; i >= 0; i--) Destroy(actionList.GetChild(i).gameObject);
            actionButtons.Clear();
            foreach (PlaceAction a in p.actions)
            {
                PlaceAction act = a;
                Button b = Btn(actionList, "", 20, () => Do(act));
                b.name = "Action_" + act.name;
                b.gameObject.AddComponent<LayoutElement>().minHeight = 48f;
                actionButtons.Add(new KeyValuePair<Button, PlaceAction>(b, act));
            }
            foreach (KeyValuePair<string, Image> f in frames) f.Value.enabled = f.Key == p.id;
            statusText.text = "";
            RefreshActions();
        }

        public void Close()
        {
            selected = null;
            panel.gameObject.SetActive(false);
            actionButtons.Clear();
            foreach (Image f in frames.Values) f.enabled = false;
            statusText.text = "장소를 누르면 할 수 있는 일이 나온다.";
        }

        /// <summary>Does an action: its points go, the clock moves, and a part with none left gives way to the next.</summary>
        void Do(PlaceAction a)
        {
            int cost = a.Cost(clock);
            if (!clock.CanAfford(cost)) return;
            string at = clock.Time;
            clock.Spend(cost);
            string msg = at + " " + selected.name + " · " + a.name + " (" + cost + "P) — 효과는 아직 없다" + (a.chemistry ? " (친밀도 후보, §10.9)" : "") + ".";
            if (clock.points == 0) msg += "  행동력을 다 써서 " + Advance();
            statusText.text = msg;
            RefreshClock();
        }

        /// <summary>The button on the top bar: whatever points are left go, and the next part of the day starts.</summary>
        public void SkipPart()
        {
            int left = clock.points;
            statusText.text = "남은 행동력 " + left + "P를 버리고 " + Advance();
            RefreshClock();
        }

        /// <summary>On to the next part; at the end of the night a new day, and the squad's day ends with it.</summary>
        string Advance()
        {
            bool newDay = clock.NextPart(DayClock.PointsFor(player));
            if (newDay) squad.EndDay();
            return (newDay ? clock.day + "일째 " : "") + DayClock.PartName(clock.part) + " " + clock.Time + " 시작, 행동력 " + clock.points + "P.";
        }

        void RefreshClock()
        {
            dateText.text = "시즌 1 · " + clock.day + "일째 (" + Weekdays[(clock.day - 1) % 7] + "요일)";
            clockText.text = clock.Time + "  " + DayClock.PartName(clock.part);
            pointsText.text = "행동력 " + clock.points + " / " + clock.partPoints;
            RefreshActions();
        }

        /// <summary>Each action with its time and cost; the ones the points left cannot pay for go grey.</summary>
        void RefreshActions()
        {
            foreach (KeyValuePair<Button, PlaceAction> ab in actionButtons)
            {
                PlaceAction a = ab.Value;
                int cost = a.Cost(clock);
                string time = a.TakesRestOfPart ? "남은 시간 전부" : Duration(a.minutes);
                ab.Key.GetComponentInChildren<Text>().text = a.name + "  <size=15><color=#b8c0cc>" + time + " · " + cost + "P</color></size>"
                    + (a.chemistry ? "  <size=14><color=#9fd7a8>친밀도</color></size>" : "");
                ab.Key.interactable = clock.CanAfford(cost);
            }
        }

        static string Duration(int minutes)
        {
            int h = minutes / 60, m = minutes % 60;
            if (h == 0) return m + "분";
            return m == 0 ? h + "시간" : h + "시간 " + m + "분";
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

            // top bar: where, when, who
            RectTransform top = Box(root, "TopBar", new Vector2(0f, 1f), Vector2.one, new Vector2(20f, -96f), new Vector2(-20f, -16f), Bar);
            Label(top, "일상", 30, Color.white, TextAnchor.MiddleLeft, Vector2.zero, new Vector2(0.08f, 1f), new Vector2(24f, 0f), Vector2.zero);
            dateText = Label(top, "", 24, Color.white, TextAnchor.MiddleCenter, new Vector2(0.08f, 0f), new Vector2(0.30f, 1f), Vector2.zero, Vector2.zero);
            clockText = Label(top, "", 26, Gold, TextAnchor.MiddleCenter, new Vector2(0.30f, 0f), new Vector2(0.44f, 1f), Vector2.zero, Vector2.zero);
            pointsText = Label(top, "", 24, Color.white, TextAnchor.MiddleCenter, new Vector2(0.44f, 0f), new Vector2(0.58f, 1f), Vector2.zero, Vector2.zero);
            Button skip = Btn(top, "다음 시간대로", 20, SkipPart);
            skip.name = "NextPart";
            RectTransform srt = skip.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0.59f, 0.18f); srt.anchorMax = new Vector2(0.71f, 0.82f);
            srt.offsetMin = srt.offsetMax = Vector2.zero;
            Label(top, PlayerLine(), 20, Dim, TextAnchor.MiddleRight, new Vector2(0.72f, 0f), Vector2.one, Vector2.zero, new Vector2(-24f, 0f));

            statusText = Label(root, "", 18, Dim, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(1f, 0f), new Vector2(20f, 8f), new Vector2(-20f, 48f));

            // the map
            // three by two blocks between the roads, one district each
            RectTransform map = Box(root, "Map", Vector2.zero, Vector2.one, new Vector2(20f, 56f), new Vector2(-20f, -110f), new Color(0.07f, 0.08f, 0.08f));
            Zone(map, "운동 지구", new Vector2(0.01f, 0.48f), new Vector2(0.315f, 0.98f), new Color(0.10f, 0.20f, 0.11f));
            Zone(map, "도심", new Vector2(0.345f, 0.48f), new Vector2(0.645f, 0.98f), new Color(0.15f, 0.15f, 0.17f));
            Zone(map, "상업 지구", new Vector2(0.675f, 0.48f), new Vector2(0.99f, 0.98f), new Color(0.12f, 0.13f, 0.20f));
            Zone(map, "공원", new Vector2(0.01f, 0.02f), new Vector2(0.315f, 0.42f), new Color(0.09f, 0.17f, 0.10f));
            Zone(map, "광장 거리", new Vector2(0.345f, 0.02f), new Vector2(0.645f, 0.42f), new Color(0.20f, 0.20f, 0.21f));
            Zone(map, "주거 지구", new Vector2(0.675f, 0.02f), new Vector2(0.99f, 0.42f), new Color(0.19f, 0.16f, 0.11f));
            foreach (Place p in PlaceCatalog.All) Marker(map, p);

            // the panel a place opens, opaque so the map does not show through
            panel = Box(root, "PlacePanel", new Vector2(1f, 0f), Vector2.one, new Vector2(-560f, 80f), new Vector2(-44f, -134f), new Color(Bar.r, Bar.g, Bar.b, 1f));
            var v = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(24, 24, 20, 20);
            v.spacing = 10f;
            v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandHeight = false;
            panelName = Label(panel, "", 30, Color.white, TextAnchor.MiddleLeft);
            panelName.gameObject.AddComponent<LayoutElement>().minHeight = 40f;
            panelBlurb = Label(panel, "", 18, Dim, TextAnchor.UpperLeft);
            panelBlurb.gameObject.AddComponent<LayoutElement>().minHeight = 30f;
            Label(panel, "할 수 있는 일", 18, Gold, TextAnchor.MiddleLeft).gameObject.AddComponent<LayoutElement>().minHeight = 30f;
            actionList = new GameObject("Actions", typeof(RectTransform)).GetComponent<RectTransform>();
            actionList.SetParent(panel, false);
            var av = actionList.gameObject.AddComponent<VerticalLayoutGroup>();
            av.spacing = 8f;
            av.childControlWidth = true; av.childControlHeight = true; av.childForceExpandHeight = false;
            Note(panel, "30분에 1P. 효과와 돈은 아직 없다.");
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
            return player.displayName + " · " + TalentCatalog.Get(player.talent).name + " · " + player.age + "세";
        }

        void Zone(RectTransform map, string label, Vector2 aMin, Vector2 aMax, Color c)
        {
            RectTransform z = Box(map, "Zone_" + label, aMin, aMax, Vector2.zero, Vector2.zero, c);
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

        RectTransform Box(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = c;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
            return rt;
        }

        void Note(RectTransform parent, string s)
        {
            Label(parent, s, 14, Dim, TextAnchor.UpperLeft).gameObject.AddComponent<LayoutElement>().minHeight = 22f;
        }

        Text Label(Transform parent, string s, int size, Color c, TextAnchor a)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font; t.fontSize = size; t.color = c; t.alignment = a; t.text = s;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        Text Label(Transform parent, string s, int size, Color c, TextAnchor a, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            Text t = Label(parent, s, size, c, a);
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
