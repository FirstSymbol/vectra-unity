using System;
using FirstSymbol.Vectra.Rendering;
using FirstSymbol.Vectra.Svg;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FirstSymbol.Vectra.Editor
{
    /// <summary>GameObject menu entries for every shape (UI and world) and helpers for SVG documents.</summary>
    static class VectraMenus
    {
        const string UI = "GameObject/UI/Vectra/";
        const string World = "GameObject/2D Object/Vectra/";
        const int Priority = 2100;

        static readonly Color s_Blue = new Color(0.24f, 0.55f, 0.96f);

        [MenuItem(UI + "Rectangle", false, Priority)] static void UIRect(MenuCommand c) => CreateUI<RectangleShape>(c);
        [MenuItem(UI + "Ellipse", false, Priority + 1)] static void UIEllipse(MenuCommand c) => CreateUI<EllipseShape>(c);
        [MenuItem(UI + "Arc · Ring · Pie", false, Priority + 2)] static void UIArc(MenuCommand c) => CreateUI<ArcShape>(c);
        [MenuItem(UI + "Polygon", false, Priority + 3)] static void UIPolygon(MenuCommand c) => CreateUI<PolygonShape>(c);
        [MenuItem(UI + "Star", false, Priority + 4)] static void UIStar(MenuCommand c) => CreateUI<StarShape>(c);
        [MenuItem(UI + "Triangle", false, Priority + 5)] static void UITriangle(MenuCommand c) => CreateUI<TriangleShape>(c);
        [MenuItem(UI + "Line", false, Priority + 6)] static void UILine(MenuCommand c) => CreateUI<LineShape>(c);
        [MenuItem(UI + "Path", false, Priority + 20)] static void UIPath(MenuCommand c) => CreateUI<PathShape>(c);
        [MenuItem(UI + "Boolean Group", false, Priority + 21)] static void UIBoolean(MenuCommand c) => CreateUI<BooleanShape>(c);
        [MenuItem(UI + "More/Cross", false, Priority + 40)] static void UICross(MenuCommand c) => CreateUI<CrossShape>(c);
        [MenuItem(UI + "More/Heart", false, Priority + 41)] static void UIHeart(MenuCommand c) => CreateUI<HeartShape>(c);
        [MenuItem(UI + "More/Arrow", false, Priority + 42)] static void UIArrow(MenuCommand c) => CreateUI<ArrowShape>(c);
        [MenuItem(UI + "More/Chevron", false, Priority + 43)] static void UIChevron(MenuCommand c) => CreateUI<ChevronShape>(c);
        [MenuItem(UI + "More/Rhombus", false, Priority + 44)] static void UIRhombus(MenuCommand c) => CreateUI<RhombusShape>(c);
        [MenuItem(UI + "More/Trapezoid", false, Priority + 45)] static void UITrapezoid(MenuCommand c) => CreateUI<TrapezoidShape>(c);
        [MenuItem(UI + "More/Parallelogram", false, Priority + 46)] static void UIParallelogram(MenuCommand c) => CreateUI<ParallelogramShape>(c);
        [MenuItem(UI + "More/Crescent", false, Priority + 47)] static void UICrescent(MenuCommand c) => CreateUI<CrescentShape>(c);
        [MenuItem(UI + "More/Egg", false, Priority + 48)] static void UIEgg(MenuCommand c) => CreateUI<EggShape>(c);
        [MenuItem(UI + "More/Vesica", false, Priority + 49)] static void UIVesica(MenuCommand c) => CreateUI<VesicaShape>(c);
        [MenuItem(UI + "More/Squircle", false, Priority + 50)] static void UISquircle(MenuCommand c) => CreateUI<SquircleShape>(c);
        [MenuItem(UI + "More/Flower", false, Priority + 51)] static void UIFlower(MenuCommand c) => CreateUI<FlowerShape>(c);
        [MenuItem(UI + "More/Gear", false, Priority + 52)] static void UIGear(MenuCommand c) => CreateUI<GearShape>(c);
        [MenuItem(UI + "More/Spiral", false, Priority + 53)] static void UISpiral(MenuCommand c) => CreateUI<SpiralShape>(c, true);
        [MenuItem(UI + "More/Wave", false, Priority + 54)] static void UIWave(MenuCommand c) => CreateUI<WaveShape>(c, true);
        [MenuItem(UI + "More/Speech Bubble", false, Priority + 55)] static void UIBubble(MenuCommand c) => CreateUI<SpeechBubbleShape>(c);
        [MenuItem(UI + "More/Checkmark", false, Priority + 56)] static void UICheck(MenuCommand c) => CreateUI<CheckmarkShape>(c, true);
        [MenuItem(UI + "More/Polyline", false, Priority + 57)] static void UIPolyline(MenuCommand c) => CreateUI<PolylineShape>(c, true);
        [MenuItem(UI + "More/SVG Path", false, Priority + 58)] static void UISvgPath(MenuCommand c) => CreateUI<SvgPathShape>(c);
        [MenuItem(UI + "More/Morph", false, Priority + 59)] static void UIMorph(MenuCommand c) => CreateUI<MorphShape>(c);

        [MenuItem(UI + "SVG Image", false, Priority + 30)]
        static void UISvg(MenuCommand c)
        {
            var go = CreateUIObject("Vectra SVG Image", c, typeof(VectraSvgImage));
            go.GetComponent<VectraSvgImage>().FontMap = VectraProjectSettings.instance.defaultFontMap;
            if (Selection.activeObject is VectraSvgDocument doc) go.GetComponent<VectraSvgImage>().Document = doc;
        }

        [MenuItem(World + "Rectangle", false, Priority)] static void WRect(MenuCommand c) => CreateWorld<RectangleShape>(c);
        [MenuItem(World + "Ellipse", false, Priority + 1)] static void WEllipse(MenuCommand c) => CreateWorld<EllipseShape>(c);
        [MenuItem(World + "Arc · Ring · Pie", false, Priority + 2)] static void WArc(MenuCommand c) => CreateWorld<ArcShape>(c);
        [MenuItem(World + "Polygon", false, Priority + 3)] static void WPolygon(MenuCommand c) => CreateWorld<PolygonShape>(c);
        [MenuItem(World + "Star", false, Priority + 4)] static void WStar(MenuCommand c) => CreateWorld<StarShape>(c);
        [MenuItem(World + "Path", false, Priority + 20)] static void WPath(MenuCommand c) => CreateWorld<PathShape>(c);
        [MenuItem(World + "Boolean Group", false, Priority + 21)] static void WBoolean(MenuCommand c) => CreateWorld<BooleanShape>(c);

        [MenuItem(World + "SVG Renderer", false, Priority + 30)]
        static void WSvg(MenuCommand c)
        {
            var go = new GameObject("Vectra SVG", typeof(VectraSvgRenderer));
            Place(go, c);
            if (Selection.activeObject is VectraSvgDocument doc) go.GetComponent<VectraSvgRenderer>().Document = doc;
        }

        [MenuItem("Assets/Create/Vectra/SVG Image in Scene", false, 30)]
        static void SvgFromAsset()
        {
            if (!(Selection.activeObject is VectraSvgDocument doc)) return;
            var go = CreateUIObject(doc.name, new MenuCommand(null), typeof(VectraSvgImage));
            var img = go.GetComponent<VectraSvgImage>();
            img.FontMap = VectraProjectSettings.instance.defaultFontMap;
            img.Document = doc;
            img.SetNativeSize();
        }

        [MenuItem("Assets/Create/Vectra/Shape in Scene", false, 31)]
        static void ShapeFromAsset()
        {
            foreach (var asset in Selection.GetFiltered<VectraShapeAsset>(SelectionMode.Assets))
            {
                var go = CreateUIObject(asset.name, new MenuCommand(null), typeof(VectraShape));
                var shape = go.GetComponent<VectraShape>();
                shape.Content.shapeAsset = asset;
                shape.Content.style = asset.defaultStyle;
                shape.SetShapeDirty();
            }
        }

        [MenuItem("Assets/Create/Vectra/Shape in Scene", true)]
        static bool ShapeFromAssetValidate() => Selection.GetFiltered<VectraShapeAsset>(SelectionMode.Assets).Length > 0;
        [MenuItem("Assets/Create/Vectra/SVG Image in Scene", true)]
        static bool SvgFromAssetValidate() => Selection.activeObject is VectraSvgDocument;

        static void CreateUI<T>(MenuCommand c, bool openPath = false) where T : VectraShapeGeometry, new()
        {
            var go = CreateUIObject("Vectra " + new T().DisplayName, c, typeof(VectraShape));
            var shape = go.GetComponent<VectraShape>();
            Configure(shape.Content, new T(), openPath, 4f);
            shape.SetShapeDirty();
        }

        static void CreateWorld<T>(MenuCommand c, bool openPath = false) where T : VectraShapeGeometry, new()
        {
            var go = new GameObject("Vectra " + new T().DisplayName, typeof(VectraRenderer));
            Place(go, c);
            var r = go.GetComponent<VectraRenderer>();
            var g = new T();
            if (g is RectangleShape rect) rect.cornerRadii = Vector4.one * 0.12f;
            Configure(r.Content, g, openPath, 0.04f);
            r.SetDirty();
        }

        static void Configure(VectraShapeContent content, VectraShapeGeometry g, bool openPath, float strokeWidth)
        {
            content.geometry = g;
            content.fills.Clear();
            content.strokes.Clear();
            if (openPath || !g.IsClosed)
                content.strokes.Add(new StrokeLayer(s_Blue, strokeWidth * 2f) { cap = Geometry.LineCap.Round, join = Geometry.LineJoin.Round });
            else
                content.fills.Add(new FillLayer(VectraPaint.Solid(s_Blue)));
            content.quality = VectraProjectSettings.instance.defaultQuality;
        }

        static void Place(GameObject go, MenuCommand c)
        {
            GameObjectUtility.SetParentAndAlign(go, c.context as GameObject);
            if (c.context == null && SceneView.lastActiveSceneView != null)
            {
                var p = SceneView.lastActiveSceneView.pivot;
                go.transform.position = new Vector3(p.x, p.y, 0f);
            }

            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeGameObject = go;
        }

        static GameObject CreateUIObject(string name, MenuCommand c, Type component)
        {
            var parent = c.context as GameObject;
            if (parent == null || parent.GetComponentInParent<Canvas>() == null) parent = FindOrCreateCanvas();
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), component);
            ((RectTransform)go.transform).sizeDelta = new Vector2(160f, 160f);
            GameObjectUtility.SetParentAndAlign(go, parent);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            Selection.activeGameObject = go;
            return go;
        }

        static GameObject FindOrCreateCanvas()
        {
            var canvas = UnityEngine.Object.FindAnyObjectByType<Canvas>();
            if (canvas != null) return canvas.gameObject;
            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            Undo.RegisterCreatedObjectUndo(go, "Create Canvas");
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                Undo.RegisterCreatedObjectUndo(es, "Create EventSystem");
            }

            return go;
        }

        [MenuItem("CONTEXT/VectraShape/Export as PNG...")]
        static void ExportShape(MenuCommand c)
        {
            var shape = (VectraShape)c.context;
            var r = shape.rectTransform.rect;
            string path = EditorUtility.SaveFilePanel("Export PNG", "", shape.name + ".png", "png");
            if (string.IsNullOrEmpty(path)) return;
            var tex = VectraRasterizer.Render(shape.Content, r.size, Mathf.CeilToInt(r.width * 2f), Mathf.CeilToInt(r.height * 2f));
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        [MenuItem("CONTEXT/VectraShape/Export as SVG...")]
        static void ExportShapeSvg(MenuCommand c)
        {
            var shape = (VectraShape)c.context;
            string path = EditorUtility.SaveFilePanel("Export SVG", "", shape.name + ".svg", "svg");
            if (string.IsNullOrEmpty(path)) return;
            System.IO.File.WriteAllText(path, SvgWriter.Write(shape.Content, shape.rectTransform.rect));
        }
    }
}
