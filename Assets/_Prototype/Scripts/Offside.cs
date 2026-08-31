using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// Law 11, reduced to the one number an attacking bot actually needs: the furthest
    /// line he is allowed to stand on.
    ///
    /// The line is the SECOND-last defender, not the last one - the keeper is normally
    /// the last, so in practice it is the deepest outfielder. Two things also un-offside
    /// you and both are max()'d in: you are never offside behind the ball, and you are
    /// never offside in your own half.
    ///
    /// What is deliberately NOT modelled: interfering with play, the moment-of-the-pass
    /// snapshot, and deflections. Bots use this to decide where to stand, and standing
    /// a stride onside is the whole skill being modelled. The flag itself can come later
    /// when there is a referee to raise it.
    /// </summary>
    public static class Offside
    {
        /// <summary>
        /// The furthest z an attacker of this side may occupy. Everything is expressed in
        /// the attacking direction, so callers never juggle signs.
        /// </summary>
        public static float Line(bool attacksPositiveZ, Transform[] defenders, float ballZ)
        {
            float dir = attacksPositiveZ ? 1f : -1f;

            // Deepest and second-deepest defender, measured toward the goal being attacked.
            float first = float.NegativeInfinity;
            float second = float.NegativeInfinity;
            int counted = 0;

            if (defenders != null)
            {
                for (int i = 0; i < defenders.Length; i++)
                {
                    if (defenders[i] == null) continue;
                    float z = defenders[i].position.z * dir;
                    counted++;
                    if (z > first) { second = first; first = z; }
                    else if (z > second) { second = z; }
                }
            }

            // Fewer than two defenders left goal-side: the halfway line and the ball are
            // all that is left holding him.
            float line = counted >= 2 ? second : float.NegativeInfinity;

            line = Mathf.Max(line, ballZ * dir);   // never offside behind the ball
            line = Mathf.Max(line, 0f);            // never offside in your own half

            return line * dir;
        }

        /// <summary>Is this position past the line - i.e. offside if the ball were played now?</summary>
        public static bool Beyond(Vector3 p, float lineZ, bool attacksPositiveZ)
        {
            return attacksPositiveZ ? p.z > lineZ : p.z < lineZ;
        }

        /// <summary>
        /// Pulls a target back onto the right side of the line, leaving a stride of margin.
        /// This is what makes a run "timed" rather than a flag.
        /// </summary>
        public static Vector3 KeepOnside(Vector3 target, float lineZ, bool attacksPositiveZ, float margin)
        {
            float limit = attacksPositiveZ ? lineZ - margin : lineZ + margin;
            if (attacksPositiveZ) target.z = Mathf.Min(target.z, limit);
            else target.z = Mathf.Max(target.z, limit);
            return target;
        }

        /// <summary>How far onside he is. Negative means he is already past the line.</summary>
        public static float Slack(Vector3 p, float lineZ, bool attacksPositiveZ)
        {
            return attacksPositiveZ ? lineZ - p.z : p.z - lineZ;
        }
    }
}
