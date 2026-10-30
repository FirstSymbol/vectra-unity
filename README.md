# Vectra — векторная графика для Unity

Векторные фигуры для **uGUI Canvas** и **world space**. Включает:
- аналитические SDF-примитивы и произвольные пути;
- булевы операции, в том числе плавные;
- градиенты, паттерны, обводки с dash и trim;
- тени, свечение, bevel и другие эффекты;
- маски, палитры с темами, анимацию и состояния взаимодействия;
- фигуры из спрайтов и общую библиотеку фигур;
- импорт и экспорт SVG.

Всё рисуется одним общим материалом: фигуры батчатся. Геометрия считается на Burst без GC-аллокаций при пересборке.

- **Unity:** 6000.0+.
- **Зависимости:** Burst, Collections, Mathematics, uGUI (с TextMeshPro).
- **Пайплайны:** Built-in, URP, HDRP. Backdrop-эффекты работают только в URP.
- **Платформы:** Windows, macOS, Linux, WebGL, WebGPU, iOS, Android — всё с Shader Model 3.5. Compute-шейдеры не используются. На WebGL Burst-код выполняется как обычный IL2CPP.

Подробное руководство: [Documentation~/Vectra.ru.md](Documentation~/Vectra.ru.md), на английском — [Documentation~/Vectra.md](Documentation~/Vectra.md).

## Установка

Package Manager → **+** → **Add package from git URL...**

```
https://github.com/FirstSymbol/vectra-unity.git
```

Конкретная версия ставится через тег: `https://github.com/FirstSymbol/vectra-unity.git#0.2.1`.

## Быстрый старт

- **GameObject → UI → Vectra → …** — фигура на канвасе (канвас создаётся автоматически).
- **GameObject → 2D Object → Vectra → …** — фигура в мире (`VectraRenderer`).
- Перетащите `.svg` в проект. Чтобы его обрабатывал Vectra, выберите импортёр **Vectra Svg Importer** в инспекторе ассета или включите **Project Settings → Vectra → Use Vectra SVG importer**. Затем создайте **GameObject → UI → Vectra → SVG Image**.
- **Сэмплы** (Package Manager → Vectra → Samples):
  - Shapes Gallery;
  - SVG Showcase;
  - Effects & Masks;
  - Boolean Operations;
  - Animation;
  - World Space;
  - URP Backdrop.

## Компоненты

| Компонент | Назначение |
|---|---|
| `VectraShape` | Фигура uGUI (`MaskableGraphic`). Работает с Mask, RectMask2D (с softness), layout, точным raycast. |
| `VectraRenderer` | Фигура в мире: MeshRenderer, sorting layer и order, опциональный `PolygonCollider2D`. |
| `VectraSvgImage` / `VectraSvgRenderer` | SVG-документ одним мешем. Текст рисуется через TextMeshPro, встроенные картинки — через RawImage. Есть override цвета по `#id`, `.class` и тегу, а также разворачивание в иерархию фигур. |
| `VectraBooleanGroup` (+ `VectraBooleanOperand`) | Дочерние фигуры становятся операндами булевой операции. Порядок задаётся иерархией. |
| `VectraMask` | Маска по контуру фигуры. Stencil — для любого UI. Soft — с пером и инверсией, для дочерних фигур Vectra. |
| `VectraTheme` (+ ассет `VectraPalette`) | Именованные цвета по темам. Смена темы перекрашивает все фигуры с токенами палитры. |
| `VectraAnimatedProperties` | Поля для ключей Animator и Timeline: trim, цвета, ширина, rotation, fill amount, dissolve, morph. |
| `VectraInteractionStates` | Hover, pressed, selected (фокус) и disabled: оттенок, масштаб, цвета, сила эффектов, плавные переходы. |
| `VectraShapeAsset` (ассет) | Общая геометрия для многих объектов: библиотека фигур. |
| `VectraBackdrop` (URP) | Frosted glass и 15 сложных blend-режимов. Нужен `VectraBackdropFeature` в URP Renderer. |
| `VectraStyle` (ассет) | Общий набор заливок, обводок и эффектов, аналог CSS-класса. |

## Фигуры

**Аналитические:** точная функция расстояния, идеальный AA на любом масштабе, эффекты почти бесплатны.
- **Прямоугольник:** свой радиус на каждый угол; стиль угла Round, Chamfer, Inverted, Notch или Squircle.
- **Базовые:** эллипс, кольцо, дуга, сектор, многоугольник, звезда, треугольник, линия.
- **Прочие:** крест, сердце, ромб, трапеция, параллелограмм, полумесяц, яйцо, vesica, суперэллипс, цветок, стрелка, шеврон.

**Path-фигуры:**
- **Контуры:** Bezier-путь (редактируется в Scene view; точки Corner, Smooth, Symmetric), полилиния со скруглением углов, спираль, шестерня, волна, SVG-путь (`d`), облачко с хвостиком, галочка.
- **Составные:** булева группа (exact или smooth), морфинг между любыми двумя фигурами.
- **Из спрайтов:** силуэт спрайта как редактируемый контур (порог альфы, разрешение, упрощение, сглаживание).

Любую фигуру, SVG-путь или трассированный спрайт можно превратить в редактируемый Bezier-путь (**Convert to editable path**).

У каждой фигуры есть `rotation`. Аналитическую фигуру всегда можно отрисовать и как путь: флаг `Force Path` или автоматически, когда это нужно для dash-обводки.

## Заливки, обводки, эффекты

- **Paint:**
  - solid (HDR);
  - градиенты linear, radial (с focal point), angular, diamond: spread pad/repeat/reflect, без лимита стопов, midpoint, интерполяция sRGB/Linear/OKLab/Step, dithering против бэндинга;
  - текстура (stretch, fit, fill, tile);
  - паттерны stripes, checker, dots, grid;
  - шум;
  - freeform-градиент по цветовым точкам;
  - contour gradient: градиент и полосы, повторяющие форму (затухание к краю, кольца).

  Цвет можно брать из палитры по имени.
- **Обводки:** любое количество; align center/inside/outside; caps; joins с miter limit; dash (абсолютные или относительные длины, offset); trim path; профиль ширины `AnimationCurve`; режим «за заливкой».
- **Эффекты:**
  - drop shadow, inner shadow;
  - outer glow, inner glow;
  - bevel & emboss, layer blur, noise/grain;
  - color adjust (HSBC);
  - fill amount (horizontal/vertical/radial), dissolve, pixelate;
  - outline (по полю расстояний);
  - color overlay, gradient overlay;
  - extrude (слои силуэта);
  - deform (анимированная деформация на GPU).

  Свои эффекты пишутся наследованием от `VectraCustomEffect`.

  У каждого слоя есть blend Normal или Additive. Для всей фигуры доступны Normal, Additive, Multiply и Screen.

## Код

```csharp
var shape = gameObject.AddComponent<VectraShape>();
var star = shape.SetGeometry<StarShape>();
star.points = 6;
shape.SetFill(VectraPaint.Linear(Color.yellow, Color.red, 90f))
     .SetStroke(Color.white, 3f, StrokeAlignment.Outside);
shape.AddEffect<DropShadowEffect>().blur = 12f;

// анимация твинами
shape.TweenTrim(0f, 1f, 0.6f).SetEase(VectraEase.OutCubic).SetLoops(-1, true);

// или вручную: изменить параметры и пометить фигуру грязной
shape.Strokes[0].trimEnd = Mathf.PingPong(Time.time, 1f);
shape.SetShapeDirty();

// палитра: смена темы перекрашивает фигуры с токенами
shape.Fills[0].paint.colorToken = "Accent";
theme.SetTheme("Dark");

// SVG в рантайме
svgImage.Document = VectraSvg.Parse(svgText);
VectraSvg.Expand(svgImage);   // отдельный VectraShape на каждый элемент
```

Утилиты:
- `VectraRasterizer.Render(...)` — фигура или SVG в `Texture2D`.
- `SvgWriter.Write(...)` — экспорт в SVG.
- `VectraOutline.GetOutline(...)` — контуры для коллайдеров.
- `VectraGeometry` — низкоуровневый Burst API: flatten, tessellate (fill rules и N-операндные булевы операции), stroke, SDF, contours.

## Редактор

- **Инспектор:**
  - сетка типов фигур (зелёные — аналитические, оранжевые — path);
  - карточки заливок, обводок и эффектов: видимость, перетаскивание за «⋯», удаление;
  - редактор градиентов (клик по полосе добавляет стоп, есть пресеты);
  - выбор цвета палитры;
  - стили (Save/Detach) и статистика меша.
- **Scene view:** инструмент **Edit Vectra Shape**. Им редактируются:
  - точки и тангенсы пути: точку добавляет «+» в середине сегмента, удаляет Ctrl+клик;
  - точки полилинии;
  - радиусы углов (Shift — один угол);
  - углы дуги;
  - ручки градиента и смещение тени.

  Оверлей инструмента переключает режим точки, включает привязку к сетке и ручки.
- **Контекстное меню:** Copy/Paste Vectra Style, Export as PNG / SVG / Sprite Asset, Expand to Hierarchy (SVG Image).
- **Окна:** Window → Vectra → Atlas Debugger и SVG Import Report.
- **CLI для скриптов и AI-агентов** (при установленном `com.unity.pipeline`): `unity command vectra help | types | list | get | create | set | add | remove | mask | states | palette | theme | svg_parse | export_png …`.
  - Вход и выход — JSON, правки отменяются через Undo.
  - **Tools → Vectra → Install Claude Code Skill** ставит в проект skill с инструкцией для Claude Code.

## URP backdrop

Добавьте **Vectra Backdrop Feature** в ассет URP Renderer. Захват идёт до прозрачных объектов. Поэтому стекло поверх другого UI делается стеком камер: фон рисует базовая камера, а стекло — overlay-камера. Так устроен сэмпл URP Backdrop.

## Ограничения SVG

- `<mask>` применяется как геометрический клип, яркость маски не учитывается.
- Паттерн-заливки заменяются цветом первой фигуры паттерна.
- Из фильтров поддерживаются blur, drop shadow (включая цепочку offset + blur + merge), saturate и hueRotate.
- Внешние картинки по URL не загружаются, работают только встроенные data-URI.
- Обо всём неподдержанном пишется в отчёт импорта (инспектор ассета и Window → Vectra → SVG Import Report).

## Производительность

- Пересборка происходит только по изменению. Тесселяция, обводки и SDF кешируются по хешам.
- Атласы градиентов (RGBAHalf) и SDF (RHalf) общие для всех фигур, поэтому у всех один материал.
- Запечённые SDF есть только у path-фигур с эффектами и у soft-масок. Аналитическим фигурам текстуры не нужны.
- Размер SDF-атласа задаётся в Project Settings → Vectra, заполненность видна в Atlas Debugger.
