namespace Career
{
    /// <summary>
    /// Every attribute is 1..100 (user, 2026-10-14). The match side works in 0..1, and the
    /// conversion is (v - 1) / 99 so that both ends land exactly: 1 is the floor (0) and 100
    /// is the ceiling (1) - "shot power 100 reaches top speed" needs 100 to be exactly 1.
    /// </summary>
    public static class StatScale
    {
        public const int Min = 1, Max = 100;

        public static float To01(float v)
        {
            float x = (v - Min) / (Max - Min);
            return x < 0f ? 0f : (x > 1f ? 1f : x);
        }

        public static float From01(float x)
        {
            x = x < 0f ? 0f : (x > 1f ? 1f : x);
            return Min + (Max - Min) * x;
        }

        public static float Clamp(float v)
        {
            return v < Min ? Min : (v > Max ? Max : v);
        }
    }
}
