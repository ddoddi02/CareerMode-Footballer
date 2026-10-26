using System;
using System.Collections.Generic;
using Prototype;
using UnityEngine;

namespace Career
{
    /// <summary>One person in the squad. <see cref="slot"/> is a label of the match's 4-1-2-3 (<c>Formation.F4123</c>).</summary>
    [Serializable]
    public class SquadMember
    {
        public string id;       // what Chemistry keys on
        public string name;
        public string slot;     // GK, LB, LCB, RCB, RB, DM, LCM, RCM, LW, ST, RW
        public bool isPlayer;
        /// <summary>The 1..100 stats by <c>CreationRules.Stats</c> id (not saved: made from the club's level, or read off the profile for the human).</summary>
        [NonSerialized] public Dictionary<string, int> stats;
    }

    /// <summary>
    /// The squad and the chemistry between its members. There is no roster yet, so
    /// <see cref="Provisional"/> fills the match's eleven slots: the player in DM (the slot the
    /// human plays) and ten teammates named after their slots. The match finds a member by
    /// slot (<see cref="AtSlot"/>) and reads <see cref="chemistry"/> by id.
    /// </summary>
    [Serializable]
    public class Squad
    {
        public const string PlayerId = "player";
        public const string PlayerSlot = "DM";

        public List<SquadMember> members = new List<SquadMember>();
        public Chemistry chemistry = new Chemistry();

        public static Squad Provisional(string playerName)
        {
            Squad s = new Squad();
            foreach (FormationSlot f in Formation.F4123)
            {
                bool player = f.label == PlayerSlot;
                s.members.Add(new SquadMember
                {
                    id = player ? PlayerId : f.label,
                    name = player ? playerName : f.label,
                    slot = f.label,
                    isPlayer = player,
                });
            }
            return s;
        }

        /// <summary>
        /// A club's eleven, from its level (user, 2026-10-26): each slot's stats are the level plus
        /// what the position needs (<see cref="SquadData"/>). When <paramref name="player"/> is given
        /// he is the one in the DM slot with his own stats; otherwise all eleven are made.
        /// <paramref name="seedKey"/> keeps the same club the same squad every run.
        /// </summary>
        public static Squad ForClub(string playerName, float level, PlayerProfile player, string seedKey)
        {
            Squad s = Provisional(playerName);
            if (player == null)
                foreach (SquadMember m in s.members) { m.isPlayer = false; m.id = m.slot; m.name = m.slot; }
            var rng = new System.Random(SquadData.StableHash(seedKey));
            foreach (SquadMember m in s.members)
                m.stats = m.isPlayer ? SquadData.PlayerStats(player) : SquadData.Generate(m.slot, level, rng);
            return s;
        }

        /// <summary>The human's stats read again off the profile (they change with training, body and perks).</summary>
        public void RefreshPlayer(PlayerProfile p)
        {
            SquadMember m = Player;
            if (m != null) m.stats = SquadData.PlayerStats(p);
        }

        /// <summary>The squad's average overall: the mean of its members' stat means. Half of a club's strength with its chemistry (user).</summary>
        public float SquadOverall
        {
            get
            {
                float sum = 0f;
                int n = 0;
                foreach (SquadMember m in members)
                    if (m.stats != null) { sum += SquadData.Overall(m.stats); n++; }
                return n == 0 ? 0f : sum / n;
            }
        }

        /// <summary>The id of the member in this slot (or the id itself, if one is given): what <see cref="chemistry"/> is keyed on.</summary>
        public string IdOf(string slotOrId)
        {
            SquadMember m = members.Find(x => x.slot == slotOrId) ?? members.Find(x => x.id == slotOrId);
            return m != null ? m.id : slotOrId;
        }

        /// <summary>The chemistry between the members in two slots, 0..100 as a whole number - what the match asks for per pair (PROJECT.md 10.15).</summary>
        public int ChemistryBetween(string slotA, string slotB)
        {
            string a = IdOf(slotA), b = IdOf(slotB);
            return a == b ? 0 : Mathf.RoundToInt(chemistry.Get(a, b));
        }

        /// <summary>A member's personal form for the match as a whole percent: the human's computed form (<see cref="FormModel.Personal"/>), 100 for the rest until they get one.</summary>
        public static int FormPercentOf(SquadMember m, PlayerProfile player)
        {
            return m.isPlayer && player != null ? Mathf.RoundToInt(player.personalForm * 100f) : 100;
        }

        public SquadMember Player { get { return members.Find(m => m.isPlayer); } }

        public SquadMember AtSlot(string slot) { return members.Find(m => m.slot == slot); }

        public List<string> Ids()
        {
            List<string> ids = new List<string>(members.Count);
            for (int i = 0; i < members.Count; i++) ids.Add(members[i].id);
            return ids;
        }

        /// <summary>Team chemistry: the mean over every pair in the squad, the player's included.</summary>
        public float TeamChemistry { get { return chemistry.Average(Ids()); } }

        /// <summary>The player's mean with his teammates.</summary>
        public float PlayerChemistry { get { return chemistry.AverageWith(PlayerId, Ids()); } }

        /// <summary>Team form from team chemistry, added to 1 in a match (<see cref="FormModel.TeamForm"/>).</summary>
        public float TeamForm { get { return FormModel.TeamForm(TeamChemistry); } }

        /// <summary>The end of a day: every pair's daily limit starts again. The daily system calls this once a day.</summary>
        public void EndDay()
        {
            chemistry.NewDay();
        }
    }
}
