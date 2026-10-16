using System;
using UnityEngine;

namespace Career
{
    /// <summary>
    /// What the player looks like. Placeholder art (DECISIONS.md 2026-10-14): colours and a few
    /// shapes picked from short lists. Height and weight are not here - they change the body
    /// model as well (PlayerProfile, CreationRules).
    /// </summary>
    [Serializable]
    public struct Appearance
    {
        public int build;       // CreationRules.Presets index last picked, -1 once the sliders moved
        public int skin;
        public int hairColor;
        public int hairStyle;
        public int kitColor;

        public static Appearance Default
        {
            get { return new Appearance { build = 1, skin = 2, hairColor = 0, hairStyle = 1, kitColor = 0 }; }
        }

        public static readonly Color[] Skins =
        {
            new Color(0.98f, 0.87f, 0.77f), new Color(0.93f, 0.76f, 0.62f), new Color(0.80f, 0.60f, 0.45f),
            new Color(0.62f, 0.44f, 0.31f), new Color(0.42f, 0.29f, 0.20f), new Color(0.28f, 0.19f, 0.13f),
        };

        public static readonly Color[] HairColors =
        {
            new Color(0.10f, 0.08f, 0.07f), new Color(0.35f, 0.22f, 0.12f), new Color(0.62f, 0.42f, 0.20f),
            new Color(0.86f, 0.74f, 0.48f), new Color(0.60f, 0.20f, 0.10f), new Color(0.75f, 0.75f, 0.78f),
        };

        public static readonly string[] HairStyles = { "삭발", "짧은 머리", "중간 머리", "긴 머리" };

        public static readonly Color[] KitColors =
        {
            new Color(0.25f, 0.55f, 0.92f), new Color(0.86f, 0.22f, 0.22f), new Color(0.95f, 0.95f, 0.95f),
            new Color(0.12f, 0.12f, 0.14f), new Color(0.95f, 0.82f, 0.20f), new Color(0.20f, 0.65f, 0.35f),
        };

        public static Color Pick(Color[] palette, int i)
        {
            return palette[Mathf.Clamp(i, 0, palette.Length - 1)];
        }
    }
}
