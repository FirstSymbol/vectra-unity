namespace FirstSymbol.Vectra.Svg
{
    /// <summary>
    /// Budgets and settings for parsing SVG. The defaults let ordinary files through and cut off hostile ones
    /// (very deep nesting, millions of elements, &lt;use&gt; chains that multiply). When a budget is hit the parser
    /// stops, keeps what it has and adds a line starting with "SVG limit reached" to the document warnings.
    /// </summary>
    public sealed class SvgParseOptions
    {
        /// <summary>Largest accepted file, in characters.</summary>
        public int maxInputLength = 32000000;
        /// <summary>Deepest element nesting, in the file and while drawing.</summary>
        public int maxDepth = 256;
        /// <summary>Most elements read from the file.</summary>
        public int maxElements = 200000;
        /// <summary>Most elements visited while drawing, counting every copy made by &lt;use&gt;.</summary>
        public int maxRenderedNodes = 200000;
        /// <summary>Most &lt;use&gt; copies expanded.</summary>
        public int maxUseExpansions = 10000;
        /// <summary>Longest path "d" or "points" value, in characters.</summary>
        public int maxPathDataLength = 1000000;
        /// <summary>Most characters of CSS read from &lt;style&gt; elements.</summary>
        public int maxStyleLength = 1000000;
        /// <summary>
        /// Languages used to pick a child of &lt;switch&gt; (systemLanguage). A tag matches when it equals a language
        /// or one is the other followed by a dash and a subtag ("en" matches "en-US").
        /// </summary>
        public string[] languages = { "en" };
    }
}
