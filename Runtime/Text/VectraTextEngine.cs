namespace FirstSymbol.Vectra.TextEngines
{
    /// <summary>The engine that draws the text of SVG documents.</summary>
    public enum VectraTextEngine
    {
        /// <summary>Lettera when it is installed, else TextMesh Pro when a font resolves, else the legacy uGUI Text.</summary>
        Auto = 0,
        Lettera = 1,
        TextMeshPro = 2,
        /// <summary>uGUI <c>Text</c> with the built-in font. Always available; ignores font families.</summary>
        Legacy = 3,
        /// <summary>SVG text is not drawn.</summary>
        None = 4
    }

    /// <summary>The choice on one SVG component: follow the project, or pick an engine for this component.</summary>
    public enum VectraTextEngineChoice
    {
        UseProjectSetting = 0,
        Auto = 1,
        Lettera = 2,
        TextMeshPro = 3,
        Legacy = 4,
        None = 5
    }

    public static class VectraTextEngineChoices
    {
        /// <summary>The engine a component asks for, given the project's setting.</summary>
        public static VectraTextEngine Effective(VectraTextEngineChoice choice, VectraTextEngine project)
        {
            switch (choice)
            {
                case VectraTextEngineChoice.Auto: return VectraTextEngine.Auto;
                case VectraTextEngineChoice.Lettera: return VectraTextEngine.Lettera;
                case VectraTextEngineChoice.TextMeshPro: return VectraTextEngine.TextMeshPro;
                case VectraTextEngineChoice.Legacy: return VectraTextEngine.Legacy;
                case VectraTextEngineChoice.None: return VectraTextEngine.None;
                default: return project;
            }
        }
    }
}
