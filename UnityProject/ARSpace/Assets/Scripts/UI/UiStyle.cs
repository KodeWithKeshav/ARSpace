using UnityEngine;
using UnityEngine.UI;

namespace ARSpace.UI
{
    /// <summary>Shared palette and helpers so every runtime-built and editor-built UI element looks identical.</summary>
    public static class UiStyle
    {
        /// <summary>Corner radius (in sprite pixels) baked into the generated rounded-rectangle sprite.</summary>
        public const float SpriteCornerPixels = 48f;

        public static readonly Color Primary = new Color(1.00f, 0.42f, 0.00f, 1f);
        public static readonly Color Panel = new Color(0.07f, 0.08f, 0.10f, 0.92f);
        public static readonly Color Card = new Color(0.16f, 0.17f, 0.20f, 1f);
        public static readonly Color Chip = new Color(0.21f, 0.22f, 0.26f, 1f);
        public static readonly Color Secondary = new Color(0.24f, 0.25f, 0.29f, 1f);
        public static readonly Color Danger = new Color(0.82f, 0.25f, 0.21f, 1f);
        public static readonly Color Tile = new Color(0.93f, 0.94f, 0.96f, 1f);
        public static readonly Color TextPrimary = Color.white;
        public static readonly Color TextMuted = new Color(0.72f, 0.75f, 0.80f, 1f);

        /// <summary>Applies the rounded sprite so the visible corner radius equals <paramref name="radius"/> canvas units.</summary>
        public static void Round(Image image, Sprite roundedSprite, float radius)
        {
            if (image == null || roundedSprite == null)
                return;

            image.sprite = roundedSprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = Mathf.Max(0.05f, SpriteCornerPixels / Mathf.Max(radius, 1f));
        }
    }
}
