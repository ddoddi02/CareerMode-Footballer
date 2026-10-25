using System;
using System.Collections.Generic;
using UnityEngine;

namespace Career
{
    /// <summary>One thing on the day's list: an action at a place. Done once its part was confirmed.</summary>
    [Serializable]
    public class PlanItem
    {
        public string place, action;
        public bool done;
    }

    /// <summary>The list for one part of the day.</summary>
    [Serializable]
    public class PartPlan
    {
        public List<PlanItem> items = new List<PlanItem>();
    }

    /// <summary>What a part of the day comes to if its list is played as it stands.</summary>
    public struct PartForecast
    {
        public int points;          // points the part has (left, for the part now)
        public int pointsLeft;      // after the items that fit
        public int endMinutes;      // minutes into the part when they are done
        public float energyAfter;   // energy when they are done
        public int[] costs;         // per item; done items 0
        public bool[] fits;         // per item; false = cut for want of points
    }

    /// <summary>
    /// Daily life between two matches (user, 2026-10-25): from the day after a match to the
    /// day before the next. Energy is what is left of the player, 0..100%: each part of the day
    /// gives 12 x energy points, rounded up, so a hard morning leaves a short afternoon. Every
    /// morning at 06:00 some of it comes back.
    ///
    /// Actions go on a list first, not straight through: the list for the part may use as
    /// many points as it has or fewer, shows the energy it will leave, and is spent in order
    /// on confirm. A day's lists can be saved as a routine and laid over a later day, cut from
    /// the back where the points do not reach. Numbers are PROVISIONAL (PROJECT.md 10.11).
    /// </summary>
    public class DailyLife
    {
        /// <summary>Energy won back at 06:00, before stamina and perks.</summary>
        public const float BaseRecovery = 40f;
        /// <summary>Stamina scales recovery: x0.7 at stamina 1, x1.3 at 100.</summary>
        public const float RecoveryAtLowStamina = 0.7f, RecoveryAtHighStamina = 1.3f;
        /// <summary>What the growth path (육성 방향) will do to recovery. There is no growth path yet.</summary>
        public const float GrowthPathRecovery = 1f;

        public readonly PlayerProfile player;
        public readonly Squad squad;
        public readonly DayClock clock = new DayClock();
        public readonly PartPlan[] today = NewDay();
        /// <summary>The saved routine, a list per part; null until one is saved.</summary>
        public PartPlan[] routine;
        /// <summary>The day the next match is played. Daily life ends the night before.</summary>
        public readonly int matchDay;

        float morningBonus;     // from sleeping early, at the next 06:00

        /// <summary>
        /// Starts the morning after a match: energy as the match left it, then the first
        /// morning's recovery. <paramref name="daysToMatch"/> days of daily life follow.
        /// </summary>
        public DailyLife(PlayerProfile player, Squad squad, float energyAfterMatch, int daysToMatch, int firstDay = 1)
        {
            this.player = player;
            this.squad = squad;
            Energy = energyAfterMatch;
            clock.day = firstDay;
            clock.part = DayPart.Morning;
            matchDay = firstDay + daysToMatch;
            Recover();
            clock.Begin(DayClock.PointsFor(Energy));
        }

        /// <summary>Energy left, 0..100%. Stored as the profile's fatigue: energy = 100 - fatigue.</summary>
        public float Energy
        {
            get { return 100f - player.fatigue; }
            set { player.fatigue = 100f - Mathf.Clamp(value, 0f, 100f); }
        }

        public bool IsMatchDay { get { return clock.day >= matchDay; } }
        public int DaysToMatch { get { return matchDay - clock.day; } }
        public PartPlan Current { get { return today[(int)clock.part]; } }

        /// <summary>
        /// Energy back at 06:00 (user: from stamina and traits, and the growth path):
        /// 40 x lerp(0.7, 1.3, stamina) x perks x growth path. Sleeping early adds its bonus on top.
        /// </summary>
        public float MorningRecovery
        {
            get
            {
                int stamina = BodyModel.WithBody(player.stamina, BodyModel.Points(player).stamina);
                return BaseRecovery * Mathf.Lerp(RecoveryAtLowStamina, RecoveryAtHighStamina, StatScale.To01(stamina))
                       * PerkCatalog.RecoveryMultiplier(player.perks) * GrowthPathRecovery;
            }
        }

        // ------------------------------------------------------------ the list --

        /// <summary>Points the current list does not use yet.</summary>
        public int FreePoints { get { return Forecast((int)clock.part, clock.points, Energy).pointsLeft; } }

        public bool CanAdd(PlaceAction a)
        {
            if (IsMatchDay) return false;
            int free = FreePoints;
            int cost = a.Cost(free);
            return cost > 0 && cost <= free;
        }

        /// <summary>Puts an action at the end of this part's list, if the points left reach.</summary>
        public bool Add(string place, string action)
        {
            PlaceAction a;
            if (!PlaceCatalog.TryAction(place, action, out a) || !CanAdd(a)) return false;
            Current.items.Add(new PlanItem { place = place, action = action });
            return true;
        }

        /// <summary>Takes an item off a list. Done items stay.</summary>
        public bool Remove(DayPart part, int index)
        {
            List<PlanItem> items = today[(int)part].items;
            if (index < 0 || index >= items.Count || items[index].done) return false;
            items.RemoveAt(index);
            return true;
        }

        /// <summary>
        /// Plays a list on paper: <paramref name="points"/> and <paramref name="energy"/> at its
        /// start. Items past the points do not fit - those a routine fill cuts.
        /// </summary>
        public PartForecast Forecast(int part, int points, float energy)
        {
            List<PlanItem> items = today[part].items;
            var f = new PartForecast { points = points, costs = new int[items.Count], fits = new bool[items.Count] };
            int free = points, minutes = part == (int)clock.part ? clock.minutes : 0;
            for (int i = 0; i < items.Count; i++)
            {
                PlaceAction a;
                if (items[i].done || !PlaceCatalog.TryAction(items[i].place, items[i].action, out a)) { f.fits[i] = true; continue; }
                int cost = a.Cost(free);
                f.costs[i] = cost;
                f.fits[i] = cost > 0 && cost <= free;
                if (!f.fits[i]) continue;
                free -= cost;
                minutes += cost * DayClock.MinutesPerPoint;
                energy = Mathf.Clamp(energy + a.energy, 0f, 100f);
            }
            f.pointsLeft = free;
            f.endMinutes = Mathf.Min(DayClock.PartMinutes, minutes);
            f.energyAfter = energy;
            return f;
        }

        /// <summary>The rest of today, part by part: the one now, then each later one with the points its forecast energy gives.</summary>
        public PartForecast[] ForecastDay()
        {
            var r = new PartForecast[4];
            float e = Energy;
            int points = clock.points;
            for (int p = (int)clock.part; p < 4; p++)
            {
                r[p] = Forecast(p, points, e);
                e = r[p].energyAfter;
                points = DayClock.PointsFor(e);
            }
            return r;
        }

        /// <summary>
        /// Spends this part's list in order: time, points, energy. A part with no points left
        /// gives way to the next. Returns what happened, for the status line.
        /// </summary>
        public string Confirm()
        {
            var done = new List<string>();
            foreach (PlanItem it in Current.items)
            {
                if (it.done) continue;
                PlaceAction a;
                if (!PlaceCatalog.TryAction(it.place, it.action, out a)) continue;
                int cost = a.Cost(clock.points);
                if (!clock.Spend(cost)) break;
                Energy += a.energy;
                morningBonus += a.morningBonus;
                it.done = true;
                done.Add(it.action);
            }
            Current.items.RemoveAll(it => !it.done);     // what did not fit goes
            if (done.Count == 0) return "할 일이 없다.";
            string msg = string.Join(", ", done.ToArray()) + " → " + clock.Time + ", 체력 " + Mathf.RoundToInt(Energy) + "%.";
            if (clock.points == 0) msg += "  " + Advance();
            return msg;
        }

        /// <summary>Leaves whatever points are left and starts the next part.</summary>
        public string SkipPart()
        {
            int left = clock.points;
            today[(int)clock.part].items.RemoveAll(it => !it.done);
            return "남은 " + left + "P를 버렸다.  " + Advance();
        }

        string Advance()
        {
            if (clock.part != DayPart.Night)
            {
                clock.NextPart(DayClock.PointsFor(Energy));
                Trim();
                return DayClock.PartName(clock.part) + " " + clock.Time + " 시작: 체력 " + Mathf.RoundToInt(Energy) + "% → " + clock.points + "P.";
            }
            // the night is over: a new day, energy comes back, the squad's day ends
            squad.EndDay();
            clock.NextPart(0);
            Recover();
            for (int i = 0; i < 4; i++) today[i] = new PartPlan();
            if (IsMatchDay)
            {
                clock.Begin(0);
                return clock.day + "일째 — 경기 날이다. (경기 연결은 아직 없다.)";
            }
            clock.Begin(DayClock.PointsFor(Energy));
            return clock.day + "일째 아침: 체력 " + Mathf.RoundToInt(Energy) + "% → " + clock.points + "P.";
        }

        void Recover()
        {
            Energy += MorningRecovery + morningBonus;
            morningBonus = 0f;
        }

        /// <summary>Cuts this part's list from the back until it fits the points.</summary>
        void Trim()
        {
            List<PlanItem> items = Current.items;
            while (true)
            {
                PartForecast f = Forecast((int)clock.part, clock.points, Energy);
                int last = Array.LastIndexOf(f.fits, false);
                if (last < 0) return;
                for (int i = items.Count - 1; i >= 0; i--)
                    if (!items[i].done) { items.RemoveAt(i); break; }
            }
        }

        // ------------------------------------------------------------ routine --

        /// <summary>Saves today's lists, done and still to do, as the routine.</summary>
        public void SaveRoutine()
        {
            routine = NewDay();
            for (int p = 0; p < 4; p++)
                foreach (PlanItem it in today[p].items)
                    routine[p].items.Add(new PlanItem { place = it.place, action = it.action });
        }

        /// <summary>
        /// Lays the routine over the rest of today: this part's list goes on from where it has got
        /// to, later parts take the routine's list. This part is cut to its points from the back;
        /// each later one is cut when it starts. Returns false with no routine.
        /// </summary>
        public bool FillFromRoutine()
        {
            if (routine == null || IsMatchDay) return false;
            for (int p = (int)clock.part; p < 4; p++)
            {
                List<PlanItem> items = today[p].items;
                items.RemoveAll(it => !it.done);
                int skip = items.Count;     // what is done here already, the routine's first ones
                for (int i = skip; i < routine[p].items.Count; i++)
                    items.Add(new PlanItem { place = routine[p].items[i].place, action = routine[p].items[i].action });
            }
            Trim();
            return true;
        }

        static PartPlan[] NewDay()
        {
            return new[] { new PartPlan(), new PartPlan(), new PartPlan(), new PartPlan() };
        }
    }
}
