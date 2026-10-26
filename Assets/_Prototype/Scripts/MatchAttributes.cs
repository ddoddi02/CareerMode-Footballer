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
