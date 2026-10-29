using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.Editor
{
    /// <summary>Paint editor that only shows the settings relevant to the selected paint type.</summary>
    [CustomPropertyDrawer(typeof(VectraPaint))]
    class VectraPaintDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            var typeProp = property.FindPropertyRelative("type");
            var header = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var type = new PropertyField(typeProp, "Paint") { style = { flexGrow = 1 } };
            header.Add(type);
            root.Add(header);
            var body = new VisualElement();
            root.Add(body);

            void Rebuild()
            {
                body.Clear();
                var t = (PaintType)typeProp.enumValueIndex;
                void F(string name, string label = null)
                {
                    var p = property.FindPropertyRelative(name);
                    if (p == null) return;
                    var f = label != null ? new PropertyField(p, label) : new PropertyField(p);
                    f.Bind(property.serializedObject);
                    body.Add(f);
                }

                switch (t)
                {
                    case PaintType.Solid:
                        F("color");
                        break;
                    case PaintType.LinearGradient:
                    case PaintType.RadialGradient:
                    case PaintType.AngularGradient:
                    case PaintType.DiamondGradient:
                    {
                        var g = new VectraGradientField(property.FindPropertyRelative("gradient"));
                        body.Add(g);
                        if (property.FindPropertyRelative("useCustomMapping").boolValue)
                        {
                            body.Add(new HelpBox("Mapped by an imported transform (SVG).", HelpBoxMessageType.None));
                            F("useCustomMapping", "Custom Mapping");
                            break;
                        }

                        F("gradientStart", t == PaintType.LinearGradient ? "Start" : "Center");
                        F("gradientEnd", t == PaintType.LinearGradient ? "End" : "Radius Handle");
                        if (t == PaintType.LinearGradient)
                        {
                            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginLeft = 3 } };
                            row.Add(new Label("Angle") { style = { width = 120 } });
                            foreach (int a in new[] { 0, 45, 90, 135, 180, 270 })
                            {
                                int angle = a;
                                row.Add(new Button(() =>
                                {
                                    float r = angle * Mathf.Deg2Rad;
                                    var d = new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * 0.5f;
                                    property.FindPropertyRelative("gradientStart").vector2Value = new Vector2(0.5f, 0.5f) - d;
                                    property.FindPropertyRelative("gradientEnd").vector2Value = new Vector2(0.5f, 0.5f) + d;
                                    property.serializedObject.ApplyModifiedProperties();
                                }) { text = angle + "°" });
                            }

                            body.Add(row);
                        }
                        else F("gradientAspect", "Aspect");

                        if (t == PaintType.RadialGradient) F("focalPoint", "Focal Point");
                        F("spread");
                        break;
                    }
                    case PaintType.Texture:
                        F("texture");
                        F("color", "Tint");
                        F("textureFit", "Fit");
                        F("textureUvRect", "UV Rect");
                        F("textureScale", "Scale");
                        F("textureOffset", "Offset");
                        F("textureRotation", "Rotation");
                        break;
                    case PaintType.Freeform:
                        F("freeformPoints", "Color Points");
                        break;
                    default:
                        F("color", "Color A");
                        F("secondaryColor", "Color B");
                        F("patternSize", "Cell Size");
                        if (t != PaintType.Noise) F("patternRotation", "Rotation");
                        if (t == PaintType.Stripes || t == PaintType.Dots || t == PaintType.Grid) F("patternRatio", "Ratio");
                        F("patternOffset", "Offset");
                        break;
                }

                F("opacity");
                F("blend");
            }

            type.RegisterValueChangeCallback(_ => Rebuild());
            Rebuild();
            return root;
        }
    }
}
