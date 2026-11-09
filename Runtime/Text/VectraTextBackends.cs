using System.Collections.Generic;
using FirstSymbol.Vectra.Svg;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra.TextEngines
{
    /// <summary>The result of choosing an engine for a document.</summary>
    public struct VectraTextChoice
    {
        public IVectraTextBackend backend;
        public VectraTextEngine requested;
        /// <summary>A named engine could not be used and another engine (or none) took its place.</summary>
        public bool fellBack;

        public VectraTextEngine Used => backend != null ? backend.Engine : VectraTextEngine.None;
    }

    /// <summary>
    /// The registry of text backends. Lettera and TextMesh Pro register from their own assemblies when the package or
    /// font is there; the legacy backend is built in.
    /// </summary>
    public static class VectraTextBackends
    {
        static readonly Dictionary<VectraTextEngine, IVectraTextBackend> s_Backends = new Dictionary<VectraTextEngine, IVectraTextBackend>();
        static readonly VectraTextEngine[] s_AutoOrder = { VectraTextEngine.Lettera, VectraTextEngine.TextMeshPro, VectraTextEngine.Legacy };
        static IVectraTextBackend s_BuiltInLegacy;

        static VectraTextBackends()
        {
            s_BuiltInLegacy = new VectraLegacyTextBackend();
            s_Backends[VectraTextEngine.Legacy] = s_BuiltInLegacy;
        }

        public static void Register(IVectraTextBackend backend)
        {
            if (backend == null || backend.Engine == VectraTextEngine.Auto || backend.Engine == VectraTextEngine.None) return;
            s_Backends[backend.Engine] = backend;
        }

        /// <summary>Removes a backend. The built-in legacy one comes back.</summary>
        public static void Unregister(VectraTextEngine engine)
        {
            if (engine == VectraTextEngine.Legacy) s_Backends[engine] = s_BuiltInLegacy;
            else s_Backends.Remove(engine);
        }

        public static bool IsRegistered(VectraTextEngine engine) => s_Backends.ContainsKey(engine);

        public static IVectraTextBackend Get(VectraTextEngine engine) => s_Backends.TryGetValue(engine, out var b) ? b : null;

        /// <summary>
        /// Picks the backend for <paramref name="requested"/>. Auto takes the first usable of Lettera, TextMesh Pro, Legacy.
        /// A named engine that is missing or unusable falls back along the same order; None draws nothing.
        /// With <paramref name="world"/> only engines that can draw without a Canvas count (the legacy one cannot).
        /// </summary>
        public static VectraTextChoice Resolve(VectraTextEngine requested, VectraFontMap map, Object tmpFont, Object letteraFont,
            bool world = false)
        {
            var choice = new VectraTextChoice { requested = requested };
            if (requested == VectraTextEngine.None) return choice;
            if (requested != VectraTextEngine.Auto && s_Backends.TryGetValue(requested, out var named) && (!world || named.SupportsWorld) &&
                named.IsUsable(map, FontFor(named, tmpFont, letteraFont)))
            {
                choice.backend = named;
                return choice;
            }

            foreach (var engine in s_AutoOrder)
            {
                if (engine == requested) continue;
                if (!s_Backends.TryGetValue(engine, out var b) || (world && !b.SupportsWorld) ||
                    !b.IsUsable(map, FontFor(b, tmpFont, letteraFont))) continue;
                choice.backend = b;
                choice.fellBack = requested != VectraTextEngine.Auto;
                return choice;
            }

            choice.fellBack = requested != VectraTextEngine.Auto;
            return choice;
        }

        /// <summary>The component font that belongs to a backend: the Lettera one for Lettera, the TextMesh Pro one otherwise.</summary>
        public static Object FontFor(IVectraTextBackend backend, Object tmpFont, Object letteraFont) =>
            backend != null && backend.Engine == VectraTextEngine.Lettera ? letteraFont : tmpFont;
    }
}
