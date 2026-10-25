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
    /// and a new day begins. How many points a part has is the energy left when it starts
    /// (<see cref="PointsFor"/>, PROJECT.md 10.11).
    /// </summary>
    [Serializable]
    public class DayClock
    {
        public const int MinutesPerPoint = 30;
        public const int PartMinutes = 6 * 60;
        /// <summary>Points a part gets at full energy - the whole six hours, so the clock never runs past the part.</summary>
        public const int MaxPoints = PartMinutes / MinutesPerPoint;

        public int day = 1;
        public DayPart part = DayPart.Morning;
        /// <summary>Minutes gone in this part, 0..360.</summary>
        public int minutes;
        /// <summary>Points left in this part.</summary>
        public int points;
        /// <summary>Points this part started with.</summary>
        public int partPoints;

        /// <summary>
        /// Points a part gives with this much energy left, 0..100% (user): 12 x energy, rounded up -
        /// 80% gives 9.6, so 10.
        /// </summary>
        public static int PointsFor(float energyPercent)
        {
            // the small take-off keeps 50% at exactly 6, not 7 from a float a hair over
            return Mathf.Clamp(Mathf.CeilToInt(MaxPoints * energyPercent / 100f - 1e-4f), 0, MaxPoints);
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
