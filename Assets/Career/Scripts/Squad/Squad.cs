using System;
using System.Collections.Generic;
using Prototype;

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
