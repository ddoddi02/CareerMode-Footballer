using UnityEngine;

namespace Prototype
{
    /// <summary>Which slot in the shape a body is standing in. No behaviour attached yet.</summary>
    public enum Role
    {
        GK,
        LB, LCB, RCB, RB,
        DM,                 // 원 볼란치 - the single pivot, and the slot the human plays
        LCM, RCM,
        LW, ST, RW
    }

    public struct FormationSlot
    {
        public Role role;
        public string label;

        /// <summary>Metres from the pivot. x = across the pitch, y = up the pitch.</summary>
        public Vector2 offset;

        public FormationSlot(Role r, string l, float x, float z)
        {
            role = r;
            label = l;
            offset = new Vector2(x, z);
        }
    }

    /// <summary>
    /// 4-1-2-3, as pure data. Positions only - no AI, no movement, no marking.
    ///
    /// Offsets are measured FROM THE PIVOT rather than from the halfway line, because
    /// the pivot is the slot the human occupies and the drill starts him at the origin.
    /// Anchoring the shape to him means the drill's geometry survives untouched while
    /// the other ten fall in around it.
    ///
    /// The shape is 39 m deep from the centre-backs to the striker, which is about
    /// where a real side sits when it is in possession and compact. The keeper is the
    /// exception: he is placed off his own goal line, not off the block, because that
    /// is what he actually does.
    ///
    /// Later this should be expressed in the 5-lane x 6-band tactical grid (PROJECT.md
    /// §3.12) so AI can query slots instead of reading metres. Metres are fine while
    /// nothing queries them.
    /// </summary>
    public static class Formation
    {
        /// <summary>How far the keeper stands off his own goal line, in metres.</summary>
        public const float KeeperDepth = 5f;

        /// <summary>The home pivot sits on the origin - the drill's start position.</summary>
        public const float HomeAnchorZ = 0f;

        /// <summary>
        /// The away pivot. Chosen so their striker ends up about 5 m off the home pivot -
        /// the "closed down as the single six" picture the drill is about, and the same
        /// gap DrillDirector warps its defender to at the start of every round.
        /// </summary>
        public const float AwayAnchorZ = 30f;

        /// <summary>
        /// Depth of the penalty area. Outfield slots are kept out of their own box: a
        /// side pinned this deep flattens its back four onto the edge of the area rather
        /// than setting up inside it, and without the clamp the away centre-backs end up
        /// standing on their own keeper.
        /// </summary>
        public const float PenaltyDepth = 16.5f;

        public static readonly FormationSlot[] F4123 =
        {
            new FormationSlot(Role.GK,  "GK",    0.0f,   0.0f),   // offset unused, see WorldPos
            new FormationSlot(Role.LB,  "LB",  -22.0f, -12.0f),
            new FormationSlot(Role.LCB, "LCB",  -7.5f, -14.0f),
            new FormationSlot(Role.RCB, "RCB",   7.5f, -14.0f),
            new FormationSlot(Role.RB,  "RB",   22.0f, -12.0f),
            new FormationSlot(Role.DM,  "DM",    0.0f,   0.0f),
            new FormationSlot(Role.LCM, "LCM", -10.0f,  10.0f),
            new FormationSlot(Role.RCM, "RCM",  10.0f,  10.0f),
            new FormationSlot(Role.LW,  "LW",  -23.0f,  22.0f),
            new FormationSlot(Role.ST,  "ST",    0.0f,  25.0f),
            new FormationSlot(Role.RW,  "RW",   23.0f,  22.0f),
        };

        /// <summary>
        /// Where this slot stands. Home attacks +Z; away is the same shape mirrored, so
        /// its left back is on the opposite touchline and still on his own left.
        /// </summary>
        public static Vector3 WorldPos(FormationSlot s, bool away, float pitchHalfZ)
        {
            if (s.role == Role.GK)
            {
                // Off his own goal line, which for the home side is at -Z.
                float gz = away ? pitchHalfZ - KeeperDepth : -pitchHalfZ + KeeperDepth;
                return new Vector3(0f, 0f, gz);
            }

            float anchor = away ? AwayAnchorZ : HomeAnchorZ;
            float x = away ? -s.offset.x : s.offset.x;
            float z = away ? anchor - s.offset.y : anchor + s.offset.y;

            float deepest = pitchHalfZ - PenaltyDepth;
            z = Mathf.Clamp(z, -deepest, deepest);
            return new Vector3(x, 0f, z);
        }

        /// <summary>
        /// The players principle 2 is written about: wingers, the striker, the two
        /// eights, and full-backs who push on. Centre-backs and the pivot hold their
        /// slot instead of hunting for space in it.
        /// </summary>
        public static bool IsFrontLine(Role r)
        {
            return r == Role.LW || r == Role.ST || r == Role.RW
                || r == Role.LCM || r == Role.RCM
                || r == Role.LB || r == Role.RB;
        }

        /// <summary>Who actually runs in behind. A full-back overlaps; he does not lead the line.</summary>
        public static bool RunsInBehind(Role r)
        {
            return r == Role.LW || r == Role.ST || r == Role.RW;
        }

        /// <summary>How far off his slot a player is allowed to roam looking for space.</summary>
        public static float ZoneRadius(Role r)
        {
            if (r == Role.ST || r == Role.LW || r == Role.RW) return 11f;
            if (r == Role.LCM || r == Role.RCM) return 9f;
            if (r == Role.LB || r == Role.RB) return 8f;
            if (r == Role.DM) return 6f;
            return 5f;      // centre-backs stay where they are
        }

        /// <summary>
        /// Which band of the shape a role belongs to: 0 back, 1 midfield, 2 front.
        /// Spacing is judged along a line, not against whoever happens to be nearest -
        /// a centre-back's neighbour is the other centre-back, never the pivot standing
        /// in front of him.
        /// </summary>
        public static int LineOf(Role r)
        {
            switch (r)
            {
                case Role.GK: return -1;
                case Role.LB: case Role.LCB: case Role.RCB: case Role.RB: return 0;
                case Role.DM: case Role.LCM: case Role.RCM: return 1;
                default: return 2;
            }
        }

        public const int LineCount = 3;

        /// <summary>
        /// How well this slot passes, 0..1.
        ///
        /// Data rather than something the scene builder invents, for the same reason the
        /// slot coordinates are: it is a property of the ROLE, and the pass selector now
        /// asks "is there a better passer than me to give this to" (PassRules.wQuality).
        /// With one shared number for the whole side that question has no answer and the
        /// term is dead weight.
        ///
        /// The eights are the highest because that is who a side plays through. Real
        /// per-player attributes replace this the moment there is a squad to load.
        /// </summary>
        public static float PassingFor(Role r)
        {
            switch (r)
            {
                case Role.GK: return 0.45f;
                case Role.LCB: case Role.RCB: return 0.56f;
                case Role.LB: case Role.RB: return 0.60f;
                case Role.DM: return 0.72f;
                case Role.LCM: case Role.RCM: return 0.80f;
                case Role.LW: case Role.RW: return 0.66f;
                default: return 0.62f;      // ST
            }
        }

        // ------------------------------------------------------- role groups ----
        // The defensive duties are written per role ("full-backs pick up wingers"),
        // so the roles have to be askable as GROUPS rather than one at a time. Kept
        // here beside the shape because that is what they are: a property of the slot,
        // not of the man standing in it.

        public static bool IsKeeper(Role r) { return r == Role.GK; }
        public static bool IsCentreBack(Role r) { return r == Role.LCB || r == Role.RCB; }
        public static bool IsFullBack(Role r) { return r == Role.LB || r == Role.RB; }
        public static bool IsMidfield(Role r) { return r == Role.DM || r == Role.LCM || r == Role.RCM; }

        /// <summary>The single pivot. He marks last, so he is the one left screening.</summary>
        public static bool IsPivot(Role r) { return r == Role.DM; }
        public static bool IsWinger(Role r) { return r == Role.LW || r == Role.RW; }
        public static bool IsStriker(Role r) { return r == Role.ST; }

        /// <summary>The men a centre-back is watching: the front three.</summary>
        public static bool IsForward(Role r) { return r == Role.LW || r == Role.ST || r == Role.RW; }

        /// <summary>Who a striker hunts when he presses: the men who play it out.</summary>
        public static bool IsBuildUp(Role r)
        {
            return r == Role.GK || IsCentreBack(r) || IsFullBack(r);
        }

        /// <summary>
        /// How far from his station an opponent has to be before he counts as being IN
        /// this man's area - the trigger for common principle 3.
        ///
        /// Wider out wide, because a full-back covers a channel rather than a point, and
        /// tightest through the middle where the goal is.
        /// </summary>
        public static float DefensiveZone(Role r)
        {
            if (IsCentreBack(r)) return 8f;
            if (IsFullBack(r)) return 11f;
            if (IsMidfield(r)) return 10f;
            if (IsWinger(r)) return 12f;
            if (IsStriker(r)) return 13f;
            return 6f;      // keeper
        }

        /// <summary>
        /// How far from his slot a defender may be dragged by anything at all - marking,
        /// covering, pressing, delaying a break.
        ///
        /// This is the TOP principle, not a courtesy: whatever the rest of the defending
        /// decides, the answer is clamped back inside this radius before it becomes an
        /// order. A side that will follow its man anywhere does not have a shape, it has
        /// eleven separate chases, and the space it leaves behind is worth more than
        /// every duel it wins.
        ///
        /// Tightest at the back and loosest at the front, because that is where the cost
        /// of being out of position is paid. A striker twelve metres out of his slot has
        /// made a bad press; a centre-back twelve metres out of his has opened the goal.
        /// </summary>
        public static float PositionLeash(Role r)
        {
            if (IsKeeper(r)) return 6f;
            if (IsCentreBack(r)) return 9f;
            if (IsFullBack(r)) return 12f;
            if (IsMidfield(r)) return 13f;
            if (IsWinger(r)) return 15f;
            return 16f;      // striker
        }

        /// <summary>
        /// Whatever role this body is playing, whichever component happens to own it.
        /// Looked up once per picture rather than per frame.
        /// </summary>
        /// <summary>
        /// What role this body plays, read off whichever brain it carries.
        ///
        /// The fallback is GK, and it is not arbitrary. Nothing in this scene is without
        /// a brain except the goalkeeper - he has not got one yet - so an unknown body IS
        /// one in practice. It also fails in the safe direction: a keeper is excluded
        /// from marking (IsMidfield, IsWinger and the rest are all false for him) and
        /// included in build-up, which is what a keeper actually is.
        ///
        /// It used to fall back to DM, and that quietly made every keeper a defensive
        /// midfielder. Nobody noticed while duties were range-gated, because he was
        /// always too far away to be picked; the moment they were not, a midfielder was
        /// assigned to mark the opposition goalkeeper.
        /// </summary>
        public static Role RoleOf(Transform t)
        {
            if (t == null) return Role.GK;
            AttackerAI a = t.GetComponent<AttackerAI>();
            if (a != null) return a.role;
            DefenderAI d = t.GetComponent<DefenderAI>();
            if (d != null) return d.role;
            FootballerController f = t.GetComponent<FootballerController>();
            if (f != null) return f.role;
            return Role.GK;
        }

        /// <summary>Which way this body faces at kickoff - down the pitch, at the other goal.</summary>
        public static Quaternion Facing(bool away)
        {
            return Quaternion.Euler(0f, away ? 180f : 0f, 0f);
        }
    }
}
