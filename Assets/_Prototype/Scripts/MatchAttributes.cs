using UnityEngine;

namespace Prototype
{
    /// <summary>
    /// What a body brings from the career side that its component fields do not already hold
    /// (MatchRoster fills it from MatchHandoff). Every field starts at "not set", which is the
    /// drill as it was: a body nobody wrote to reads the old role table and the old constants.
    /// Not serialised, so the scene stays as it is.
    /// </summary>
    public class MatchAttributes
    {
        /// <summary>Shot power, 1..150 (Career.ShotPower). 0 = the old +-15% band around Base.Shot.</summary>
        public float shotPower;
        /// <summary>Finishing as 0..1. Negative = the slot's table (Formation.ShootingFor).</summary>
        public float finishing01 = -1f;
        /// <summary>Body strength as 0..1 for a bot carrier's contest. Negative = the even 0.5.</summary>
        public float strength01 = -1f;

        /// <summary>Ball control as 0..1: how close the ball sits to him on every touch. Negative = as the curve gives it.</summary>
        public float ballControl01 = -1f;
        /// <summary>Dribbling as 0..1: how much of his free speed he keeps with the ball. Negative = all of it.</summary>
        public float dribbling01 = -1f;
        /// <summary>Tricks as 0..1: beating a challenge on the ball. Negative = none (even).</summary>
        public float tricks01 = -1f;
        /// <summary>Mental strength as 0..1: how little his sight shrinks under pressure. Negative = it does not shrink.</summary>
        public float mental01 = -1f;
        /// <summary>Stamina as 0..1: how slowly he tires. Negative = an even 0.5.</summary>
        public float stamina01 = -1f;
        /// <summary>What is left in him, 0..1 (MatchStamina). Starts at the day's remaining energy.</summary>
        public float energy = 1f;
        /// <summary>The share of his aim left when he is tired, 1 = fresh (read through IForm.FormPercent).</summary>
        public float accuracy = 1f;

        /// <summary>How far in front the ball sits, as a multiple of the curve: 1.25 at ball control 1, 0.75 at 100.</summary>
        public float TouchScale { get { return ballControl01 < 0f ? 1f : Mathf.Lerp(1.25f, 0.75f, ballControl01); } }
        /// <summary>The share of his free speed he keeps with the ball: 0.88 at dribbling 1, all of it at 100.</summary>
        public float CarryKeep { get { return dribbling01 < 0f ? 1f : Mathf.Lerp(0.88f, 1f, dribbling01); } }
        /// <summary>-0.5..+0.5 around an even duel (Tackle.Input.trickBonus). 0 for a body with no sheet.</summary>
        public float TrickBonus { get { return tricks01 < 0f ? 0f : tricks01 - 0.5f; } }

        /// <summary>
        /// Speed, acceleration and agility reach the match as a swing around the number the match
        /// already runs on: stat 1 = -15 %, 100 = +15 %, 50 about as it was. A placeholder -
        /// the width is the user's to answer (PROJECT.md 3.28).
        /// </summary>
        public const float BodySwing = 0.30f;

        public static float BodyFactor(int stat)
        {
            return 1f + BodySwing * (Career.StatScale.To01(stat) - 0.5f);
        }

        /// <summary>The mean pace of his shot, m/s: his power if he has one, else the old band by finishing.</summary>
        public static float ShotPace(MatchAttributes a, float powerScale, float finishing01)
        {
            return a != null && a.shotPower > 0f
                ? Career.ShotPower.ProvisionalSpeed(a.shotPower * powerScale)
                : BallModel.Speed(BallModel.Base.Shot, finishing01);
        }

        /// <summary>The kick itself: through his power if he has one, else Base.Shot as before.</summary>
        public static BallModel.Strike Shoot(MatchAttributes a, float powerScale, UnityEngine.Vector3 from, UnityEngine.Vector3 aim,
                                             float finishing01, float difficulty)
        {
            return a != null && a.shotPower > 0f
                ? BallModel.Resolve(from, aim, 0f, ShotPace(a, powerScale, finishing01), finishing01, difficulty)
                : BallModel.Resolve(from, aim, BallModel.Base.Shot, 0f, finishing01, difficulty);
        }
    }
}
