using UnityEngine;

namespace RimSynapse.Psychology.API
{
    /// <summary>Which region of the compass a relationship sits in (#72 / #24).</summary>
    public enum CompassQuadrant { Neutral, Friends, FondButWary, Allies, Enemies }

    /// <summary>
    /// Pure geometry + colouring for the relationship compass view: it plots each known colonist by WARMTH
    /// (liking, the x axis) against TRUST (reliance, the y axis), each on [-100, 100] with the origin at centre.
    /// Kept free of any drawing so the mapping and quadrant logic are unit-testable; the dialog does the paint.
    /// </summary>
    public static class SynapseRelationshipCompass
    {
        /// <summary>Below this magnitude on both axes a relationship reads as "neutral" (no strong feeling yet).</summary>
        public const float NeutralBand = 8f;

        /// <summary>Map (warmth, trust) onto a point inside <paramref name="plot"/> — warmth → x (right is warmer),
        /// trust → y (UP is more trust, i.e. smaller screen-y). Values are clamped to ±100.</summary>
        public static Vector2 PlotPoint(float warmth, float trust, Rect plot)
        {
            float w = Mathf.Clamp(warmth, -100f, 100f) / 100f;
            float t = Mathf.Clamp(trust, -100f, 100f) / 100f;
            float x = plot.center.x + w * (plot.width * 0.5f);
            float y = plot.center.y - t * (plot.height * 0.5f);
            return new Vector2(x, y);
        }

        /// <summary>Classify a relationship: Friends (warm + trusted), Fond-but-wary (warm, not trusted),
        /// Allies (trusted, not warm), Enemies (neither), or Neutral within the dead-band.</summary>
        public static CompassQuadrant Quadrant(float warmth, float trust)
        {
            if (Mathf.Abs(warmth) < NeutralBand && Mathf.Abs(trust) < NeutralBand) return CompassQuadrant.Neutral;
            bool warm = warmth >= 0f, trusting = trust >= 0f;
            if (warm && trusting) return CompassQuadrant.Friends;
            if (warm) return CompassQuadrant.FondButWary;
            if (trusting) return CompassQuadrant.Allies;
            return CompassQuadrant.Enemies;
        }

        /// <summary>The dot colour for a quadrant — green friends, amber fond-but-wary, teal allies, red enemies.</summary>
        public static Color QuadrantColor(CompassQuadrant q)
        {
            switch (q)
            {
                case CompassQuadrant.Friends:     return new Color(0.42f, 0.78f, 0.46f);
                case CompassQuadrant.FondButWary:  return new Color(0.86f, 0.70f, 0.30f);
                case CompassQuadrant.Allies:       return new Color(0.38f, 0.68f, 0.80f);
                case CompassQuadrant.Enemies:      return new Color(0.83f, 0.38f, 0.38f);
                default:                           return new Color(0.62f, 0.64f, 0.68f);
            }
        }

        /// <summary>The faint quadrant-wash colour (for tinting the four corners of the plot).</summary>
        public static Color QuadrantWash(CompassQuadrant q)
        {
            Color c = QuadrantColor(q);
            return new Color(c.r, c.g, c.b, 0.07f);
        }
    }
}
