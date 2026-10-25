using UnityEngine;

namespace Career
{
    /// <summary>Something to do at a place: a name and how long it takes. What it does is not decided.</summary>
    public struct PlaceAction
    {
        /// <summary><see cref="minutes"/> value for "the rest of this part of the day" (sleeping early).</summary>
        public const int RestOfPart = -1;

        public string name;
        /// <summary>How long it takes, in 30-minute steps; one action point a step (<see cref="DayClock"/>).</summary>
        public int minutes;
        /// <summary>One of the chemistry candidates (PROJECT.md 10.9), named the same so it can be wired later.</summary>
        public bool chemistry;

        public PlaceAction(string name, int minutes, bool chemistry = false)
        {
            this.name = name;
            this.minutes = minutes;
            this.chemistry = chemistry;
        }

        public bool TakesRestOfPart { get { return minutes == RestOfPart; } }

        /// <summary>Action points it costs now: the minutes in points, or every point left.</summary>
        public int Cost(DayClock clock)
        {
            return TakesRestOfPart ? clock.points : DayClock.Cost(minutes);
        }
    }

    /// <summary>A place on the daily map. <see cref="mapPos"/> is 0..1 across the map, from the bottom left.</summary>
    public class Place
    {
        public string id, name, blurb;
        public string icon;         // a glyph on the marker, placeholder art
        public Vector2 mapPos;
        public Color color;
        public PlaceAction[] actions;
    }

    /// <summary>
    /// The places of the daily map (user, 2026-10-25): one line a place, so the ones the user's
    /// "and so on" leaves out go in as one more line. Actions have a name and a time - their
    /// effects and money are not built (PROJECT.md 10.10, 10.11).
    /// </summary>
    public static class PlaceCatalog
    {
        /// <summary>Names the chemistry candidates use (PROJECT.md 10.9).</summary>
        public const string TrainTogether = "같이 훈련", MealTogether = "같이 식사·여가";

        public static readonly Place[] All =
        {
            new Place { id = "training_ground", name = "훈련장", icon = "훈", mapPos = new Vector2(0.12f, 0.78f),
                color = new Color(0.30f, 0.62f, 0.36f), blurb = "팀 훈련과 개인 훈련을 하는 곳.",
                actions = new[] { new PlaceAction("개인 훈련", 120), new PlaceAction(TrainTogether, 120, true), new PlaceAction("회복 훈련", 60) } },
            new Place { id = "club", name = "클럽", icon = "클", mapPos = new Vector2(0.24f, 0.58f),
                color = new Color(0.25f, 0.45f, 0.75f), blurb = "구단 사무실과 라커룸, 의무실.",
                actions = new[] { new PlaceAction("감독 면담", 60), new PlaceAction("전술 미팅", 60), new PlaceAction("치료", 60) } },
            new Place { id = "home", name = "자택", icon = "집", mapPos = new Vector2(0.84f, 0.22f),
                color = new Color(0.80f, 0.62f, 0.35f), blurb = "쉬고 자는 곳.",
                actions = new[] { new PlaceAction("휴식", 60), new PlaceAction("일찍 자기", PlaceAction.RestOfPart), new PlaceAction("집밥", 60) } },
            new Place { id = "cafe", name = "카페", icon = "카", mapPos = new Vector2(0.42f, 0.76f),
                color = new Color(0.62f, 0.42f, 0.30f), blurb = "커피 한 잔, 가벼운 만남.",
                actions = new[] { new PlaceAction("혼자 커피", 30), new PlaceAction(MealTogether, 90, true) } },
            new Place { id = "restaurant", name = "식당", icon = "식", mapPos = new Vector2(0.56f, 0.60f),
                color = new Color(0.85f, 0.40f, 0.30f), blurb = "든든한 한 끼.",
                actions = new[] { new PlaceAction("혼자 식사", 60), new PlaceAction(MealTogether, 90, true) } },
            new Place { id = "plaza", name = "광장", icon = "광", mapPos = new Vector2(0.49f, 0.24f),
                color = new Color(0.55f, 0.55f, 0.60f), blurb = "사람이 모이는 도심 한가운데.",
                actions = new[] { new PlaceAction("산책", 60), new PlaceAction("팬 만나기", 60) } },
            new Place { id = "department_store", name = "백화점", icon = "백", mapPos = new Vector2(0.77f, 0.62f),
                color = new Color(0.70f, 0.40f, 0.70f), blurb = "옷과 물건을 사는 곳.",
                actions = new[] { new PlaceAction("쇼핑", 120) } },
            new Place { id = "car_showroom", name = "자동차 전시장", icon = "차", mapPos = new Vector2(0.90f, 0.84f),
                color = new Color(0.35f, 0.65f, 0.75f), blurb = "새 차를 구경하는 곳.",
                actions = new[] { new PlaceAction("차 구경", 60) } },
        };

        public static Place Get(string id)
        {
            for (int i = 0; i < All.Length; i++)
                if (All[i].id == id) return All[i];
            return null;
        }
    }
}
