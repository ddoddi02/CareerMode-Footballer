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

        /// <summary>Which way this body faces at kickoff - down the pitch, at the other goal.</summary>
        public static Quaternion Facing(bool away)
        {
            return Quaternion.Euler(0f, away ? 180f : 0f, 0f);
        }
    }
}
