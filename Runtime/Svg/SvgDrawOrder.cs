using System.Collections.Generic;

namespace FirstSymbol.Vectra.Svg
{
    /// <summary>One step of an SVG document in paint order: a run of path elements, a composited layer, or one text or image element.</summary>
    public struct SvgDrawItem
    {
        /// <summary>First element of the step.</summary>
        public int from;
        /// <summary>One past the last element of the step (a text or image is one element).</summary>
        public int to;
        /// <summary>The step is not mesh: a text, an image, or a composited texture (<see cref="composite"/>).</summary>
        public bool overlay;
        /// <summary>
        /// The step is drawn by <see cref="Rendering.SvgCompositor"/> into a texture (elements [from, to) with their layers) that is
        /// shown as a quad. <see cref="overlay"/> is set too, so code that only knows meshes, texts and images skips it.
        /// </summary>
        public bool composite;
    }

    /// <summary>
    /// Cuts a document into steps in paint order. Path elements before the first text or image form one mesh; each text
    /// or image is its own object; path elements after it form the next mesh, drawn above it. That keeps SVG's paint order
    /// when shapes follow text. A top-level layer (group opacity, mask, blend mode, background blur) is a composite step of its
    /// own; a layer that reads its backdrop also takes everything drawn since the last text or image into its texture, because
    /// the backdrop is the document's own content (the scene behind the component is not read).
    /// </summary>
    public static class SvgDrawOrder
    {
        static readonly List<int> s_Tops = new List<int>();

        /// <summary>Fills <paramref name="items"/> (cleared first) and returns a hash of its layout.</summary>
        public static int Split(VectraSvgDocument doc, bool includeText, bool includeImages, List<SvgDrawItem> items)
        {
            items.Clear();
            if (doc == null || doc.elements == null) return 17;
            int runStart = -1;
            var list = doc.elements;
            bool layered = SvgLayers.HasLayers(doc);
            if (layered) SvgLayers.Children(doc, -1, 0, list.Count, s_Tops);
            int nextTop = 0, segStart = 0, segItems = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (layered && nextTop < s_Tops.Count && doc.layers[s_Tops[nextTop]].from == i)
                {
                    var layer = doc.layers[s_Tops[nextTop++]];
                    if (layer.NeedsBackdrop)
                    {
                        items.RemoveRange(segItems, items.Count - segItems);
                        runStart = -1;
                        items.Add(new SvgDrawItem { from = segStart, to = layer.to, overlay = true, composite = true });
                    }
                    else
                    {
                        Close(items, ref runStart, i);
                        items.Add(new SvgDrawItem { from = i, to = layer.to, overlay = true, composite = true });
                    }

                    // Texts and images inside a layer are drawn above its texture, without the layer's opacity or mask.
                    bool inner = false;
                    for (int k = layer.from; k < layer.to; k++)
                    {
                        var t = list[k];
                        if (t == null || !(t.isText ? includeText : t.isImage && includeImages)) continue;
                        items.Add(new SvgDrawItem { from = k, to = k + 1, overlay = true });
                        inner = true;
                    }

                    i = layer.to - 1;
                    if (inner)
                    {
                        segStart = layer.to;
                        segItems = items.Count;
                    }

                    continue;
                }

                var e = list[i];
                if (e == null) continue;
                bool overlay = e.isText ? includeText : e.isImage && includeImages;
                if (overlay)
                {
                    Close(items, ref runStart, i);
                    items.Add(new SvgDrawItem { from = i, to = i + 1, overlay = true });
                    segStart = i + 1;
                    segItems = items.Count;
                    continue;
                }

                if (e.isText || e.isImage || e.path == null || e.path.IsEmpty) continue;
                if (runStart < 0) runStart = i;
            }

            Close(items, ref runStart, list.Count);
            int hash = 17;
            foreach (var it in items) hash = hash * 31 + it.from * 2 + it.to * 7 + (it.overlay ? 1 : 0) + (it.composite ? 5 : 0);
            return hash;
        }

        static void Close(List<SvgDrawItem> items, ref int runStart, int end)
        {
            if (runStart < 0) return;
            items.Add(new SvgDrawItem { from = runStart, to = end });
            runStart = -1;
        }

        /// <summary>The range of the first step when it is a mesh (what the component itself draws), else an empty range.</summary>
        public static void MainRange(List<SvgDrawItem> items, out int from, out int to)
        {
            if (items.Count > 0 && !items[0].overlay)
            {
                from = items[0].from;
                to = items[0].to;
            }
            else from = to = 0;
        }
    }
}
