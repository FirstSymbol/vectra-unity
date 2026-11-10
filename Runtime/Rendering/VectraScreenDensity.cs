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

        /// <summary>True when something other than the main view can show objects (the editor sets the hooks): lets components skip polling in players.</summary>
        public static bool HasExtraCameras => ExtraCameraCount != null && ExtraCamera != null;

        /// <summary>
        /// The densest-view rule for meshes: the pixel size curves must be flattened for when an extra view is denser than
        /// <paramref name="current"/> (the main view), 0 when none is.
        /// </summary>
        public static float DetailSize(Transform t, float current)
        {
            float d = WithExtraCameras(t, current);
            return d < current ? d : 0f;
        }

        /// <summary>
        /// Size of a screen pixel in the local units of a canvas object, taking the densest view: the camera of a Screen Space - Camera
        /// canvas (which includes zoom) and the extra (editor) cameras. <paramref name="px"/> is the plain value of the main view.
        /// </summary>
        public static float CanvasDensest(Transform t, Canvas canvas, float px)
        {
            if (canvas != null)
            {
                var root = canvas.rootCanvas;
                if (root.renderMode == RenderMode.ScreenSpaceCamera && root.worldCamera != null)
                {
                    float seen = LocalPerPixel(t, root.worldCamera);
                    if (seen < float.PositiveInfinity) px = seen;
                }
            }

            return WithExtraCameras(t, px);
        }

        /// <summary>True when views other than the one that gave the plain pixel size can show a canvas object (an extra camera or a Screen Space - Camera canvas).</summary>
        public static bool CanvasFollowsViews(Canvas canvas)
        {
            if (HasExtraCameras) return true;
            return canvas != null && canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.rootCanvas.worldCamera != null;
        }

        /// <summary>
        /// Debounce shared by every object that rebuilds when the density on screen changes: a step over 25 % rebuilds at once, a smaller
        /// change over 3 % at most ten times a second. <paramref name="nextCheck"/> is the object's own clock.
        /// </summary>
        public static bool Drifted(float now, float built, ref float nextCheck, float time)
        {
            if (!(built > 0f) || !(now > 0f) || float.IsInfinity(now)) return false;
            float ratio = now / built;
            if (ratio > 0.97f && ratio < 1.03f) return false;
            bool step = ratio > 1.25f || ratio < 0.8f;
            if (!step && time < nextCheck) return false;
            nextCheck = time + 0.1f;
            return true;
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
