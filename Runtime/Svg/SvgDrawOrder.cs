using System.Collections.Generic;

namespace FirstSymbol.Vectra.Svg
{
    /// <summary>One step of an SVG document in paint order: a run of path elements, or one text or image element.</summary>
    public struct SvgDrawItem
    {
        /// <summary>First element of the step.</summary>
        public int from;
        /// <summary>One past the last element of the step (a text or image is one element).</summary>
        public int to;
        /// <summary>The step is a text or an image, which are separate objects, not mesh.</summary>
        public bool overlay;
    }

    /// <summary>
    /// Cuts a document into steps in paint order. Path elements before the first text or image form one mesh; each text
    /// or image is its own object; path elements after it form the next mesh, drawn above it. That keeps SVG's paint order
    /// when shapes follow text.
    /// </summary>
    public static class SvgDrawOrder
    {
        /// <summary>Fills <paramref name="items"/> (cleared first) and returns a hash of its layout.</summary>
        public static int Split(VectraSvgDocument doc, bool includeText, bool includeImages, List<SvgDrawItem> items)
        {
            items.Clear();
            int hash = 17;
            if (doc == null || doc.elements == null) return hash;
            int runStart = -1, runKey = 0;
            var list = doc.elements;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == null) continue;
                bool overlay = e.isText ? includeText : e.isImage && includeImages;
                if (overlay)
                {
                    Close(items, ref runStart, i, ref hash);
                    runKey = 0;
                    items.Add(new SvgDrawItem { from = i, to = i + 1, overlay = true });
                    hash = hash * 31 + i * 2 + 1;
                    continue;
                }

                if (e.isText || e.isImage || e.path == null || e.path.IsEmpty) continue;
                // A mesh draws with one texture: a pattern with another texture starts a new run.
                int key = SvgPatterns.TextureKey(e);
                if (key != 0 && runKey != 0 && key != runKey) Close(items, ref runStart, i, ref hash);
                if (key != 0) runKey = key;
                if (runStart < 0) runStart = i;
            }

            Close(items, ref runStart, list.Count, ref hash);
            return hash;
        }

        static void Close(List<SvgDrawItem> items, ref int runStart, int end, ref int hash)
        {
            if (runStart < 0) return;
            items.Add(new SvgDrawItem { from = runStart, to = end });
            hash = hash * 31 + runStart * 2 + end * 7;
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
