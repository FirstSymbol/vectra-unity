# Vectra — векторная графика для Unity

Vectra рисует векторные фигуры в **uGUI Canvas**, в **world space** и в **UI Toolkit**. Поддерживаются:
- аналитические примитивы и произвольные пути;
- булевы операции, градиенты, обводки, эффекты и маски;
- SVG-документы.

Все фигуры используют один материал, поэтому интерфейс из фигур Vectra батчится в несколько draw call. Пересборка аналитических фигур, путей прямоугольника и эллипса и булевых групп из них не выделяет управляемую память (в режиме пути звезда, многоугольник и ещё несколько фигур пока создают небольшой массив точек).

*English version: [Vectra.md](Vectra.md).*

- **Unity:** 6000.3 (6.3 LTS) и новее.
- **Пайплайны:** Built-in, URP, HDRP. Backdrop-эффекты работают только в URP.
- **Зависимости:** Burst, Collections, Mathematics, uGUI. Текст из SVG рисует Lettera или TextMeshPro, если они есть, иначе встроенный uGUI Text.
- **Платформы:** Shader Model 3.5 и выше — Windows, macOS, Linux, WebGL 2, WebGPU, iOS, Android. Compute-шейдеры не используются. На WebGL Burst-джобы выполняются как код IL2CPP.

## Содержание

1. [Установка](#установка)
2. [Как это устроено](#как-это-устроено)
3. [Компоненты](#компоненты)
4. [UI Toolkit](#ui-toolkit)
5. [Фигуры](#фигуры)
6. [Заливки, обводки и палитры](#заливки-обводки-и-палитры)
7. [Эффекты](#эффекты)
8. [Маски](#маски)
9. [Булевы операции](#булевы-операции)
10. [SVG](#svg)
11. [Мир и Shader Graph](#мир-и-shader-graph)
12. [Векторные спрайты](#векторные-спрайты)
13. [Анимация](#анимация)
14. [URP backdrop](#urp-backdrop)
15. [Инструменты редактора](#инструменты-редактора)
16. [Командная строка](#командная-строка)
17. [Производительность](#производительность)
18. [Частые проблемы](#частые-проблемы)

## Установка

Package Manager → **+** → **Add package from git URL…** → `https://github.com/FirstSymbol/vectra-unity.git`. Чтобы поставить конкретную версию, добавьте к адресу `#0.6.0`.

Сэмплы находятся в **Package Manager → Vectra Vector Graphics → Samples**.

| Сэмпл | Что показывает |
|---|---|
| Shapes Gallery | Все примитивы и генераторы путей, заливки, анимированный trim, dash, fill amount, морфинг |
| SVG Showcase | SVG в Canvas и в мире, переопределения по элементам |
| Effects & Masks | Все эффекты; soft, инвертированная и stencil-маски; палитра с переключением темы |
| Boolean Operations | Группы с движущимися операндами, smooth union (метаболы), BooleanShape из кода |
| Animation | Твины, AnimationClip через Animated Properties, морфинг, бегущий пунктир |
| World Space | Карточки VectraRenderer в 3D, сгенерированные коллайдеры, обработка кликов |
| UI Toolkit | UXML-панель с `VectraElement`, стилизованными из USS, SVG-элемент и звезда из кода (см. [UI Toolkit](#ui-toolkit)) |
| URP Backdrop | Frosted glass и 15 blend-режимов поверх движущегося UI (см. [URP backdrop](#urp-backdrop)) |

## Как это устроено

**Аналитические фигуры** (прямоугольник, эллипс, звезда и другие) рисуются одним квадом на слой. Фрагментный шейдер вычисляет точное знаковое расстояние до контура. Благодаря этому:
- края сглажены на любом масштабе;
- радиусы углов не растягиваются;
- тени, свечение, bevel и обводки почти ничего не стоят.

**Path-фигуры** (кривые Безье, SVG, булевы операции, шестерни и другие) Burst-джобы на CPU флаттенят и разбивают на трапеции, стороны которых сглаживаются аналитически. Эффектам, которым нужно поле расстояний (тени, свечение, blur, pixelate, soft-маски), поле запекается в общий атлас.

**Один материал.** Параметры слоёв передаются в вершинах, градиенты хранятся в общем атласе градиентов, а запечённые поля расстояний — в общем SDF-атласе. Поэтому у всех фигур Vectra один материал и одни текстуры. Исключения со своей текстурой или материалом: текстурные заливки, backdrop-фигуры и дети soft-маски.

**Пересборка** происходит только при изменениях. Тесселяция, обводки и поля расстояний кешируются по хешам.

## Компоненты

| Компонент | Назначение |
|---|---|
| `VectraShape` | Фигура uGUI (`MaskableGraphic`): Mask, RectMask2D (с softness), layout, точный raycast |
| `VectraRenderer` | Фигура в мире (MeshRenderer): sorting layer и order, опциональный `PolygonCollider2D` |
| `VectraSvgImage` / `VectraSvgRenderer` | SVG-документ одним мешем (UI / мир) с переопределениями по элементам |
| `VectraElement` / `VectraSvgElement` | Те же фигуры и SVG-документы для UI Toolkit (UXML, USS, рантайм и окна редактора): [UI Toolkit](#ui-toolkit) |
| `VectraBooleanGroup` + `VectraBooleanOperand` | Дочерние фигуры становятся операндами булевой операции |
| `VectraMask` | Маскирует детей по контуру фигуры: stencil или soft (с пером, с инверсией) |
| `VectraTheme` | Делает `VectraPalette` активной и выбирает тему |
| `VectraAnimatedProperties` | Поля для Animator и Timeline (trim, цвета, fill amount и др.) |
| `VectraInteractionStates` | Состояния hover, pressed, selected (фокус) и disabled с плавными переходами |
| `VectraBackdrop` (URP) | Frosted glass и сложные blend-режимы |

Ассеты:
- `VectraStyle` — общий набор заливок, обводок и эффектов, как CSS-класс;
- `VectraShapeAsset` — общая геометрия, основа библиотеки фигур;
- `VectraPalette` — именованные цвета по темам;
- `VectraSvgDocument` — импортированный SVG;
- `VectraFontMap` — соответствие font-family шрифтам каждого движка текста.

Объекты создаются через **GameObject → UI → Vectra** (Canvas) или **GameObject → 2D Object → Vectra** (мир).

```csharp
var shape = gameObject.AddComponent<VectraShape>();
var star = shape.SetGeometry<StarShape>();
star.points = 6;
shape.SetFill(VectraPaint.Linear(Color.yellow, Color.red, 90f))
     .SetStroke(Color.white, 3f, StrokeAlignment.Outside);
shape.AddEffect<DropShadowEffect>().blur = 12f;

// после изменения параметров из кода:
shape.Strokes[0].trimEnd = 0.5f;
shape.SetShapeDirty();
```

## UI Toolkit

`VectraElement` и `VectraSvgElement` (пространство имён `FirstSymbol.Vectra.UIToolkit`, сборка `FirstSymbol.Vectra.UIToolkit`) рисуют Vectra в панелях UI Toolkit: в рантайм-`UIDocument` и в окнах редактора. Оба — `[UxmlElement]`, поэтому UI Builder показывает их в **Library → Project → Custom Controls (C#)**.

| Элемент | Назначение |
|---|---|
| `VectraElement` | Любой примитив или путь с заливками, обводками, эффектами, булевыми операциями, палитрами и стилями. Берёт тот же `VectraShapeContent`, что и `VectraShape`, поэтому код для uGUI переносится без изменений. |
| `VectraSvgElement` | `VectraSvgDocument`: фигуры, переопределения цветов по селектору, порядок рисования, текст SVG (см. ограничения ниже). |

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:vf="FirstSymbol.Vectra.UIToolkit">
    <vf:VectraElement shape="Star" sides="6" fill="#ffcc00" stroke="#222222" stroke-width="3" class="badge" />
    <vf:VectraSvgElement document="project://database/Assets/Icons/logo.svg" />
</ui:UXML>
```

Атрибуты `VectraElement`: `shape` (Rectangle, Ellipse, Triangle, Polygon, Star, Heart, Squircle, Custom), `sides`, `fill`, `stroke`, `stroke-width`, `corner-radius`, `quality`, `shape-asset` и `style-asset` (общая геометрия и внешний вид). Атрибуты `VectraSvgElement`: `document`, `preserve-aspect`, `quality`, `generate-text`. Размер задают обычные `width` и `height`; без них элемент 100 на 100 пунктов.

Свойства USS переопределяют атрибуты и меняются вместе с `:hover`, `:active` и другими псевдосостояниями:

```css
.badge { --vectra-fill: #ffcc00; --vectra-stroke: #222222; --vectra-stroke-width: 3; --vectra-corner-radius: 8; }
.badge:hover { --vectra-fill: #ffe680; }
```

Из кода фигура описывается так же, как у `VectraShape`:

```csharp
using FirstSymbol.Vectra.UIToolkit;
using UnityEngine.UIElements;

var root = new VisualElement();
var star = new VectraElement { Shape = VectraElementShape.Star, Fill = Color.yellow, StrokeWidth = 3f };
star.style.width = 120;
star.style.height = 120;
star.SetGeometry<StarShape>().points = 7;
star.AddEffect<DropShadowEffect>().blur = 10f;
star.Fills[0].paint = VectraPaint.Linear(Color.yellow, Color.red, 90f);
star.SetShapeDirty();   // после изменения геометрии, заливок, обводок или эффектов
root.Add(star);
```

**Как это устроено.** Меш UI Toolkit несёт только позицию, цвет и один uv, а `MeshGenerationContext` принимает текстуру, а не материал; вершина Vectra несёт четыре float4. Поэтому элемент строит тот же меш, что и `VectraShape`, рисует его тем же материалом и шейдером в render texture, а UI Toolkit показывает эту текстуру квадом. Сглаженные SDF-края, градиенты, штрихи, тени, свечения, фаски и все остальные эффекты выглядят так же, как в Canvas. Маленькие элементы делят страницы по 2048 пикселей, поэтому UI Toolkit батчит их квады в один draw call; элемент больше 512 пикселей получает свою текстуру. Картинка перерисовывается только когда меняются фигура, размер элемента или плотность экрана (на 25 %); неподвижный элемент ничего не стоит, а перерисовка после прогрева не выделяет управляемую память.

**Ограничения.**
- Элемент — растр той плотности, для которой он нарисован. Масштаб (transform) растягивает его, пока плотность не ушла на 25 %, затем он перерисовывается. Повёрнутый элемент пересэмплируется и мягче.
- Режимы смешивания кроме Normal, `VectraMask`, состояния взаимодействия, URP backdrop и текст через Lettera относятся к uGUI и миру и здесь не работают. Фигура не может смешиваться с тем, что находится за элементом.
- Текст SVG рисуют метки (label) UI Toolkit шрифтом панели (у Lettera и TextMesh Pro нет элемента для UI Toolkit). Чтобы был виден какой-либо шрифт, у панели должна быть тема (theme style sheet). Базовая линия и выравнивание считаются по размеру шрифта, поэтому шрифт с необычными метриками может сидеть на пункт-два выше или ниже.
- Элементы SVG `<image>` пропускаются.
- Текстура не больше 4096 пикселей по стороне; элемент больше рисуется с меньшей плотностью.
- Попадание курсора определяется по контуру (`ContainsPoint`), с учётом дыр и обводок.

## Фигуры

- **Аналитические:**
  - прямоугольник: свой радиус на каждый угол; стиль угла round, chamfer, inverted, notch или squircle;
  - эллипс, дуга, кольцо, сектор, многоугольник, звезда, треугольник, линия;
  - крест, сердце, ромб, трапеция, параллелограмм, полумесяц, яйцо, vesica, суперэллипс, цветок, стрелка, шеврон.
- **Пути:** путь Безье (редактируется в Scene view), полилиния со скруглением, спираль, шестерня, волна, данные SVG-пути, облачко, галочка.
- **Составные:** булева фигура (точная или плавная), морфинг между двумя фигурами.

- **Из спрайтов:** `TracedSprite` превращает альфа-силуэт спрайта в контуры. Настраиваются порог альфы, разрешение сетки, упрощение и сглаживание. Дыры учитываются, текстура не обязана быть readable.

У каждой фигуры есть `rotation`. Аналитическую фигуру можно отрисовать как путь флагом **Force Path**. Для dash-обводки это происходит автоматически.

**Convert to editable path** (вкладка Shape или `VectraPathConverter.ToPathShape`) превращает любую фигуру, SVG-путь или трассированный спрайт в точки Безье, которые редактируются в Scene view.

**Общие фигуры.** Геометрию можно сохранить как `VectraShapeAsset`: вкладка Shape → Save или Assets → Create → Vectra → Shape. Все объекты, ссылающиеся на ассет, рисуют одну геометрию и обновляются при его изменении. **Detach** копирует её обратно для локальной правки. Вместе со стилями и палитрами это библиотека фигур; поставить фигуру в сцену можно через Assets → Create → Vectra → Shape in Scene.

## Заливки, обводки и палитры

**Типы paint:**
- solid (HDR);
- градиенты linear, radial (с фокусом), angular, diamond. Spread: pad, repeat, reflect. Число стопов не ограничено, есть midpoint. Интерполяция sRGB, Linear, OKLab или Step;
- texture (stretch, fit, fill, tile);
- паттерны stripes, checker, dots, grid, noise;
- freeform-градиент;
- **contour gradient** — градиент по расстоянию от контура. Даёт затухание к краю, а со spread Repeat/Reflect — кольца и полосы, повторяющие форму.

Цвет и альфа задаются в одном списке стопов. Градиенты дизерятся против бэндинга.

**Обводки:**
- любое количество на фигуру;
- позиция: по центру, внутри, снаружи;
- caps и joins с miter limit;
- dash с абсолютными или относительными длинами и offset;
- trim path;
- профиль ширины (`AnimationCurve`);
- отрисовка под заливкой или над ней.

**Hairline.** У обводки есть **Unit**: Local (ширина следует масштабу фигуры) или **Pixels**: обводка остаётся шириной N пикселей
экрана при любом масштабе и зуме, со сглаживанием, в Canvas и в мировом пространстве. Меш следует размеру пикселя
(изменение на 1% пересобирает его), поэтому зум камеры или смена Canvas Scaler не размывают линию. `vector-effect="non-scaling-stroke"` из SVG превращается в такую обводку (см. раздел SVG).

```csharp
shape.SetStroke(Color.white, 1f, StrokeAlignment.Center);
shape.Strokes[0].widthMode = StrokeWidthMode.Pixels;   // 1 пиксель при любом зуме
shape.SetShapeDirty();
```

**Палитры.** `VectraPalette` (Assets → Create → Vectra → Palette) хранит именованные цвета, по одному значению на тему.
1. В строке **Palette** у заливки, обводки или цвета паттерна выберите имя цвета.
2. Добавьте на сцену компонент **Vectra Theme**: он делает палитру активной и выбирает тему.

При смене темы перекрашиваются все фигуры, использующие токены:

```csharp
theme.SetTheme("Dark");            // компонент VectraTheme
VectraPalette.Active = palette;    // или напрямую из кода
palette.SetTheme("Light");
```

Токены работают для цветов paint: solid, оттенок текстуры, цвета паттерна. Цвета эффектов задаются обычными значениями.

## Эффекты

Доступные эффекты:
- drop shadow, inner shadow;
- outer glow, inner glow;
- bevel & emboss;
- layer blur;
- noise / grain;
- color adjust (hue, saturation, brightness, contrast);
- fill amount (horizontal, vertical, radial);
- dissolve;
- **pixelate**;
- **outline** — полоса поля расстояний, скруглённая на всех углах;
- **color overlay**, **gradient overlay**;
- **extrude** — слои силуэта от ближнего цвета к дальнему;
- **deform** — волнистый контур, анимируется на GPU без пересборки.

У каждого слоя и эффекта blend Normal или Additive. Для всей фигуры доступны Normal, Additive, Multiply и Screen.

Одновременно действует только один модификатор из fill amount, pixelate и dissolve; приоритет в этом же порядке.

У path-фигур pixelate и эффекты на основе расстояния используют запечённое поле. Геометрические обводки path-фигур не пикселизуются.

**Пользовательские эффекты.** Унаследуйте `VectraCustomEffect` и выводите слои в `Populate`. Они проходят тот же трекинг изменений, батчинг и шейдер, что и встроенные эффекты, и появляются в меню Add effect:

```csharp
[Serializable] public class NeonRings : VectraCustomEffect
{
    public Color color = Color.cyan; public int rings = 3; public float gap = 6f;
    public override string DisplayName => "Neon Rings";
    public override float Reach => rings * gap + 4f;
    public override void Populate(VectraEffectBuilder b)
    {
        for (int i = 1; i <= rings; i++) b.Band(color, 2f, i * gap);   // также Fill, Shadow, InnerShadow
    }
}
```

## Маски

Добавьте **Vectra Mask** на `VectraShape`. Есть два режима.

- **Stencil.** Работает как uGUI `Mask`, но stencil повторяет точный контур, а не прямоугольник. Маскирует любой UI под собой: Image, Text, другие фигуры. Край жёсткий.
- **Soft.** Вычисляется шейдером Vectra попиксельно по функции расстояния:
  - `Softness` — перо от края внутрь;
  - `Inset` — сдвиг края;
  - `Invert` — показывает детей снаружи фигуры.

  Действует на детей `VectraShape` и `VectraSvgImage`, остальная графика не маскируется. Дети одной soft-маски используют общий материал и батчатся между собой.

## Булевы операции

**Vectra Boolean Group** делает дочерние фигуры операндами операций union, subtract, intersect и exclude; порядок определяет иерархия.
- Дети скрываются, а результат рисует собственная фигура группы со всеми её заливками, обводками и эффектами.
- Операнды можно двигать, поворачивать и масштабировать в рантайме.
- Параметр **Smoothness** плавно смешивает операнды (метаболы) через поле расстояний.

Из кода можно работать с `BooleanShape` напрямую:

```csharp
var b = shape.SetGeometry<BooleanShape>();
b.operands.Clear();
b.operands.Add(new BooleanOperand(new EllipseShape(), BooleanOp.Union, new Rect(0, 0, 1, 1)));
b.operands.Add(new BooleanOperand(new EllipseShape(), BooleanOp.Subtract, new Rect(0.25f, 0.25f, 0.5f, 0.5f)));
shape.SetShapeDirty();
```

## SVG

**Импорт.** Включить импортёр Vectra можно двумя способами:
- выбрать **Vectra Svg Importer** в списке импортёров `.svg`-файла;
- включить **Project Settings → Vectra → Use Vectra importer**. Кнопка рядом переключает все существующие файлы.

Режимы импорта: Vectra Document, Sprite или Texture. Окно **Window → Vectra → SVG Import Report** показывает, что из каждого файла не удалось воспроизвести.

**Поддерживается:**
- path (все команды), базовые фигуры, группы, вложенные svg, use, symbol, defs;
- transform;
- CSS: `<style>`, селекторы, специфичность, наследование; презентационные атрибуты;
- linear- и radial-градиенты с transform и spread;
- обводки с dash, opacity;
- clip-path;
- фильтры blur, drop shadow, saturate и hue-rotate;
- `<text>` со стилями `<tspan>` (см. «Движки текста» ниже);
- встроенные data-URI картинки;
- маркеры (см. «Маркеры» ниже).

**Порядок рисования.** Текст и картинки — отдельные объекты, а фигуры, идущие после них в файле, рисуются выше. `VectraSvgImage` рисует фигуры до первого текста своим мешем, а каждую следующую серию фигур кладёт в дочерний объект (`SVG Layer`) после текста, за которым она идёт. Красный блок, стоящий в файле после текста, закрывает этот текст, как в браузере. `VectraSvgRenderer` делает то же через sorting order. Дочерние объекты создаются автоматически и не сохраняются; при изменении размера изображения они не пересоздаются, меняются только их позиции.

**Маркеры.** `marker-start`, `marker-mid`, `marker-end` и сокращение `marker` работают на `path`, `line`, `polyline` и `polygon`. Поддерживаются `orient` (`auto`, `auto-start-reverse` или угол), `markerUnits` (по умолчанию `strokeWidth`, либо `userSpaceOnUse`), `refX` / `refY`, `viewBox`, `markerWidth` / `markerHeight` и `overflow` (по умолчанию hidden: маркер обрезается по своему viewport). Маркеры рисуются как обычные элементы после фигуры, поэтому получают её transform, clip-path и фильтр. Маркеры на содержимом самого маркера не рисуются, а каждый маркер считается в лимите `<use>`.

**Рантайм-API:**

```csharp
var doc = VectraSvg.Parse(svgText);                 // или VectraSvg.Load(textAsset)
svgImage.Document = doc;
VectraSvg.Download(url, (d, error) => svgImage.Document = d);
```

**Expand to hierarchy** превращает документ в отдельный `VectraShape` на каждый элемент, чтобы их можно было редактировать и анимировать по отдельности. Запуск: кнопка в инспекторе SVG Image, контекстное меню или `VectraSvg.Expand(image)`. Clip-path превращается в операнды булевой операции у залитых элементов или во вложенные stencil-маски у элементов с обводкой.

**Движки текста.** `<text>` из SVG рисует движок текста; выбирается в **Project Settings → Vectra → Text engine**:

| Движок | Чем рисуется | Что нужно |
|---|---|---|
| Auto (по умолчанию в новых проектах) | Lettera, если установлена; иначе TextMesh Pro, если есть шрифт; иначе Legacy | |
| Lettera | `LetteraText` | пакет Lettera 1.0.0 или новее |
| TextMesh Pro | `TextMeshProUGUI` | TMP Essential Resources или шрифт TMP в font map или на компоненте |
| Legacy | uGUI `Text` со встроенным шрифтом | ничего; семейства шрифтов игнорируются |
| None | текст не рисуется | |

Страница показывает, какие движки есть в проекте и что выберет Auto. Если названный движок не установлен или у него нет шрифта, берётся следующий по порядку Auto, а консоль один раз на документ говорит об этом. Каждый SVG Image может переопределить выбор проекта (**Text engine** в инспекторе или `image.TextEngine`). Выбор попадает и в сборку плеера.

Стиль текста сохраняется: `<tspan>` со своим bold, italic, цветом или размером остаётся внутри одного текста разметкой (`<b>`, `<i>`, `<color>`, `<size>`) в любом движке. Не рисуется: позиции отдельных `<tspan>` (`x`, `y`, `dx`, `dy`), `textPath`, `letter-spacing` и подчёркивание; `<tspan>` с другим `font-family` берёт шрифт всего текста.

**Текст в мировом пространстве.** `VectraSvgRenderer` рисует текст через те же движки, но их текстом без Canvas: **World Text** Lettera (`LetteraWorldText`) или **3D** TextMesh Pro (`TextMeshPro`). Legacy uGUI `Text` работает только внутри Canvas, поэтому с одним этим движком текст мирового SVG пропускается, а консоль один раз сообщает об этом. У рендерера те же **Text engine**, **Font map** и запасные шрифты, что у UI-изображения. Текст и фигуры после него сохраняют порядок через sorting order: `SortingOrder` компонента плюс единица на каждый шаг документа. Движок, написанный под 0.3, должен получить `SupportsWorld` и `BuildWorld` (если он не может рисовать без Canvas, верните `false` и ничего не делайте).

**Обновление с 0.2.** До 0.3 весь текст SVG шёл через TextMesh Pro. Проект, который уже пользовался Vectra (есть файл настроек Vectra, font map или SVG с текстом), один раз получает в настройках **TextMesh Pro** и строку в консоли, поэтому текст выглядит как раньше. Чтобы перейти на Lettera, выберите **Auto** или **Lettera**. Новые проекты стартуют с Auto. Поля `VectraFontMap` для TextMesh Pro остаются как были (теперь это обычные ссылки на объекты, сохранённые данные те же); рядом появились поля Lettera: `lettera`, `letteraBold`, `letteraFallback` (шрифт Lettera или набор шрифтов).

**Шрифты.** `VectraFontMap` (Assets → Create → Vectra → SVG Font Map) сопоставляет имена `font-family` шрифтам каждого движка. Назначается на SVG Image; новые изображения получают font map по умолчанию из Project Settings. Без совпадения берётся запасной шрифт компонента (свой для каждого движка), затем шрифт движка по умолчанию. В этой версии Lettera находит шрифты только через font map.

**Ограничения:**
- `<mask>` применяется как геометрический клип, яркость маски не учитывается;
- паттерн-заливки заменяются цветом первой фигуры паттерна;
- из фильтров поддерживаются только blur, drop shadow, saturate и hue-rotate;
- внешние картинки по URL не загружаются;
- `opacity` группы применяется к каждому потомку отдельно, поэтому перекрывающиеся потомки просвечивают друг через друга (отчёт импорта сообщает об этом);
- текст и картинки не обрезаются по `clip-path`; картинки — прямоугольники по осям, `preserveAspectRatio` для них игнорируется;
- `<use>` перекрашивает цель только для своей копии, а на своём месте цель сохраняет свой стиль;
- из `<switch>` рисуется только первый потомок, у которого выполнены условия `systemLanguage` / `requiredExtensions` (список языков — `SvgParseOptions.languages`, по умолчанию `en`).

**Обводки, не зависящие от масштаба.** `vector-effect="non-scaling-stroke"` сохраняет ширину обводки на экране при любых преобразованиях, как в браузерах: `stroke-width` в экранных единицах и не масштабируется ни `transform` элемента, ни вписыванием `viewBox`, ни Canvas, ни масштабом объекта, ни камерой. `stroke-dasharray` и `stroke-dashoffset` такой обводки тоже в экранных единицах. Парсер делает из неё обводку в пикселях (`StrokeWidthMode.Pixels` с `dashInPixels`). Один SVG-px — это одна опорная точка на Canvas экранного пространства (`stroke-width="2"` — 2 × scale factor Canvas физических пикселей; на Canvas мирового пространства — один физический пиксель) и один физический пиксель в мировом пространстве (`VectraSvgRenderer`). Свойство не наследуется (SVG 2; Chrome рисует так же): ставьте его на элемент, в его `style` или в CSS-правило; `<use>` на помеченный элемент не зависит от масштаба, а `<g>` или `<use>`, на которых оно стоит, — нет. `non-scaling-size`, `non-rotation` и `fixed-position` игнорируются и попадают в отчёт импорта. Пока нет в UI Toolkit (`VectraSvgElement`), в запечённых спрайтах и PNG и в экспорте SVG.

**Недоверенные файлы.** Парсер останавливается на лимитах, а не падает и не зависает: вложенность 256, 200 000 элементов, 200 000 нарисованных элементов (каждая копия `<use>` считается), 10 000 копий `<use>`, 1 000 000 символов данных пути, 32 млн символов в файле. Файл, упёршийся в лимит, сохраняет прочитанное и получает предупреждение, начинающееся с «SVG limit reached». Лимиты меняет `SvgParser.Parse(text, new SvgParseOptions { ... })`.

## Мир и Shader Graph

`VectraRenderer` рисует фигуру через `MeshRenderer`. Шейдер выбирается по конвейеру рендера: **Vectra/World URP** (URP, с проходом
и для 2D Renderer), **Vectra/World HDRP** и **Vectra/World** для Built-in (и любого другого конвейера). Шейдеры URP и HDRP держат
все свойства в `UnityPerMaterial`, а данные фигуры в вершинах, поэтому **SRP Batcher** собирает все фигуры с общим материалом в один
батч; фигура не использует `MaterialPropertyBlock`. Фигура с текстурной заливкой получает свой материал на текстуру, общий для всех
фигур с этой текстурой. Совместимость шейдера видна в инспекторе (SRP Batcher: compatible) и в `Window → Analysis → Frame Debugger`.

**Shader Graph (URP и HDRP).** Собирайте свои материалы с полями расстояния, сглаживанием и paint из Vectra. В пакете лежит код для
Custom Function (`Runtime/ShaderGraph/VectraShaderGraph.hlsl`) и пять Sub Graph в *Create Node → Vectra*:

| Sub Graph | Что делает |
|---|---|
| Vectra SDF Shape | Расстояние со знаком до прямоугольника, эллипса, звезды, многоугольника, креста, ромба, суперэллипса или капсулы |
| Vectra Coverage | Сглаженная непрозрачность по расстоянию (фильтр края Vectra) с мягкостью |
| Vectra Outline | Непрозрачность контура: по центру, внутри или снаружи; ширина в единицах или в **пикселях экрана** (hairline) |
| Vectra Pixel Size | Размер пикселя экрана в единицах расстояния |
| Vectra Paint | Paint solid, linear, radial, angular, diamond, stripes, checker, dots, grid и noise по UV |

Они используют экранные производные, поэтому работают только на стадии фрагмента. Импортируйте sample **Shader Graph** с *Vectra Card*:
unlit-граф с прозрачностью (цели URP и HDRP), который из этих функций делает скруглённую карточку с градиентом и hairline-контуром
шириной 2 пикселя.

## Векторные спрайты

Запеките фигуру или SVG в `Sprite` для `SpriteRenderer` или uGUI `Image`: текстуру рисует GPU теми же шейдерами, что и на экране,
меш плотно облегает непрозрачные пиксели (tight), задан pivot, а у рамки может быть 9-slice border из радиусов углов.
Прозрачные текселы сохраняют цвет соседей, поэтому фильтрация не даёт тёмной каймы.

```csharp
var options = VectraSpriteOptions.Default;
options.resolution = 256;          // длинная сторона в пикселях
options.pixelsPerUnit = 100f;      // 0 = столько же единиц, сколько у фигуры
options.autoBorder = true;         // 9-slice border из радиусов углов прямоугольника
Sprite sprite = VectraSpriteBaker.Bake(shape, options);   // VectraShape, VectraRenderer, VectraSvgDocument или content + size
```

В редакторе: правый клик по фигуре Vectra → **Bake to Sprite Asset…** или **Bake to 9-Slice Sprite Asset…** (PNG с настройками
импорта спрайта), либо выберите `.svg` → **Assets → Vectra → Bake SVG to Sprite**. SVG можно импортировать сразу как Sprite: у
**Vectra Svg Importer** в режиме *Sprite* есть pivot, 9-slice border, tight mesh и mipmaps. Спрайт увеличивается под тени и свечение
(`includeOverflow`). Tight-меш строит Unity, поэтому его контур приблизителен; точный коллайдер даёт опция
**Generate Collider** у `VectraRenderer` или `VectraOutline.GetOutline`.

## Анимация

**Твины** работают без зависимостей и останавливаются сами, когда цель уничтожена:

```csharp
shape.TweenTrim(0f, 1f, 0.6f).SetEase(VectraEase.OutCubic).OnComplete(Next);
shape.TweenFillColor(Color.red, 0.3f);
shape.TweenRotation(360f, 1f).SetEase(VectraEase.Linear).SetLoops(-1);
shape.TweenFillAmount(0.75f, 0.5f);        // добавит FillAmountEffect, если его нет
shape.TweenMorph(1f, 1f).SetLoops(-1, true);
shape.TweenFloat(0f, 1f, 1f, v => shape.Strokes[0].dashOffset = v * 40f);
shape.KillTweens();
```

Твины работают для `VectraShape` и `VectraRenderer` (интерфейс `IVectraShape`).
- **Кривые:** Linear, In/Out/InOut для Quad и Cubic, InOutSine, OutBack, OutElastic, OutBounce.
- **Опции:** `SetDelay`, `SetLoops(count, yoyo)`, `SetUnscaledTime`, `Kill`.

**Animator и Timeline.** Списки слоёв и полиморфные эффекты нельзя ключевать напрямую. Поэтому:
1. Добавьте **Vectra Animated Properties**.
2. Включите нужные параметры: цвет и прозрачность заливки, цвет и ширина обводки, trim, dash offset, rotation, fill amount, dissolve, morph.
3. Ключуйте его поля в окне Animation или на анимационной дорожке Timeline.

## Состояния взаимодействия

**Vectra Interaction States** задаёт фигуре вид для состояний Normal, Hover, Pressed, Selected (фокус клавиатуры или геймпада) и Disabled: оттенок, масштаб, цвета заливки и обводки, силу теней и свечения. Переходы между состояниями плавные, с выбранной кривой.
- Компонент реагирует на события указателя и выделения.
- Selectable на том же объекте (Button, Toggle…) включает Disabled.
- `SetState` задаёт состояние из кода, `StateChanged` сообщает о смене.
- Работает только в Play Mode.

## URP backdrop

Настройка:
1. Добавьте **Vectra Backdrop Feature** в ассет Universal Renderer (Add Renderer Feature).
2. Добавьте **Vectra Backdrop (URP)** на `VectraShape` и выберите режим: Frosted Glass или blend-режим (Overlay, Soft Light, Hard Light, Color Dodge/Burn, Difference, Exclusion, Hue, Saturation, Color, Luminosity, Darken, Lighten, Linear Burn/Dodge).

Фича захватывает изображение камеры **до прозрачных объектов**, один раз на камеру, и размывает его. Значит, в backdrop попадает непрозрачная часть сцены.

Чтобы размывать другой UI, нужен стек камер:
1. Фоновый UI рисует базовая камера.
2. Стекло рисует overlay-камера из её стека.
3. Оба канваса в режиме Screen Space - Camera.

Тогда захват overlay-камеры содержит готовый фон. Сэмпл *URP Backdrop* устроен именно так.

Backdrop-фигуры используют собственный материал.

## Инструменты редактора

**Инспектор:**
- сетка фигур;
- карточки заливок, обводок и эффектов: видимость, перетаскивание за «⋯», дублирование, удаление;
- редактор градиентов с пресетами;
- выбор цвета палитры;
- стили (Save / Detach);
- статистика: вершины, размер поля расстояний, время сборки.

**Scene view:** инструмент **Edit Vectra Shape** редактирует:
- точки и тангенсы пути: «+» на сегменте добавляет точку, Ctrl/Cmd+клик удаляет;
- точки полилинии;
- радиусы углов (Shift — один угол);
- углы дуги;
- ручки градиента;
- смещение тени.

Его оверлей переключает выбранную точку между Corner, Smooth и Symmetric, включает привязку к сетке и показывает или скрывает группы ручек.

**Контекстное меню:**
- Copy / Paste Vectra Style, в том числе между ассетами VectraStyle;
- Export as PNG / SVG, Bake to Sprite Asset / 9-Slice Sprite Asset;
- Expand to Hierarchy (для SVG Image).

**Окна:**
- **Window → Vectra → Atlas Debugger** — текстуры атласов, заполненность, регионы, сброс;
- **Window → Vectra → SVG Import Report**.

**Project Settings → Vectra:** импортёр SVG, качество по умолчанию, размер атласа расстояний, font map по умолчанию.

## Командная строка

Если установлен пакет `com.unity.pipeline`, опциональная сборка `FirstSymbol.Vectra.Editor.Cli` регистрирует команды `unity command vectra …` для скриптов, CI и AI-агентов. Каждая команда отвечает JSON, правки отменяются через Undo.

- **Обзор:** `help`, `types` (все фигуры, эффекты и paint со значениями по умолчанию), `list`, `get --target` (`--full true` — все поля).
- **Правка:** `create`, `set --data <JSON-патч>`, `add`, `remove`, `mask`, `states`, `convert_to_path`, `save_style`, `save_shape`, `palette`, `theme`, `svg_parse` и `svg_create` (у обеих есть `--text_engine`), `select`.
- **Вывод:** `export_png` (визуальная проверка результата), `export_svg`.

```bash
unity command vectra create --shape Rectangle --name Card --size 320,180 --data "{effects:[{$type:DropShadow,blur:24}]}"
unity command vectra set --target Card --data "{fills:[{paint:{type:LinearGradient,gradient:'#6366F1,#EC4899'}}]}"
unity command vectra export_png --target Card --output card.png
```

**Claude Code.** Пункт **Tools → Vectra → Install Claude Code Skill** копирует `Documentation~/AgentGuide.md` в `.claude/skills/vectra/SKILL.md`. После этого агент знает команды, формат JSON и готовые рецепты.

Ещё примеры:

```
unity command vectra stats
unity command vectra reset_atlases
unity command vectra use_importer --path Assets/Icons
unity command vectra export_png --target "Canvas/Logo" --output Logo.png --scale 2
unity command vectra export_svg --target Logo --output Logo.svg
unity command vectra expand_svg --target "SVG Image"
```

## Производительность

- По возможности оставляйте фигуры аналитическими. Path-фигуры с эффектами запекают поле расстояний, его размер задаёт Quality.
- Фигуры пересобираются только при изменениях. Анимируемые параметры пересобираются каждый кадр без управляемых аллокаций.
- Текстурные заливки, backdrop-фигуры и дети soft-маски используют отдельные материалы, но батчатся между собой.
- Мировые фигуры в URP и HDRP используют шейдеры, совместимые с SRP Batcher (у шейдера 0.5.3 такой совместимости не было). Замер: 500 фигур стоят столько же CPU, сколько раньше, в плеере Windows IL2CPP (см. [Platforms.ru.md](Platforms.ru.md)); изменение в том, что теперь они проходят в батчер (нет property block на фигуру, общие батчи с другими совместимыми объектами), а не ускоренный кадр на таком размере.
- В UI Toolkit фигура растеризуется в общую страницу только при изменении; маленькие элементы делят страницы, поэтому их квады батчатся (см. [UI Toolkit](#ui-toolkit)).
- Лимит SDF-атласа задаётся в Project Settings и попадает в сборки (сборка добавляет в Preloaded Assets небольшой ассет настроек и потом убирает его), заполненность видна в Atlas Debugger. При изменении в видеопамять уходят только изменившиеся блоки атласа, а не вся текстура.
- Атлас градиентов (ширина 256, до 4096 строк, не больше 8 МиБ в half) отдаёт одну строку заливкам с одинаковыми цветами и стопами. Строки сравниваются по содержимому, поэтому две разные заливки никогда не окажутся на одной строке. Строка, которой больше никто не пользуется, остаётся запечённой; когда атлас полон, вытесняются наименее недавно использованные. Если одновременно живы 4095 разных градиентов, остальные рисуются белыми, а консоль один раз предупреждает.
- Что собрано и запущено на каких платформах: [Platforms.ru.md](Platforms.ru.md).

## Частые проблемы

| Симптом | Решение |
|---|---|
| Фигура не видна в Canvas | Канвасу нужны шейдерные каналы TexCoord1–3. Они добавляются автоматически, когда фигура включается под канвасом |
| Backdrop-фигуры рисуются без эффекта | Добавьте Vectra Backdrop Feature в активный URP Renderer |
| Стекло не размывает другой UI | Фоновый UI рисуйте базовой камерой, стекло — overlay-камерой |
| Нет текста из SVG | Проверьте **Project Settings → Vectra → Text engine**. Для TextMesh Pro импортируйте TMP Essential Resources или назначьте шрифт / font map; Lettera нужен как пакет; если движок пришлось заменить, консоль один раз предупреждает |
| Элемент Vectra в UI Toolkit ничего не показывает | Ему нужны размер (width и height) и панель; для текста SVG у панели ещё должна быть тема (theme style sheet) со шрифтом |
| `.svg` импортируется другим импортёром | Выберите Vectra Svg Importer на файле или включите его в Project Settings |
