using UnityEngine;

namespace Career
{
    /// <summary>
    /// Something to do at a place: how long it takes and what it does to energy. Its other
    /// effects (stats, chemistry) are not decided. Special events hang on
    /// it in <see cref="DailyEvents"/>.
    /// </summary>
    public struct PlaceAction
    {
        public string name;
        /// <summary>How long it takes, in 30-minute steps; one action point a step (<see cref="DayClock"/>).</summary>
        public int minutes;
        /// <summary>Energy it takes (negative) or gives back (positive), in percentage points.</summary>
        public float energy;
        /// <summary>Extra energy at the next 06:00 recovery, in percentage points (recovery training).</summary>
        public float morningBonus;
        /// <summary>Goes to bed: the day ends here, and the sleep is counted from the clock now (<see cref="DailyLife"/>).</summary>
        public bool sleeps;
        /// <summary>One of the chemistry candidates (PROJECT.md 10.9), named the same so it can be wired later.</summary>
        public bool chemistry;

        public PlaceAction(string name, int minutes, float energy, bool chemistry = false, float morningBonus = 0f, bool sleeps = false)
        {
            this.name = name;
            this.minutes = minutes;
            this.energy = energy;
            this.morningBonus = morningBonus;
            this.chemistry = chemistry;
            this.sleeps = sleeps;
        }

        /// <summary>Action points it costs: the minutes in points. Going to bed costs none - it ends the day.</summary>
        public int Cost { get { return sleeps ? 0 : DayClock.Cost(minutes); } }
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
        public const string SleepEarly = "일찍 자기";
        /// <summary>What recovery training adds to the next morning's recovery, %p (user: costs more now, gives more back at 06:00). PROVISIONAL.</summary>
        public const float RecoveryTrainingBonus = 10f;

        /// <summary>
        /// Energy back for every 30 minutes of leisure (user: everything but training is recovery),
        /// and of resting at home or being treated. PROVISIONAL.
        /// </summary>
        public const float LeisurePerHalfHour = 2f, RestPerHalfHour = 3f;

        static PlaceAction Leisure(string name, int minutes, bool chemistry = false)
        {
            return new PlaceAction(name, minutes, LeisurePerHalfHour * minutes / DayClock.MinutesPerPoint, chemistry);
        }

        static PlaceAction Rest(string name, int minutes)
        {
            return new PlaceAction(name, minutes, RestPerHalfHour * minutes / DayClock.MinutesPerPoint);
        }

        public static readonly Place[] All =
        {
            new Place { id = "training_ground", name = "훈련장", icon = "훈", mapPos = new Vector2(0.12f, 0.78f),
                color = new Color(0.30f, 0.62f, 0.36f), blurb = "팀 훈련과 개인 훈련을 하는 곳.",
                actions = new[] { new PlaceAction("개인 훈련", 120, -15f), new PlaceAction(TrainTogether, 120, -15f, true), new PlaceAction("회복 훈련", 60, -10f, false, RecoveryTrainingBonus) } },
            new Place { id = "clubhouse", name = "구단 클럽하우스", icon = "구", mapPos = new Vector2(0.24f, 0.58f),
                color = new Color(0.25f, 0.45f, 0.75f), blurb = "구단 사무실과 라커룸, 의무실.",
                actions = new[] { new PlaceAction("감독 면담", 60, -2f), new PlaceAction("전술 미팅", 60, -2f), Rest("치료", 60) } },
            new Place { id = "home", name = "자택", icon = "집", mapPos = new Vector2(0.84f, 0.22f),
                color = new Color(0.80f, 0.62f, 0.35f), blurb = "쉬고 자는 곳.",
                actions = new[] { Rest("휴식", 60), new PlaceAction(SleepEarly, 0, 0f, false, 0f, true), Leisure("집밥", 60) } },
            new Place { id = "cafe", name = "카페", icon = "카", mapPos = new Vector2(0.42f, 0.76f),
                color = new Color(0.62f, 0.42f, 0.30f), blurb = "커피 한 잔, 가벼운 만남.",
                actions = new[] { Leisure("혼자 커피", 30), Leisure(MealTogether, 90, true) } },
            new Place { id = "restaurant", name = "식당", icon = "식", mapPos = new Vector2(0.56f, 0.60f),
                color = new Color(0.85f, 0.40f, 0.30f), blurb = "든든한 한 끼.",
                actions = new[] { Leisure("혼자 식사", 60), Leisure(MealTogether, 90, true) } },
            new Place { id = "plaza", name = "광장", icon = "광", mapPos = new Vector2(0.49f, 0.24f),
                color = new Color(0.55f, 0.55f, 0.60f), blurb = "사람이 모이는 도심 한가운데.",
                actions = new[] { Leisure("산책", 60), Leisure("팬 만나기", 60) } },
            new Place { id = "department_store", name = "백화점", icon = "백", mapPos = new Vector2(0.77f, 0.62f),
                color = new Color(0.70f, 0.40f, 0.70f), blurb = "옷과 물건을 사는 곳.",
                actions = new[] { Leisure("쇼핑", 120) } },
            new Place { id = "car_showroom", name = "자동차 전시장", icon = "차", mapPos = new Vector2(0.90f, 0.84f),
                color = new Color(0.35f, 0.65f, 0.75f), blurb = "새 차를 구경하는 곳.",
                actions = new[] { Leisure("차 구경", 60) } },
            // the club for a night out, not the team's (user). No drinks. Under 18 he is turned away (DailyEvents).
            new Place { id = "nightclub", name = "나이트클럽", icon = "나", mapPos = new Vector2(0.77f, 0.87f),
                color = new Color(0.55f, 0.30f, 0.80f), blurb = "음악과 춤이 있는 밤의 클럽.",
                actions = new[] { Leisure("파티", 120), Leisure("춤", 60) } },
        };

        public static Place Get(string id)
        {
            for (int i = 0; i < All.Length; i++)
                if (All[i].id == id) return All[i];
            return null;
        }

        public static bool TryAction(string placeId, string actionName, out PlaceAction action)
        {
            action = default(PlaceAction);
            Place p = Get(placeId);
            if (p == null) return false;
            foreach (PlaceAction a in p.actions)
                if (a.name == actionName) { action = a; return true; }
            return false;
        }
    }
}
