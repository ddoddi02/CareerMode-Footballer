using System.Collections.Generic;
using Prototype;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Career
{
    /// <summary>
    /// Character creation (user, 2026-10-16): stats, perks and looks on one screen, balanced
    /// with points the way Project Zomboid does it - good perks cost, bad perks pay back, and
    /// the character cannot be confirmed while the points are below zero. Before that, a step
    /// of its own: talent, the difficulty (user, 2026-10-17). The talent button on the top bar
    /// goes back to it.
    ///
    /// uGUI, built in code when the scene starts (the builder only lays out the camera, the
    /// event system and this component). uGUI rather than UI Toolkit because the built-in
    /// dynamic font falls back to the system's Korean glyphs with no font asset to make, and
    /// buttons, a slider and a text field are all it needs.
    ///
    /// Every change rebuilds the profile from the draft and redraws: points, the stat values
    /// with their perk changes, shot power and its speed, the body multipliers and the body
    /// traits - so a perk or a build shows what it does the moment it is picked. Confirm saves
    /// the profile (CharacterStore) and the shot drill starts with it.
    /// </summary>
    public class CharacterCreationScreen : MonoBehaviour
    {
        public AvatarPreview avatar;

        readonly CharacterDraft draft = new CharacterDraft();
        Font font;

        // the two steps
        GameObject talentPage, mainPage;
        readonly Dictionary<TalentTier, Button> talentCards = new Dictionary<TalentTier, Button>();

        // what Refresh rewrites
        Text pointsText, statusText, derivedText, confirmLabel, heightLabel, peakLabel, weightLabel, muscleLabel, talentLabel;
        Slider weightSlider, muscleSlider, peakSlider;
        Button confirmButton;
        readonly List<Text> statNames = new List<Text>(), statValues = new List<Text>(), statDiffs = new List<Text>();
        readonly List<Button> statUps = new List<Button>();
        readonly Dictionary<string, Button> perkButtons = new Dictionary<string, Button>();
        readonly List<Button> buildButtons = new List<Button>(), styleButtons = new List<Button>();
        readonly List<Image> skinFrames = new List<Image>(), hairFrames = new List<Image>(), kitFrames = new List<Image>();

        static readonly Color Panel = new Color(0.09f, 0.11f, 0.14f, 0.96f);
        static readonly Color Row = new Color(0.16f, 0.19f, 0.23f, 1f);
        static readonly Color Picked = new Color(0.20f, 0.45f, 0.30f, 1f);
        static readonly Color PickedBad = new Color(0.50f, 0.24f, 0.20f, 1f);
        static readonly Color Dim = new Color(0.55f, 0.58f, 0.62f, 1f);
        static readonly Color Good = new Color(0.45f, 0.85f, 0.55f, 1f);
        static readonly Color Bad = new Color(0.95f, 0.45f, 0.40f, 1f);

        public CharacterDraft Draft { get { return draft; } }

        void Start()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            BuildUi();
            ShowTalentStep(true);
            Refresh();
        }

        /// <summary>Talent first; the stats, perks and looks after it.</summary>
        public void ShowTalentStep(bool talent)
        {
            talentPage.SetActive(talent);
            mainPage.SetActive(!talent);
        }

        public bool OnTalentStep { get { return talentPage.activeSelf; } }

        // ------------------------------------------------------------ layout --

        void BuildUi()
        {
            var canvasGo = new GameObject("CreationCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            RectTransform canvasRoot = canvasGo.GetComponent<RectTransform>();

            RectTransform talent = Page(canvasRoot, "TalentPage");
            talentPage = talent.gameObject;
            BuildTalentStep(talent);

            RectTransform root = Page(canvasRoot, "MainPage");
            mainPage = root.gameObject;

            // top bar
            RectTransform top = Box(root, "TopBar", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -96f), new Vector2(-20f, -16f), Panel);
            Label(top, "캐릭터 생성", 30, Color.white, TextAnchor.MiddleLeft, new Vector2(0f, 0f), new Vector2(0.13f, 1f), new Vector2(24f, 0f), Vector2.zero);
            Button back = Btn(top, "", 16, () => ShowTalentStep(true), new Vector2(0.13f, 0.18f), new Vector2(0.29f, 0.82f));
            back.name = "TalentBack";
            talentLabel = back.GetComponentInChildren<Text>();
            pointsText = Label(top, "", 26, Color.white, TextAnchor.MiddleCenter, new Vector2(0.29f, 0f), new Vector2(0.75f, 1f), Vector2.zero, Vector2.zero);
            Btn(top, "처음부터", 20, ResetDraft, new Vector2(0.76f, 0.18f), new Vector2(0.85f, 0.82f));   // pool toggle gone: stats no longer cost points
            confirmButton = Btn(top, "확정", 24, Confirm, new Vector2(0.86f, 0.12f), new Vector2(0.995f, 0.88f));
            confirmLabel = confirmButton.GetComponentInChildren<Text>();

            statusText = Label(root, "", 18, Dim, TextAnchor.MiddleCenter, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(20f, 8f), new Vector2(-20f, 40f));

            // three columns
            RectTransform left = Column(root, "Stats", 0f, 0.31f);
            RectTransform mid = Column(root, "Perks", 0.31f, 0.65f);
            RectTransform right = Column(root, "Looks", 0.65f, 1f);
            BuildStats(left);
            BuildPerks(mid);
            BuildLooks(right);
        }

        RectTransform Page(RectTransform parent, string name)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Stretch(rt, Vector2.zero, Vector2.zero);
            return rt;
        }

        /// <summary>The first step: four talents side by side, and on to the rest.</summary>
        void BuildTalentStep(RectTransform page)
        {
            RectTransform head = Box(page, "TalentHead", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -190f), new Vector2(-20f, -16f), Panel);
            Label(head, "재능 선택 — 난이도", 34, Color.white, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(28f, 0f), new Vector2(-28f, -18f));
            Label(head, "재능은 시작 오버롤(나눠 줄 스탯 합)과 키의 장단점, 그리고 경력 내내 얼마나 빨리 늘고 버티는지를 정한다. 특전 포인트는 모두 10이다.\n"
                      + "<color=#e8c070>시작 오버롤·키 규칙은 바로 적용된다. 성장·노화·특성 획득은 그 시스템이 생기면 적용된다.</color>",
                  19, new Color(0.85f, 0.87f, 0.90f), TextAnchor.LowerLeft, Vector2.zero, Vector2.one, new Vector2(28f, 18f), new Vector2(-28f, 0f));

            TalentDef[] all = TalentCatalog.All;
            for (int i = 0; i < all.Length; i++)
            {
                TalentDef t = all[i];
                float x0 = (float)i / all.Length, x1 = (float)(i + 1) / all.Length;
                Button card = Btn(page, "", 18, () => { draft.SetTalent(t.tier); Refresh(); }, new Vector2(x0, 0.15f), new Vector2(x1, 0.80f));
                card.name = "Talent_" + t.tier;
                var rt = card.GetComponent<RectTransform>();
                rt.offsetMin = new Vector2(20f, 0f); rt.offsetMax = new Vector2(i == all.Length - 1 ? -20f : 0f, 0f);
                Text body = card.GetComponentInChildren<Text>();
                body.alignment = TextAnchor.UpperLeft;
                body.fontSize = 22;
                body.lineSpacing = 1.1f;
                Stretch(body.rectTransform, new Vector2(22f, 18f), new Vector2(-22f, -20f));
                body.text = TalentCard(t);
                talentCards[t.tier] = card;
            }

            Button next = Btn(page, "다음 — 스탯 · 특전 · 외형", 24, () => { ShowTalentStep(false); Refresh(); },
                              new Vector2(0.38f, 0.03f), new Vector2(0.62f, 0.11f));
            next.name = "TalentNext";
            next.GetComponent<Image>().color = Picked;
        }

        static string TalentCard(TalentDef t)
        {
            TalentDef normal = TalentCatalog.Get(TalentTier.TopFlight);
            var sb = new System.Text.StringBuilder();
            sb.AppendFormat("<size=34><b>{0}</b></size>\n<size=22><color=#e8c070>{1}</color></size>\n\n", t.name, t.difficulty);
            sb.AppendFormat("<size=24><b>시작 오버롤 {0}</b></size> <size=16><color=#9aa3ad>(스탯 합 {1}을 나눠 준다)</color></size>\n", t.startOverall, CreationRules.StatBudget(t.tier));
            sb.AppendFormat("<size=17><color=#{0}>{1}</color></size>\n", Hex(t.heightDownsides ? Dim : Good), TalentCatalog.HeightLine(t));
            sb.Append("\n");
            sb.Append(t.blurb).Append("\n\n");
            sb.Append("<size=20>");
            sb.AppendFormat("성장 경험치 {0}\n", Mul(t.growthXp));
            sb.AppendFormat("근육 성장 {0}\n", Mul(t.muscleGrowth));
            sb.AppendFormat("15세 가속 손해 <color=#{0}>{1:0%}</color> · 키 성장 하한 <color=#{2}>{3:0%}</color>\n",
                Hex(t.youthAccelPenalty < 0.7f ? Good : (t.youthAccelPenalty > 0.7f ? Bad : Color.white)), t.youthAccelPenalty,
                Hex(t.minHeightGrowth > 0.5f ? Good : Color.white), t.minHeightGrowth);
            sb.AppendFormat("좋은 특성 획득 확률 {0}\n", Mul(t.goodTraitChance));
            sb.AppendFormat("나이에 따른 하락 {0}\n", Mul(t.ageDecline, false));
            sb.AppendFormat("훈련 안 한 날의 하락 {0}\n", t.idleDecline <= 0f ? "<color=#" + Hex(Good) + ">없음</color>" : Mul(t.idleDecline, false));
            string share = string.Format("훈련 하루의 {0:0%}", t.matchXpShare);
            sb.AppendFormat("경기 한 번의 경험치: {0}",
                Mathf.Approximately(t.matchXpShare, normal.matchXpShare) ? share
                : "<color=#" + Hex(t.matchXpShare > normal.matchXpShare ? Good : Bad) + ">" + share + "</color>");
            sb.Append("</size>");
            return sb.ToString();
        }

        RectTransform Column(RectTransform root, string name, float x0, float x1)
        {
            RectTransform c = Box(root, name, new Vector2(x0, 0f), new Vector2(x1, 1f), new Vector2(20f, 48f), new Vector2(-4f, -108f), Panel);
            var v = c.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(18, 18, 14, 14);
            v.spacing = 6f;
            v.childControlHeight = true;
            v.childControlWidth = true;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = true;
            return c;
        }

        void BuildStats(RectTransform col)
        {
            Header(col, "스탯 — 1~100, 5씩. 재능이 준 합계를 나눠 준다");
            Note(col, "모든 스탯은 재능의 시작 오버롤에서 출발한다. 하나를 올리려면 다른 것을 내려야 한다. 평균이 오버롤이다. 오른쪽 숫자는 출발점과의 차이");
            for (int i = 0; i < CreationRules.Stats.Length; i++)
            {
                int k = i;
                CreationRules.StatDef s = CreationRules.Stats[i];
                RectTransform row = RowBox(col, 52f);
                var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                h.spacing = 6f; h.padding = new RectOffset(10, 8, 4, 4);
                h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false;
                Text name = Label(row, "", 19, Color.white, TextAnchor.MiddleLeft);
                statNames.Add(name);
                var nameLe = name.gameObject.AddComponent<LayoutElement>();
                nameLe.minWidth = 140f; nameLe.preferredWidth = 215f; nameLe.flexibleWidth = 1f;   // gives way first on a narrow screen
                Fixed(Btn(row, "−", 26, () => { draft.StepStat(k, -1); Refresh(); }), 46f).name = "Stat_" + s.id + "_Down";
                statValues.Add(Fixed(Label(row, "", 22, Color.white, TextAnchor.MiddleCenter), 96f));
                Button up = Fixed(Btn(row, "+", 26, () => { if (!draft.StepStat(k, +1)) Flash("올릴 스탯 포인트가 없다 — 다른 스탯을 먼저 내려라"); Refresh(); }), 46f);
                up.name = "Stat_" + s.id + "_Up";
                statUps.Add(up);
                statDiffs.Add(Fixed(Label(row, "", 18, Dim, TextAnchor.MiddleRight), 60f));
            }
            Header(col, "몸과 특전에서 나오는 값 (고르는 즉시 바뀜)");
            derivedText = Label(col, "", 18, new Color(0.88f, 0.90f, 0.92f), TextAnchor.UpperLeft);
            Flexible(derivedText, 420f);
        }

        void BuildPerks(RectTransform col)
        {
            Header(col, "특전 — 좋은 특전은 포인트를 쓰고, 나쁜 특전은 돌려준다");
            Note(col, "[연결] 드릴에서 바로 보인다 · [배율만] 배율은 바뀌지만 경기에 아직 안 이어짐 · [미정] 그 시스템이 아직 없다");
            Header(col, "좋은 특전");
            foreach (Perk p in PerkCatalog.All) if (p.Good) PerkRow(col, p);
            Header(col, "나쁜 특전");
            foreach (Perk p in PerkCatalog.All) if (!p.Good) PerkRow(col, p);
        }

        void PerkRow(RectTransform col, Perk p)
        {
            string id = p.id;
            Button b = Btn(col, string.Format("<b>{0}</b>  <size=15><color=#c8ced6>{1}</color>  <color=#8a939d>{2}</color></size>",
                                              p.name, p.effect, Tag(p.link)), 19,
                           () => { if (!draft.TogglePerk(id)) Flash("같이 고를 수 없는 특전이 이미 있다"); Refresh(); });
            b.name = "Perk_" + id;
            Text t = b.GetComponentInChildren<Text>();
            t.alignment = TextAnchor.MiddleLeft;
            t.rectTransform.offsetMin = new Vector2(14f, 0f);
            Text cost = Label(b.transform, p.cost > 0 ? "−" + p.cost : "+" + (-p.cost), 20, p.cost > 0 ? Bad : Good, TextAnchor.MiddleRight,
                              Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-14f, 0f));
            cost.raycastTarget = false;
            Fixed(b, 0f, 40f);
            perkButtons[id] = b;
        }

        void BuildLooks(RectTransform col)
        {
            Header(col, "몸과 외형 — 15세. 포인트 없이 자유롭게, 근육만 상한이 있다");

            var img = new GameObject("AvatarView", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            img.transform.SetParent(col, false);
            if (avatar != null) img.texture = avatar.Texture;     // same shape as this box (AvatarPreview)
            Fixed(img, 0f, 260f);

            // name
            RectTransform nameRow = Line(col, "이름");
            InputField name = Field(nameRow, draft.displayName);
            name.onValueChanged.AddListener(v => { draft.displayName = v; });

            // height, weight, muscle - free, but muscle stops at the 15-year-old cap
            RectTransform hRow = Line(col, "지금 키");
            Slider hs = MakeSlider(hRow, CreationRules.HeightMin, CreationRules.HeightMax, draft.heightCm);
            hs.name = "HeightSlider";
            hs.wholeNumbers = true;
            hs.onValueChanged.AddListener(v => { draft.SetHeight(v); Refresh(); });
            heightLabel = Fixed(Label(hRow, "", 17, Color.white, TextAnchor.MiddleRight), 190f);

            RectTransform pRow = Line(col, "잠재 최고 키");
            peakSlider = MakeSlider(pRow, draft.heightCm, draft.heightCm + CreationRules.PeakAbove, draft.peakHeightCm);
            peakSlider.name = "PeakHeightSlider";
            peakSlider.wholeNumbers = true;
            peakSlider.onValueChanged.AddListener(v => { draft.SetPeakHeight(v); Refresh(); });
            peakLabel = Fixed(Label(pRow, "", 17, Color.white, TextAnchor.MiddleRight), 190f);

            RectTransform wRow = Line(col, "몸무게");
            weightSlider = MakeSlider(wRow, CreationRules.WeightMin, CreationRules.WeightMax, draft.weightKg);
            weightSlider.name = "WeightSlider";
            weightSlider.onValueChanged.AddListener(v => { draft.SetWeight(v); Refresh(); });
            weightLabel = Fixed(Label(wRow, "", 17, Color.white, TextAnchor.MiddleRight), 190f);

            RectTransform mRow = Line(col, "근육");
            muscleSlider = MakeSlider(mRow, CreationRules.MuscleMin, CreationRules.MuscleCap, draft.musclePercent);
            muscleSlider.name = "MuscleSlider";
            muscleSlider.onValueChanged.AddListener(v => { draft.SetMuscle(v); Refresh(); });
            muscleLabel = Fixed(Label(mRow, "", 17, Color.white, TextAnchor.MiddleRight), 190f);

            RectTransform bRow = Line(col, "빠른 선택");
            for (int i = 0; i < CreationRules.Presets.Length; i++)
            {
                int k = i;
                Button btn = Btn(bRow, CreationRules.Presets[i].name, 16, () => { draft.ApplyPreset(k); Refresh(); });
                btn.name = "Build_" + i;
                Fixed(btn, 0f, 40f).GetComponent<LayoutElement>().flexibleWidth = 1f;
                buildButtons.Add(btn);
            }

            Swatches(col, "피부", Appearance.Skins, skinFrames, i => draft.appearance.skin = i);
            Swatches(col, "머리색", Appearance.HairColors, hairFrames, i => draft.appearance.hairColor = i);

            RectTransform sRow = Line(col, "머리 모양");
            for (int i = 0; i < Appearance.HairStyles.Length; i++)
            {
                int k = i;
                Button btn = Btn(sRow, Appearance.HairStyles[i], 16, () => { draft.appearance.hairStyle = k; Refresh(); });
                Fixed(btn, 0f, 40f).GetComponent<LayoutElement>().flexibleWidth = 1f;
                styleButtons.Add(btn);
            }

            Swatches(col, "유니폼", Appearance.KitColors, kitFrames, i => draft.appearance.kitColor = i);
        }

        void Swatches(RectTransform col, string label, Color[] palette, List<Image> frames, System.Action<int> pick)
        {
            RectTransform row = Line(col, label);
            for (int i = 0; i < palette.Length; i++)
            {
                int k = i;
                var frame = new GameObject("Swatch", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Image>();
                frame.transform.SetParent(row, false);
                Fixed(frame, 44f, 40f);
                var inner = new GameObject("Colour", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                inner.transform.SetParent(frame.transform, false);
                inner.color = palette[i];
                inner.raycastTarget = false;
                Stretch(inner.rectTransform, new Vector2(4f, 4f), new Vector2(-4f, -4f));
                frame.GetComponent<Button>().onClick.AddListener(() => { pick(k); Refresh(); });
                frames.Add(frame);
            }
        }

        // ---------------------------------------------------------- actions --

        void ResetDraft()
        {
            draft.ResetStats();
            draft.perks.Clear();
            draft.SetHeight(CreationRules.DefaultHeight);
            draft.SetPeakHeight(CreationRules.DefaultPeakHeight);
            draft.ApplyPreset(Appearance.Default.build);
            Refresh();
        }

        void Confirm()
        {
            if (!draft.CanConfirm) { Flash("포인트가 0보다 적으면 확정할 수 없다"); return; }
            PlayerProfile p = draft.ToProfile();
            CharacterStore.Save(p);
            Flash("저장했다 — 슛 드릴이 이 선수로 시작한다 (" + CharacterStore.Path + ")");
        }

        void Flash(string s)
        {
            statusText.text = s;
        }

        // ---------------------------------------------------------- redraw --

        public void Refresh()
        {
            foreach (var kv in talentCards)
                kv.Value.GetComponent<Image>().color = kv.Key == draft.talent ? Picked : Row;
            TalentDef talent = TalentCatalog.Get(draft.talent);
            talentLabel.text = string.Format("재능: {0} ({1})\n<size=13>눌러서 바꾸기</size>", talent.name, talent.difficulty);

            int left = draft.Remaining, statLeft = draft.StatLeft;
            pointsText.text = string.Format("특전 포인트 <color=#{0}><b>{1}</b></color><size=16><color=#9aa3ad>/{2}</color></size>   스탯 남음 <color=#{3}><b>{4}</b></color>   오버롤 <b>{5:0.#}</b>",
                                            Hex(left >= 0 ? Good : Bad), left, draft.StartingPoints,
                                            Hex(statLeft == 0 ? Good : Bad), statLeft, draft.Overall);

            bool ok = draft.CanConfirm;
            confirmButton.interactable = ok;
            confirmLabel.text = ok ? "확정" : (left < 0 ? "포인트 부족" : "스탯 " + statLeft + " 남음");

            for (int i = 0; i < CreationRules.Stats.Length; i++)
            {
                CreationRules.StatDef sd = CreationRules.Stats[i];
                statNames[i].text = string.Format("{0}\n<size=14><color=#9aa3ad>{1} · 기준 {2} · {3}</color></size>", sd.name, sd.effect, draft.Ordinary, Tag(sd.link));
                int d = draft.PerkDelta(i);
                statValues[i].text = d == 0 ? draft.FinalStat(i).ToString()
                    : string.Format("{0} <size=15><color=#{1}>({2:+0;−0})</color></size>", draft.FinalStat(i), Hex(d > 0 ? Good : Bad), d);
                int c = draft.stats[i] - draft.Ordinary;
                statDiffs[i].text = c == 0 ? "0" : (c > 0 ? "+" + c : "−" + (-c));
                statDiffs[i].color = c > 0 ? Good : (c < 0 ? Bad : Dim);
                statUps[i].interactable = statLeft > 0 && draft.stats[i] < StatScale.Max;
            }

            foreach (var kv in perkButtons)
            {
                Perk p = PerkCatalog.Get(kv.Key);
                bool on = draft.perks.Contains(kv.Key);
                bool blocked = !on && PerkCatalog.Blocked(kv.Key, draft.perks);
                kv.Value.interactable = !blocked;
                kv.Value.GetComponent<Image>().color = on ? (p.Good ? Picked : PickedBad) : Row;
                foreach (Text t in kv.Value.GetComponentsInChildren<Text>()) t.canvasRenderer.SetAlpha(blocked ? 0.35f : 1f);
            }

            for (int i = 0; i < buildButtons.Count; i++)
                buildButtons[i].GetComponent<Image>().color = i == draft.appearance.build ? Picked : Row;
            for (int i = 0; i < styleButtons.Count; i++)
                styleButtons[i].GetComponent<Image>().color = i == draft.appearance.hairStyle ? Picked : Row;
            Frames(skinFrames, draft.appearance.skin);
            Frames(hairFrames, draft.appearance.hairColor);
            Frames(kitFrames, draft.appearance.kitColor);
            PlayerProfile body = draft.ToProfile();
            BodyModifiers bm = BodyModel.Evaluate(body);
            float hAerial, hAgility;
            BodyModel.HeightEffects(draft.heightCm, draft.talent, out hAerial, out hAgility);
            heightLabel.text = string.Format("{0:0} cm <size=14>공중 {1} 민첩 {2}</size>", draft.heightCm, Mul(hAerial), Mul(hAgility));
            peakLabel.text = string.Format("{0:0} cm <size=14>최대 +{1:0} · 생활 습관대로</size>", draft.peakHeightCm, draft.peakHeightCm - draft.heightCm);
            peakSlider.minValue = draft.heightCm;                                  // now .. now + 20
            peakSlider.maxValue = draft.heightCm + CreationRules.PeakAbove;
            peakSlider.SetValueWithoutNotify(draft.peakHeightCm);
            weightLabel.text = string.Format("{0:0.0} kg <size=14>체지방 {1:0.0}%</size>", draft.weightKg, body.BodyFatPercent);
            muscleLabel.text = string.Format("{0:0.0}% <size=14>{1:0.0} kg · 상한 {2:0}%</size>", draft.musclePercent, body.muscleKg, CreationRules.MuscleCap);
            weightSlider.minValue = CreationRules.WeightMin;
            weightSlider.SetValueWithoutNotify(draft.weightKg);
            muscleSlider.SetValueWithoutNotify(draft.musclePercent);

            derivedText.text = Derived(draft.ToProfile());
            if (avatar != null) avatar.Show(draft);
        }

        static string Derived(PlayerProfile p)
        {
            ShotPower.Parts sp = ShotPower.Explain(p);
            BodyModifiers m = BodyModel.Evaluate(p);
            float v = ShotPower.ProvisionalSpeed(sp.total);
            float acc = StatScale.To01(p.finishing);
            var sb = new System.Text.StringBuilder();
            sb.AppendFormat("<b>슛 파워 {0:0}</b> = 기본 {1:0} + 근육 {2:+0;−0;0} + 유연성 {3:+0;−0;0} → {4:0}{5}\n",
                sp.total, sp.baseValue, sp.muscle, sp.flexibility, sp.capped,
                sp.traits != 0f ? string.Format(" + 특전 {0:+0;−0} (100 상한 밖)", sp.traits) : "");
            sb.AppendFormat("   {0:0.0} m/s ({1:0} km/h) · 40m 도달 {2:0.00}초\n", v, v * 3.6f, Ball.TravelTime(40f, v, 5f));
            sb.AppendFormat("<b>마무리 {0}</b> → 정확도 {1:0.000} · 18m 오차 반경 {2:0.00} m\n\n", p.finishing, acc, BallModel.AimSpread(acc, 18f, 1f));
            sb.AppendFormat("{0}세 · 몸무게 {1:0.0} kg · 체지방 {2:0.0}% · 근육 {3:0.0} kg ({4:0.0}%)\n",
                p.age, p.WeightKg, p.BodyFatPercent, p.muscleKg, p.MusclePercent);
            sb.AppendFormat("속도 {0} · 민첩 {1} · 체력 {2}\n가속 {3} · 몸싸움 {4}\n",
                Mul(m.speed), Mul(m.agility), Mul(m.stamina), Mul(m.acceleration), Mul(m.physicality));
            float hAerial, hAgility;
            BodyModel.HeightEffects(p.heightCm, p.talent, out hAerial, out hAgility);
            sb.AppendFormat("키 {0:0} cm (잠재 {1:0}) → 공중볼 {2} · 민첩(키 몫) {3}{4}\n", p.heightCm, p.peakHeightCm, Mul(m.aerial), Mul(hAgility),
                TalentCatalog.Get(p.talent).heightDownsides ? "" : " <size=14><color=#9aa3ad>(재능: 키 단점 없음)</color></size>");
            sb.AppendFormat("부상 빈도: 하체 {0} · 근육 {1}\n", Mul(m.lowerBodyInjury, false), Mul(m.muscleInjury, false));
            sb.AppendFormat("몸 특성(자동): {0}", Traits(p.traits));
            sb.Append("\n<size=14><color=#8a939d>배율은 경기에 아직 안 이어졌다. 슛 파워·마무리는 슛 드릴에서 바로 쓰인다.</color></size>");
            return sb.ToString();
        }

        /// <summary>A multiplier, green when it helps and red when it hurts.</summary>
        static string Mul(float x, bool higherIsBetter = true)
        {
            string s = "×" + x.ToString("0.00");
            if (Mathf.Abs(x - 1f) < 0.005f) return s;
            bool helps = (x > 1f) == higherIsBetter;
            return "<color=#" + Hex(helps ? Good : Bad) + ">" + s + "</color>";
        }

        static string Traits(BodyTrait t)
        {
            if (t == BodyTrait.None) return "없음";
            var parts = new List<string>();
            if ((t & BodyTrait.ExcessFat) != 0) parts.Add("<color=#" + Hex(Bad) + ">체지방 과다</color> (하체 부상 ↑)");
            if ((t & BodyTrait.ExcessMuscle) != 0) parts.Add("<color=#" + Hex(Bad) + ">근육 과다</color> (체력·민첩 ↓, 근육 부상 ↑)");
            return string.Join(" · ", parts.ToArray());
        }

        static string Tag(PerkLink l)
        {
            return l == PerkLink.Live ? "[연결]" : l == PerkLink.MultiplierOnly ? "[배율만]" : "[미정]";
        }

        static string Hex(Color c) { return ColorUtility.ToHtmlStringRGB(c); }

        void Frames(List<Image> frames, int picked)
        {
            for (int i = 0; i < frames.Count; i++) frames[i].color = i == picked ? Color.white : new Color(0.25f, 0.28f, 0.32f);
        }

        // ------------------------------------------------------- ui helpers --

        RectTransform Box(RectTransform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
            go.GetComponent<Image>().color = c;
            return rt;
        }

        RectTransform RowBox(RectTransform parent, float height)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = Row;
            go.AddComponent<LayoutElement>().minHeight = height;
            return go.GetComponent<RectTransform>();
        }

        /// <summary>A labelled row: name on the left, controls after it.</summary>
        RectTransform Line(RectTransform parent, string label)
        {
            var go = new GameObject("Line_" + label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().minHeight = 44f;
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6f;
            h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            Fixed(Label(go.transform, label, 18, Dim, TextAnchor.MiddleLeft), 96f);
            return go.GetComponent<RectTransform>();
        }

        void Header(RectTransform parent, string s)
        {
            Text t = Label(parent, s, 18, new Color(0.95f, 0.82f, 0.45f), TextAnchor.MiddleLeft);
            t.gameObject.AddComponent<LayoutElement>().minHeight = 28f;     // grows if it wraps
        }

        void Note(RectTransform parent, string s)
        {
            Text t = Label(parent, s, 14, Dim, TextAnchor.UpperLeft);
            t.gameObject.AddComponent<LayoutElement>().minHeight = 20f;
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
            cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.55f);
            b.colors = cb;
            b.onClick.AddListener(onClick);
            Text t = Label(go.transform, label, size, Color.white, TextAnchor.MiddleCenter);
            t.raycastTarget = false;
            Stretch(t.rectTransform, Vector2.zero, Vector2.zero);
            return b;
        }

        Button Btn(Transform parent, string label, int size, UnityEngine.Events.UnityAction onClick, Vector2 aMin, Vector2 aMax)
        {
            Button b = Btn(parent, label, size, onClick);
            var rt = b.GetComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = rt.offsetMax = Vector2.zero;
            return b;
        }

        InputField Field(Transform parent, string value)
        {
            var go = new GameObject("NameField", typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.08f);
            Text t = Label(go.transform, "", 20, Color.white, TextAnchor.MiddleLeft);
            t.supportRichText = false;
            Stretch(t.rectTransform, new Vector2(10f, 2f), new Vector2(-10f, -2f));
            var f = go.GetComponent<InputField>();
            f.textComponent = t;
            f.characterLimit = 16;
            f.text = value;
            Fixed(f, 0f, 40f).GetComponent<LayoutElement>().flexibleWidth = 1f;
            return f;
        }

        Slider MakeSlider(Transform parent, float min, float max, float value)
        {
            var go = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(parent, false);
            Fixed(go.GetComponent<RectTransform>(), 0f, 30f).GetComponent<LayoutElement>().flexibleWidth = 1f;

            Image bg = new GameObject("Background", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            bg.transform.SetParent(go.transform, false);
            bg.color = new Color(0.05f, 0.06f, 0.08f);
            Stretch(bg.rectTransform, new Vector2(0f, 10f), new Vector2(0f, -10f));

            var fillArea = new GameObject("Fill Area", typeof(RectTransform)).GetComponent<RectTransform>();
            fillArea.SetParent(go.transform, false);
            Stretch(fillArea, new Vector2(6f, 10f), new Vector2(-6f, -10f));
            Image fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            fill.transform.SetParent(fillArea, false);
            fill.color = Picked;
            Stretch(fill.rectTransform, Vector2.zero, Vector2.zero);

            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform)).GetComponent<RectTransform>();
            handleArea.SetParent(go.transform, false);
            Stretch(handleArea, new Vector2(10f, 0f), new Vector2(-10f, 0f));
            Image handle = new GameObject("Handle", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            handle.transform.SetParent(handleArea, false);
            handle.rectTransform.sizeDelta = new Vector2(20f, 0f);
            handle.rectTransform.anchorMin = new Vector2(0f, 0f);
            handle.rectTransform.anchorMax = new Vector2(0f, 1f);

            var s = go.GetComponent<Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.minValue = min; s.maxValue = max; s.value = value;
            return s;
        }

        static void Stretch(RectTransform rt, Vector2 oMin, Vector2 oMax)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = oMin; rt.offsetMax = oMax;
        }

        static T Fixed<T>(T c, float width, float height = -1f) where T : Component
        {
            var le = c.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            if (width > 0f) { le.minWidth = width; le.preferredWidth = width; }
            if (height > 0f) { le.minHeight = height; le.preferredHeight = height; }
            return c;
        }

        static void Flexible(Component c, float minHeight)
        {
            var le = c.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            le.minHeight = minHeight;
            le.flexibleHeight = 1f;
        }
    }
}
