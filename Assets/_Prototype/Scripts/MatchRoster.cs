using System.Collections.Generic;
using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// Puts the career side's players on the pitch (MatchHandoff.club / opponent): each sheet's
    /// stats go into the fields that already play them - passing, finishing, shot power,
    /// crossing, strength, tackling, keeping, the three body stats and form - and the club's
    /// chemistry into TeamChemistry. A slot with no sheet keeps what the drill gave it, and with
    /// no handoff at all nothing here runs (PROJECT.md 3.28). Read-only on MatchHandoff.
    /// </summary>
    public static class MatchRoster
    {
        /// <summary>True when there were sheets to apply.</summary>
        public static bool Apply(DrillDirector d)
        {
            if (!MatchHandoff.requested || d == null || d.possession == null) return false;
            bool any = false;

            if (MatchHandoff.club != null)
            {
                var home = Bodies(d, Side.Home);
                for (int i = 0; i < home.Count; i++) any |= Dress(d, home[i], Sheet(MatchHandoff.club, home[i]), true);
                ApplyChemistry(home);
            }
            if (MatchHandoff.opponent != null)
            {
                var away = Bodies(d, Side.Away);
                for (int i = 0; i < away.Count; i++) any |= Dress(d, away[i], Sheet(MatchHandoff.opponent, away[i]), false);
            }
            return any;
        }

        /// <summary>Everybody on a side: the team's roster, the human and the keeper, once each.</summary>
        public static List<Transform> Bodies(DrillDirector d, Side side)
        {
            var list = new List<Transform>();
            TeamAttack atk = side == Side.Home ? d.possession.homeAttack : d.possession.awayAttack;
            if (atk != null && atk.mates != null)
                for (int i = 0; i < atk.mates.Length; i++)
                    if (atk.mates[i] != null && !list.Contains(atk.mates[i])) list.Add(atk.mates[i]);
            if (side == Side.Home && d.player != null && !list.Contains(d.player.transform)) list.Add(d.player.transform);
            foreach (GoalkeeperAI k in Object.FindObjectsByType<GoalkeeperAI>(FindObjectsSortMode.None))
                if (k.MySide == side && !list.Contains(k.transform)) list.Add(k.transform);
            return list;
        }

        static string SlotOf(Transform t)
        {
            return t.GetComponent<GoalkeeperAI>() != null ? "GK" : Formation.RoleOf(t).ToString();
        }

        static PlayerSheet Sheet(PlayerSheet[] sheets, Transform t)
        {
            string slot = SlotOf(t);
            for (int i = 0; i < sheets.Length; i++)
                if (sheets[i] != null && sheets[i].slot == slot) return sheets[i];
            return null;
        }

        static bool Dress(DrillDirector d, Transform t, PlayerSheet s, bool human)
        {
            if (s == null) return false;
            var atk = t.GetComponent<AttackerAI>();
            var fc = t.GetComponent<FootballerController>();
            var def = t.GetComponent<DefenderAI>();
            var gk = t.GetComponent<GoalkeeperAI>();

            int shortP = s.Stat("shortPassing", 0), longP = s.Stat("longPassing", 0);
            if (atk != null) { if (shortP > 0) atk.shortPassing = shortP; if (longP > 0) atk.longPassing = longP; }
            if (fc != null) { if (shortP > 0) fc.shortPassing = shortP; if (longP > 0) fc.longPassing = longP; }

            int form = Mathf.Clamp(s.formPercent, Form.MinPercent, Form.MaxPercent);
            if (atk != null) atk.form = form;
            if (fc != null) fc.form = form;
            if (gk != null) gk.form = form;

            MatchAttributes attrs = atk != null ? atk.attrs : fc != null ? fc.attrs : null;
            int fin = s.Stat("finishing", 0), cross = s.Stat("crossing", 0), phys = s.Stat("physicality", 0);
            int power = s.Stat("shotPower", s.Stat("shotPowerBase", 0));
            if (attrs != null)
            {
                if (fin > 0) attrs.finishing01 = Career.StatScale.To01(fin);
                if (power > 0) attrs.shotPower = power;
                if (phys > 0) attrs.strength01 = Career.StatScale.To01(phys);
                attrs.ballControl01 = Pct(s, "ballControl");
                attrs.dribbling01 = Pct(s, "dribbling");
                attrs.tricks01 = Pct(s, "tricks");
                attrs.mental01 = Pct(s, "mentalStrength");
                attrs.stamina01 = Pct(s, "stamina");
                attrs.energy = Mathf.Clamp01(s.energyPercent / 100f);
            }
            if (fc != null)
            {
                if (fin > 0) d.playerShooting = Career.StatScale.To01(fin);
                if (cross > 0) d.playerCrossing = Career.StatScale.To01(cross);
                if (phys > 0) d.playerStrength = Career.StatScale.To01(phys);
            }

            int tackle = s.Stat("tackling", 0);
            if (def != null && tackle > 0) def.tackling01 = Career.StatScale.To01(tackle);

            if (gk != null)
            {
                // the 20 career stats have no keeper's; a sheet may carry them under these ids
                int keeping = s.Stat("keeping", 0), reflexes = s.Stat("reflexes", 0);
                if (keeping > 0) gk.keeping = keeping;
                if (reflexes > 0) gk.reflexes = reflexes;
            }

            // body: a swing around the number the match already runs on (MatchAttributes.BodySwing)
            int top = s.Stat("topSpeed", 0), acc = s.Stat("acceleration", 0), agi = s.Stat("agility", 0);
            float ft = top > 0 ? MatchAttributes.BodyFactor(top) : 1f;
            float fa = acc > 0 ? MatchAttributes.BodyFactor(acc) : 1f;
            float fg = agi > 0 ? MatchAttributes.BodyFactor(agi) : 1f;
            if (atk != null) { atk.speed *= ft; atk.sprintSpeed *= ft; Move(atk.motion, fa, fg); }
            if (def != null) { def.speed *= ft; def.sprintSpeed *= ft; def.jogSpeed *= ft; Move(def.motion, fa, fg); }
            if (gk != null) Move(gk.motion, fa, fg);
            if (fc != null) { fc.walkSpeed *= ft; fc.sprintSpeed *= ft; fc.accel *= fa; fc.bodyTurn *= fg; }
            return true;
        }

        static float Pct(PlayerSheet s, string id)
        {
            int v = s.Stat(id, 0);
            return v > 0 ? Career.StatScale.To01(v) : -1f;
        }

        static void Move(MotionModel m, float accel, float agility)
        {
            if (m == null) return;
            m.accel *= accel;
            m.turnAccel *= agility;
            m.bodyTurn *= agility;
        }

        /// <summary>The club's pairs, by slot, into TeamChemistry (the opponent keeps the baseline).</summary>
        static void ApplyChemistry(List<Transform> home)
        {
            if (MatchHandoff.chemistry == null || TeamChemistry.Active == null) return;
            for (int i = 0; i < home.Count; i++)
                for (int j = i + 1; j < home.Count; j++)
                    TeamChemistry.Active.Set(home[i], home[j], MatchHandoff.chemistry(SlotOf(home[i]), SlotOf(home[j])));
        }
    }
}
