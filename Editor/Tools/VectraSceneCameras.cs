using FirstSymbol.Vectra.Rendering;
using UnityEditor;
using UnityEngine;

namespace FirstSymbol.Vectra.Editor
{
    /// <summary>Tells the SVG pictures about the Scene views, which can magnify a Canvas far beyond what the Game view shows.</summary>
    [InitializeOnLoad]
    static class VectraSceneCameras
    {
        static VectraSceneCameras()
        {
            VectraScreenDensity.ExtraCameraCount = () => SceneView.sceneViews.Count;
            VectraScreenDensity.ExtraCamera = i => SceneView.sceneViews[i] is SceneView view ? view.camera : null;
        }
    }
}
