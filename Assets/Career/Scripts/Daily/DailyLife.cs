using System;
using System.Collections.Generic;
using UnityEngine;

namespace Career
{
    /// <summary>
    /// Daily life between two matches (user, 2026-10-25): from the day after a match to the
    /// day before the next. Energy is what is left of the player, 0..100%: each part of the day
    /// gives 12 x energy points, rounded up, so a hard morning leaves a short afternoon. Every
    /// morning at 06:00 some of it comes back.
    ///
    /// An action is done the moment it is picked (user, 2026-10-26; the list and routine were
    /// dropped): its points, time and energy go at once, and a special event may follow
    /// (<see cref="DailyEvents"/>, <see cref="LastEvent"/>).
    ///
    /// The morning recovery is the base x stamina x perks x how long he slept, plus what
    /// recovery training added. Sleep runs from when he goes to bed to 06:00. When the evening's
    /// points run out he either goes to bed - the night is skipped - or goes on into the night
    /// (user); at the end of the night's points he is in bed as well. Numbers are PROVISIONAL
    /// (PROJECT.md 10.11).
    /// </summary>
    public class DailyLife
    {
        /// <summary>Energy won back at 06:00, before stamina, perks and sleep.</summary>
        public const float BaseRecovery = 40f;
        /// <summary>Stamina scales recovery: x0.7 at stamina 1, x1.3 at 100.</summary>
        public const float RecoveryAtLowStamina = 0.7f, RecoveryAtHighStamina = 1.3f;
        /// <summary>What the growth path (육성 방향) will do to recovery. There is no growth path yet.</summary>
        public const float GrowthPathRecovery = 1f;
        /// <summary>Sleep scales recovery: x0.4 + x0.075 an hour, between x0.4 and x1.1 - 8 hours is x1, 6 hours x0.85.</summary>
        public const float SleepBase = 0.4f, SleepPerHour = 0.075f, SleepMin = 0.4f, SleepMax = 1.1f;
        /// <summary>Sleep the first morning counts, before there was a night to count: 8 hours, x1.</summary>
        public const float FirstNightHours = 8f;

        public readonly PlayerProfile player;
        public readonly Squad squad;
        public readonly DayClock clock = new DayClock();
        /// <summary>The day's log, a line per thing done, per part.</summary>
        public readonly List<string>[] log = NewLog();
        /// <summary>The day the next match is played (from the season's calendar). Daily life ends the night before.</summary>
        public int matchDay { get; private set; }
        /// <summary>0..1 for the special events' chances; swapped for a fixed value in tests.</summary>
        public Func<float> roll = () => UnityEngine.Random.value;

        /// <summary>The evening is over and he has not said yet whether he goes to bed (<see cref="ChooseNight"/>).</summary>
        public bool AwaitingNightChoice { get; private set; }
        /// <summary>What the last action brought, for the result panel; null if it was an ordinary one.</summary>
        public EventResult LastEvent { get; private set; }
        /// <summary>Hours slept before the morning he woke to.</summary>
        public float LastSleepHours { get; private set; }

        float recoveryBonus;    // from recovery training, at the next 06:00

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
            LastSleepHours = FirstNightHours;
            Recover(FirstNightHours);
            UpdateForm();
            clock.Begin(DayClock.PointsFor(Energy));
        }

        /// <summary>Energy left, 0..100%. Stored as the profile's fatigue: energy = 100 - fatigue.</summary>
        public float Energy
        {
            get { return 100f - player.fatigue; }
            set { player.fatigue = 100f - Mathf.Clamp(value, 0f, 100f); }
        }

        /// <summary>The calendar moved the next match (a knockout round was drawn); daily life follows it.</summary>
        public void SetMatchDay(int day) { matchDay = day; }

        public bool IsMatchDay { get { return clock.day >= matchDay; } }
        public int DaysToMatch { get { return matchDay - clock.day; } }

        // ------------------------------------------------------------ recovery --

        /// <summary>x0.4 + x0.075 an hour, kept between x0.4 and x1.1 (assistant's provisional).</summary>
        public static float SleepMultiplier(float hours)
        {
            return Mathf.Clamp(SleepBase + SleepPerHour * hours, SleepMin, SleepMax);
        }

        /// <summary>Recovery before sleep and training: 40 x lerp(0.7, 1.3, stamina) x perks x growth path.</summary>
        public float BaseMorningRecovery
        {
            get
            {
                int stamina = BodyModel.WithBody(player.stamina, BodyModel.Points(player).stamina);
                return BaseRecovery * Mathf.Lerp(RecoveryAtLowStamina, RecoveryAtHighStamina, StatScale.To01(stamina))
                       * PerkCatalog.RecoveryMultiplier(player.perks) * GrowthPathRecovery;
            }
        }

        /// <summary>What recovery training has added to tomorrow's morning so far, %p.</summary>
        public float RecoveryBonus { get { return recoveryBonus; } }

        /// <summary>Energy back at 06:00 after this many hours of sleep: base x sleep + recovery training.</summary>
        public float MorningRecovery(float sleepHours)
        {
            return BaseMorningRecovery * SleepMultiplier(sleepHours) + recoveryBonus;
        }

        /// <summary>Hours he would sleep if he went to bed on this clock.</summary>
        public float SleepHoursIfNow { get { return clock.MinutesToMorning / 60f; } }

        // ------------------------------------------------------------ actions --

        /// <summary>
        /// The action as it will go for this player: the same, or - if an event blocks it - a short,
        /// empty visit.
        /// </summary>
        public PlaceAction Effective(string place, PlaceAction a)
        {
            return DailyEvents.IsBlocked(player, place, a.name) ? new PlaceAction(a.name, DailyEvents.BlockedMinutes, 0f) : a;
        }

        /// <summary>Whether it can be done now: not on a match day or in the question at the evening's end, enough points, and bed only in its own part (early to bed in the evening, falling asleep in the night).</summary>
        public bool CanDo(string place, PlaceAction a)
        {
            if (IsMatchDay || AwaitingNightChoice) return false;
            if (a.sleeps) return a.name == PlaceCatalog.SleepNight ? clock.part == DayPart.Night : clock.part == DayPart.Evening;
            int cost = Effective(place, a).Cost;
            return cost > 0 && cost <= clock.points;
        }

        /// <summary>
        /// Does an action now: points, time, energy, then its special events. Returns what
        /// happened, for the status line; an event's page is in <see cref="LastEvent"/>.
        /// </summary>
        public string Do(string place, string action)
        {
            LastEvent = null;
            PlaceAction a;
            if (!PlaceCatalog.TryAction(place, action, out a) || !CanDo(place, a)) return "";
            if (a.sleeps) return (a.name == PlaceCatalog.SleepNight ? "잠들었다.  " : "일찍 잤다.  ") + GoToBed();

            PlaceAction eff = Effective(place, a);
            int part = (int)clock.part;
            clock.Spend(eff.Cost);
            Energy += eff.energy;
            recoveryBonus += eff.morningBonus;

            EventResult ev = DailyEvents.Resolve(player, place, action, roll);
            if (ev != null)
            {
                Apply(ev);
                LastEvent = ev;
            }
            string line = PlaceCatalog.Get(place).name + " · " + action + (ev != null && ev.blocked ? " (입장 불가)" : "");
            log[part].Add(line + " → " + clock.Time);
            string msg = line + " → " + clock.Time + ", 체력 " + Mathf.RoundToInt(Energy) + "%.";
            if (clock.points == 0) msg += "  " + Advance();
            return msg;
        }

        void Apply(EventResult ev)
        {
            if (ev.mental == 0) return;
            int before = player.mentalStrength;
            player.mentalStrength = Mathf.Clamp(before + ev.mental, StatScale.Min, StatScale.Max);
            ev.effects.Add("정신력 " + (ev.mental > 0 ? "+" : "−") + Mathf.Abs(ev.mental) + " (" + before + " → " + player.mentalStrength + ")");
        }

        /// <summary>Leaves whatever points are left and starts the next part.</summary>
        public string SkipPart()
        {
            if (AwaitingNightChoice || IsMatchDay) return "";
            LastEvent = null;
            int left = clock.points;
            return "남은 " + left + "P를 버렸다.  " + Advance();
        }

        string Advance()
        {
            if (clock.part == DayPart.Evening)
            {
                AwaitingNightChoice = true;
                return "저녁이 끝났다. 바로 잘까, 새벽으로 넘어갈까?";
            }
            if (clock.part == DayPart.Night) return GoToBed();     // the night's points are spent: bed
            return StartNextPart();
        }

        string StartNextPart()
        {
            clock.NextPart(DayClock.PointsFor(Energy));
            return DayClock.PartName(clock.part) + " " + clock.Time + " 시작: 체력 " + Mathf.RoundToInt(Energy) + "% → " + clock.points + "P.";
        }

        /// <summary>
        /// The answer at the end of the evening (user): straight to bed - the night is skipped - or
        /// on into the night. Sleep is counted from the clock to 06:00 either way.
        /// </summary>
        public string ChooseNight(bool sleepNow)
        {
            if (!AwaitingNightChoice) return "";
            AwaitingNightChoice = false;
            return sleepNow ? "바로 잤다.  " + GoToBed() : "새벽으로 넘어갔다.  " + StartNextPart();
        }

        /// <summary>He goes to bed on this clock: the sleep is what is left to 06:00, and the next day begins.</summary>
        string GoToBed()
        {
            float hours = SleepHoursIfNow;
            return NewDayStarts(hours);
        }

        /// <summary>The night is over: a new day, energy comes back by the sleep, the squad's day ends.</summary>
        string NewDayStarts(float sleepHours)
        {
            squad.EndDay();
            clock.NextDay();
            LastSleepHours = sleepHours;
            float back = MorningRecovery(sleepHours);
            Recover(sleepHours);
            UpdateForm();
            for (int i = 0; i < 4; i++) log[i].Clear();
            string lead = "수면 " + Hours(sleepHours) + " → 회복 +" + Mathf.RoundToInt(back) + "%p.  ";
            if (IsMatchDay)
            {
                clock.Begin(0);
                return lead + clock.day + "일째 — 경기 날이다.";
            }
            clock.Begin(DayClock.PointsFor(Energy));
            return lead + clock.day + "일째 아침: 체력 " + Mathf.RoundToInt(Energy) + "% → " + clock.points + "P.";
        }

        /// <summary>
        /// The day's personal form (user: from fatigue, mental strength, recent results and chemistry,
        /// PROJECT.md 10.15), worked out each morning once the energy is back. Only the human has it
        /// for now; whether the teammates do too is asked.
        /// </summary>
        void UpdateForm()
        {
            player.personalForm = FormModel.Personal(player.fatigue, player.mentalStrength, player.recentResults, squad.PlayerChemistry);
        }

        void Recover(float sleepHours)
        {
            Energy += MorningRecovery(sleepHours);
            recoveryBonus = 0f;
        }

        /// <summary>"8시간", "6시간 30분".</summary>
        public static string Hours(float hours)
        {
            int m = Mathf.RoundToInt(hours * 60f), h = m / 60;
            if (m % 60 == 0) return h + "시간";
            return h == 0 ? m + "분" : h + "시간 " + (m % 60) + "분";
        }

        static List<string>[] NewLog()
        {
            return new[] { new List<string>(), new List<string>(), new List<string>(), new List<string>() };
        }
    }
}
