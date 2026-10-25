using System;
using UnityEngine;

namespace Career
{
    /// <summary>The four six-hour parts of a day (user): morning 06-12, afternoon 12-18, evening 18-24, night 00-06.</summary>
    public enum DayPart { Morning, Afternoon, Evening, Night }

    /// <summary>
    /// Time and action points (user, 2026-10-25). Everything done costs points, one for every
    /// 30 minutes it takes. Each part of the day has its own points; once they are spent the
    /// clock moves to the start of the next part and they fill again. The night ends at 06:00
    /// and a new day begins. How many points a part has depends on stamina (PROJECT.md 10.11).
    /// </summary>
    [Serializable]
    public class DayClock
    {
        public const int MinutesPerPoint = 30;
        public const int PartMinutes = 6 * 60;
        /// <summary>Points a part gets at stamina 1 and at 100. 12 is the whole six hours, so the clock never runs past the part.</summary>
        public const int MinPoints = 6, MaxPoints = PartMinutes / MinutesPerPoint;

        public int day = 1;
        public DayPart part = DayPart.Morning;
        /// <summary>Minutes gone in this part, 0..360.</summary>
        public int minutes;
        /// <summary>Points left in this part.</summary>
        public int points;
        /// <summary>Points this part started with.</summary>
        public int partPoints;

        /// <summary>Points a part gives at this stamina - the stamina as it plays, body points added.</summary>
        public static int PointsFor(int stamina)
        {
            return Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(MinPoints, MaxPoints, StatScale.To01(stamina))), MinPoints, MaxPoints);
        }

        public static int PointsFor(PlayerProfile p)
        {
            return PointsFor(BodyModel.WithBody(p.stamina, BodyModel.Points(p).stamina));
        }

        public static int StartHour(DayPart part)
        {
            switch (part)
            {
                case DayPart.Morning: return 6;
                case DayPart.Afternoon: return 12;
                case DayPart.Evening: return 18;
                default: return 0;
            }
        }

        public static string PartName(DayPart part)
        {
            switch (part)
            {
                case DayPart.Morning: return "오전";
                case DayPart.Afternoon: return "오후";
                case DayPart.Evening: return "저녁";
                default: return "새벽";
            }
        }

        public static int Cost(int actionMinutes)
        {
            return Mathf.Max(1, Mathf.CeilToInt(actionMinutes / (float)MinutesPerPoint));
        }

        /// <summary>"HH:MM" on the clock now.</summary>
        public string Time
        {
            get
            {
                int m = StartHour(part) * 60 + minutes;
                return string.Format("{0:00}:{1:00}", (m / 60) % 24, m % 60);
            }
        }

        public void Begin(int perPart)
        {
            minutes = 0;
            points = partPoints = perPart;
        }

        public bool CanAfford(int cost) { return cost > 0 && cost <= points; }

        /// <summary>Spends the points and moves the clock. Returns false if there are not enough.</summary>
        public bool Spend(int cost)
        {
            if (!CanAfford(cost)) return false;
            points -= cost;
            minutes = Mathf.Min(PartMinutes, minutes + cost * MinutesPerPoint);
            return true;
        }

        /// <summary>
        /// To the start of the next part, whatever is left; the points fill to
        /// <paramref name="perPart"/>. Returns true when the night ended and a new day began.
        /// </summary>
        public bool NextPart(int perPart)
        {
            bool newDay = part == DayPart.Night;
            part = newDay ? DayPart.Morning : part + 1;
            if (newDay) day++;
            Begin(perPart);
            return newDay;
        }
    }
}
