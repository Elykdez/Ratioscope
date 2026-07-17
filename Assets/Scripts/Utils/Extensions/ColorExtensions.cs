using UnityEngine;

namespace Hypocycloid.Utils
{
    public static class ColorExtensions
    {
        public static Color32 WithAlpha(this Color32 rgba, byte newAlpha) => new(rgba.r, rgba.g, rgba.b, newAlpha);

        public static Color WithAlpha(this Color rgba, float newAlpha) => new(rgba.r, rgba.g, rgba.b, newAlpha);
    }
}
