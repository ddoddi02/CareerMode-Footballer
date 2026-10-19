using System;
using System.Collections.Generic;
using UnityEngine;

namespace Career
{
    /// <summary>How well two people in the squad get on, 0..100. One value a pair, the same both ways.</summary>
    [Serializable]
    public class Bond
    {
        public string a, b;     // member ids, a < b (ordinal)
        public float value = Chemistry.Neutral;
        /// <summary>How much daily life has moved this pair today. Reset by <see cref="Chemistry.NewDay"/>.</summary>
        public float today;
    }

    /// <summary>
    /// Chemistry between squad members (user, 2026-10-20): every pair, the player's with each
    /// teammate and the teammates' among themselves. Daily life fills it; the match reads it
    /// (passes, dribbling, answering a call for the ball - the attack session's side).
    ///
    /// Closeness and liking are one number for now, and the same both ways. A pair nobody has
    /// touched is <see cref="Neutral"/> and is not stored. Every number here is PROVISIONAL
    /// (PROJECT.md 10.9).
    /// </summary>
    [Serializable]
    public class Chemistry
    {
        public const float Min = 0f, Max = 100f, Neutral = 50f;
        /// <summary>Most daily life can move one pair in a day, either way: 50 to 100 takes ten good days at the least.</summary>
        public const float MaxDailyChange = 5f;

        public List<Bond> bonds = new List<Bond>();

        public float Get(string x, string y)
        {
            Bond b = Find(x, y, false);
            return b != null ? b.value : Neutral;
        }

        /// <summary>
        /// What daily actions call: moves the pair by up to <paramref name="delta"/>, within what
        /// is left of today's <see cref="MaxDailyChange"/>. Returns how much it really moved.
        /// </summary>
        public float Change(string x, string y, float delta)
        {
            Bond b = Find(x, y, true);
            float room = delta > 0f ? MaxDailyChange - b.today : -MaxDailyChange - b.today;
            float d = delta > 0f ? Mathf.Min(delta, Mathf.Max(0f, room)) : Mathf.Max(delta, Mathf.Min(0f, room));
            float before = b.value;
            b.value = Mathf.Clamp(b.value + d, Min, Max);
            b.today += b.value - before;
            return b.value - before;
        }

        /// <summary>Sets a pair outright, past the daily limit - for events that change everything at once.</summary>
        public void Set(string x, string y, float value)
        {
            Find(x, y, true).value = Mathf.Clamp(value, Min, Max);
        }

        /// <summary>A new day: every pair can move <see cref="MaxDailyChange"/> again.</summary>
        public void NewDay()
        {
            for (int i = 0; i < bonds.Count; i++) bonds[i].today = 0f;
        }

        /// <summary>Team chemistry: the mean over every pair of these members.</summary>
        public float Average(IList<string> ids)
        {
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < ids.Count; i++)
                for (int j = i + 1; j < ids.Count; j++) { sum += Get(ids[i], ids[j]); n++; }
            return n > 0 ? sum / n : Neutral;
        }

        /// <summary>One member's mean with each of the others.</summary>
        public float AverageWith(string id, IList<string> ids)
        {
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == id) continue;
                sum += Get(id, ids[i]);
                n++;
            }
            return n > 0 ? sum / n : Neutral;
        }

        Bond Find(string x, string y, bool add)
        {
            if (string.IsNullOrEmpty(x) || string.IsNullOrEmpty(y) || x == y)
                throw new ArgumentException("A bond needs two different members: " + x + ", " + y);
            if (string.CompareOrdinal(x, y) > 0) { string t = x; x = y; y = t; }
            for (int i = 0; i < bonds.Count; i++)
                if (bonds[i].a == x && bonds[i].b == y) return bonds[i];
            if (!add) return null;
            Bond b = new Bond { a = x, b = y };
            bonds.Add(b);
            return b;
        }
    }
}
