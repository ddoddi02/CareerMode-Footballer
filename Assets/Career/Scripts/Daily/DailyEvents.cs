using System;
using System.Collections.Generic;
using UnityEngine;

namespace Career
{
    /// <summary>
    /// A special event on an action (user, 2026-10-26): the action goes as usual unless its
    /// condition holds, and then this happens as well - or instead, if it blocks. One row of the
    /// <see cref="DailyEvents.Rows"/> table.
    /// </summary>
    public class ActionEvent
    {
        public string id;
        public string place;
        /// <summary>The action it hangs on; null = any action at the place.</summary>
        public string action;
        public Func<PlayerProfile, bool> condition;
        /// <summary>Chance it happens once the condition holds, 0..1; null = always.</summary>
        public Func<PlayerProfile, float> chance;
        /// <summary>The action does not take place: it costs <see cref="DailyEvents.BlockedMinutes"/> and does nothing.</summary>
        public bool blocks;
        /// <summary>Change to mental strength (정신력) when it happens.</summary>
        public int mental;
        /// <summary>The visual-novel text of the page when it happens.</summary>
        public string text;
        /// <summary>Text when the condition held but the chance did not come up; null = nothing shown.</summary>
        public string missText;
    }

    /// <summary>What happened on one action: the page the result panel shows.</summary>
    public class EventResult
    {
        public string title;
        public readonly List<string> pages = new List<string>();     // one block of text per event that spoke
        public readonly List<string> fired = new List<string>();     // ids of the events that happened
        public readonly List<string> effects = new List<string>();   // "정신력 −8 (50 → 42)", added by whoever applies them
        public bool blocked;
        public int mental;
    }

    /// <summary>
    /// The special events of the daily map (user, 2026-10-26). The base event of an action is
    /// just doing it; a row here turns up when its condition holds. One row, one event.
    /// Values are PROVISIONAL (PROJECT.md 10.12).
    /// </summary>
    public static class DailyEvents
    {
        /// <summary>Age of an adult (Spain, 18): below it, no nightclub.</summary>
        public const int AdultAge = 18;
        /// <summary>What a blocked action costs: a short walk to the door and back.</summary>
        public const int BlockedMinutes = 30;
        /// <summary>Chance the paparazzi catch him at the nightclub door: 5% at fame 0, 60% at 100.</summary>
        public const float PaparazziAtNoFame = 0.05f, PaparazziAtFullFame = 0.60f;
        public const int PaparazziMental = -8;

        public static readonly ActionEvent[] Rows =
        {
            new ActionEvent { id = "nightclub_underage", place = "nightclub",
                condition = p => p.age < AdultAge, blocks = true,
                text = "클럽 입구에서 경호원이 팔을 뻗어 길을 막았다.\n\"신분증 좀 보여 주세요… 미안하지만 미성년자는 못 들어갑니다.\"\n돌아서는 길, 괜히 얼굴이 화끈거렸다." },
            new ActionEvent { id = "nightclub_paparazzi", place = "nightclub",
                condition = p => p.age < AdultAge,
                chance = p => Mathf.Lerp(PaparazziAtNoFame, PaparazziAtFullFame, p.fame / (float)PlayerProfile.FameMax),
                mental = PaparazziMental,
                text = "그때 골목 건너편에서 셔터 소리가 터졌다.\n다음 날 아침 기사 제목: '미성년 유망주, 밤의 클럽가 배회'.\n댓글창은 온통 비난이다. 마음이 무겁다.",
                missText = "주변을 살폈지만, 다행히 아무도 보지 못한 것 같다." },
        };

        static bool Applies(ActionEvent e, PlayerProfile p, string place, string action)
        {
            return e.place == place && (e.action == null || e.action == action) && e.condition(p);
        }

        /// <summary>Whether an event stops this action from taking place at all (always, when its condition holds).</summary>
        public static bool IsBlocked(PlayerProfile p, string place, string action)
        {
            foreach (ActionEvent e in Rows)
                if (e.blocks && Applies(e, p, place, action)) return true;
            return false;
        }

        /// <summary>
        /// The events of doing this action: null when the table has nothing for it (the usual
        /// case). <paramref name="roll"/> gives 0..1; an event with a chance happens when it comes
        /// up under it. Only reads the player - the caller applies <see cref="EventResult.mental"/>.
        /// </summary>
        public static EventResult Resolve(PlayerProfile p, string place, string action, Func<float> roll)
        {
            EventResult r = null;
            foreach (ActionEvent e in Rows)
            {
                if (!Applies(e, p, place, action)) continue;
                bool happens = e.chance == null || roll() < e.chance(p);
                string page = happens ? e.text : e.missText;
                if (page == null) continue;
                if (r == null)
                {
                    Place pl = PlaceCatalog.Get(place);
                    r = new EventResult { title = pl != null ? pl.name : place };
                }
                r.pages.Add(page);
                if (!happens) continue;
                r.fired.Add(e.id);
                r.blocked |= e.blocks;
                r.mental += e.mental;
            }
            return r;
        }
    }
}
