using System;
using UnityEngine;

namespace FirstSymbol.Vectra.Rendering
{
    /// <summary>
    /// How many screen pixels an object covers, for the pictures SVG components draw (composited layers and, in Linear projects, whole
    /// documents): a picture must have the resolution of what is on screen, not of the Canvas scale factor alone. The cameras that show
    /// the object count (a Screen Space - Camera or World Space canvas through its camera) and so do extra cameras the editor adds, the
    /// Scene views, which magnify canvases independently of the Game view.
    /// </summary>
    public static class VectraScreenDensity
    {
        /// <summary>Set by the editor: how many extra cameras (Scene views) show the scene, and the camera at an index. Null in players.</summary>
        public static Func<int> ExtraCameraCount;
        public static Func<int, Camera> ExtraCamera;

        /// <summary>Size of a screen pixel in the local units of <paramref name="t"/> as <paramref name="cam"/> shows it; infinity when it does not show it.</summary>
        public static float LocalPerPixel(Transform t, Camera cam)
        {
            if (t == null || cam == null) return float.PositiveInfinity;
            var p0 = t.position;
            var s0 = cam.WorldToScreenPoint(p0);
            if (s0.z <= cam.nearClipPlane) return float.PositiveInfinity;
            var s1 = cam.WorldToScreenPoint(t.TransformPoint(Vector3.right));
            var s2 = cam.WorldToScreenPoint(t.TransformPoint(Vector3.up));
            float dx = Mathf.Sqrt((s1.x - s0.x) * (s1.x - s0.x) + (s1.y - s0.y) * (s1.y - s0.y));
            float dy = Mathf.Sqrt((s2.x - s0.x) * (s2.x - s0.x) + (s2.y - s0.y) * (s2.y - s0.y));
            float d = Mathf.Max(dx, dy);
            return d > 1e-4f ? 1f / d : float.PositiveInfinity;
        }

        /// <summary>The smaller of <paramref name="current"/> and the pixel size the extra (editor) cameras give <paramref name="t"/>.</summary>
        public static float WithExtraCameras(Transform t, float current)
        {
            var count = ExtraCameraCount;
            var get = ExtraCamera;
            if (count == null || get == null) return current;
            int n = count();
            for (int i = 0; i < n; i++)
            {
                float s = LocalPerPixel(t, get(i));
                if (s < current) current = s;
            }

            return current;
        }
    }
}
