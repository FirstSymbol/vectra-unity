# Vectra — векторная графика для Unity

Vectra рисует векторные фигуры в **uGUI Canvas** и в **world space**. Поддерживаются:
- аналитические примитивы и произвольные пути;
- булевы операции, градиенты, обводки, эффекты и маски;
- SVG-документы.

Все фигуры используют один материал, поэтому интерфейс из фигур Vectra батчится в несколько draw call. Пересборка геометрии не выделяет управляемую память.

*English version: [Vectra.md](Vectra.md).*

- **Unity:** 6000.0 и новее.
- **Пайплайны:** Built-in, URP, HDRP. Backdrop-эффекты работают только в URP.
- **Зависимости:** Burst, Collections, Mathematics, uGUI. TextMeshPro нужен для текста из SVG.
- **Платформы:** Shader Model 3.5 и выше — Windows, macOS, Linux, WebGL 2, WebGPU, iOS, Android. Compute-шейдеры не используются. На WebGL Burst-джобы выполняются как код IL2CPP.

## Содержание

1. [Установка](#установка)
2. [Как это устроено](#как-это-устроено)
3. [Компоненты](#компоненты)
4. [Фигуры](#фигуры)
5. [Заливки, обводки и палитры](#заливки-обводки-и-палитры)
6. [Эффекты](#эффекты)
7. [Маски](#маски)
8. [Булевы операции](#булевы-операции)
9. [SVG](#svg)
10. [Анимация](#анимация)
11. [URP backdrop](#urp-backdrop)
12. [Инструменты редактора](#инструменты-редактора)
13. [Командная строка](#командная-строка)
14. [Производительность](#производительность)
15. [Частые проблемы](#частые-проблемы)

## Установка

Package Manager → **+** → **Add package from git URL…** → `https://github.com/FirstSymbol/vectra-unity.git`. Чтобы поставить конкретную версию, добавьте к адресу `#0.2.0`.

Сэмплы находятся в **Package Manager → Vectra Vector Graphics → Samples**.

| Сэмпл | Что показывает |
|---|---|
| Shapes Gallery | Все примитивы и генераторы путей, заливки, анимированный trim, dash, fill amount, морфинг |
| SVG Showcase | SVG в Canvas и в мире, переопределения по элементам |
| Effects & Masks | Все эффекты; soft, инвертированная и stencil-маски; палитра с переключением темы |
| Boolean Operations | Группы с движущимися операндами, smooth union (метаболы), BooleanShape из кода |
| Animation | Твины, AnimationClip через Animated Properties, морфинг, бегущий пунктир |
| World Space | Карточки VectraRenderer в 3D, сгенерированные коллайдеры, обработка кликов |
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
| `VectraBooleanGroup` + `VectraBooleanOperand` | Дочерние фигуры становятся операндами булевой операции |
| `VectraMask` | Маскирует детей по контуру фигуры: stencil или soft (с пером, с инверсией) |
| `VectraTheme` | Делает `VectraPalette` активной и выбирает тему |
| `VectraAnimatedProperties` | Поля для Animator и Timeline (trim, цвета, fill amount и др.) |
| `VectraBackdrop` (URP) | Frosted glass и сложные blend-режимы |

Ассеты:
- `VectraStyle` — общий набор заливок, обводок и эффектов, как CSS-класс;
- `VectraPalette` — именованные цвета по темам;
- `VectraSvgDocument` — импортированный SVG;
- `VectraFontMap` — соответствие font-family шрифтам TextMeshPro.

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

## Фигуры

- **Аналитические:**
  - прямоугольник: свой радиус на каждый угол; стиль угла round, chamfer, inverted, notch или squircle;
  - эллипс, дуга, кольцо, сектор, многоугольник, звезда, треугольник, линия;
  - крест, сердце, ромб, трапеция, параллелограмм, полумесяц, яйцо, vesica, суперэллипс, цветок, стрелка, шеврон.
- **Пути:** путь Безье (редактируется в Scene view), полилиния со скруглением, спираль, шестерня, волна, данные SVG-пути, облачко, галочка.
- **Составные:** булева фигура (точная или плавная), морфинг между двумя фигурами.

У каждой фигуры есть `rotation`. Аналитическую фигуру можно отрисовать как путь флагом **Force Path**. Для dash-обводки это происходит автоматически.

## Заливки, обводки и палитры

**Типы paint:**
- solid (HDR);
- градиенты linear, radial (с фокусом), angular, diamond. Spread: pad, repeat, reflect. Число стопов не ограничено, есть midpoint. Интерполяция sRGB, Linear, OKLab или Step;
- texture (stretch, fit, fill, tile);
- паттерны stripes, checker, dots, grid, noise;
- freeform-градиент.

Градиенты дизерятся против бэндинга.

**Обводки:**
- любое количество на фигуру;
- позиция: по центру, внутри, снаружи;
- caps и joins с miter limit;
- dash с абсолютными или относительными длинами и offset;
- trim path;
- профиль ширины (`AnimationCurve`);
- отрисовка под заливкой или над ней.

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
- **color overlay**, **gradient overlay**.

У каждого слоя и эффекта blend Normal или Additive. Для всей фигуры доступны Normal, Additive, Multiply и Screen.

Одновременно действует только один модификатор из fill amount, pixelate и dissolve; приоритет в этом же порядке.

У path-фигур pixelate и эффекты на основе расстояния используют запечённое поле. Геометрические обводки path-фигур не пикселизуются.

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
- `<text>` через TextMeshPro;
- встроенные data-URI картинки.

**Рантайм-API:**

```csharp
var doc = VectraSvg.Parse(svgText);                 // или VectraSvg.Load(textAsset)
svgImage.Document = doc;
VectraSvg.Download(url, (d, error) => svgImage.Document = d);
```

**Expand to hierarchy** превращает документ в отдельный `VectraShape` на каждый элемент, чтобы их можно было редактировать и анимировать по отдельности. Запуск: кнопка в инспекторе SVG Image, контекстное меню или `VectraSvg.Expand(image)`. Clip-path превращается в операнды булевой операции у залитых элементов или во вложенные stencil-маски у элементов с обводкой.

**Шрифты.** `VectraFontMap` (Assets → Create → Vectra → SVG Font Map) сопоставляет имена `font-family` шрифтам TextMeshPro. Назначается на SVG Image; новые изображения получают шрифт по умолчанию из Project Settings.

**Ограничения:**
- `<mask>` применяется как геометрический клип, яркость маски не учитывается;
- паттерн-заливки заменяются цветом первой фигуры паттерна;
- из фильтров поддерживаются только blur, drop shadow, saturate и hue-rotate;
- внешние картинки по URL не загружаются.

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
- Export as PNG / SVG / Sprite Asset;
- Expand to Hierarchy (для SVG Image).

**Окна:**
- **Window → Vectra → Atlas Debugger** — текстуры атласов, заполненность, регионы, сброс;
- **Window → Vectra → SVG Import Report**.

**Project Settings → Vectra:** импортёр SVG, качество по умолчанию, размер атласа расстояний, font map по умолчанию.

## Командная строка

Если установлен пакет `com.unity.pipeline`, опциональная сборка `FirstSymbol.Vectra.Editor.Cli` регистрирует команды:

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
- Лимит SDF-атласа задаётся в Project Settings, заполненность видна в Atlas Debugger.

## Частые проблемы

| Симптом | Решение |
|---|---|
| Фигура не видна в Canvas | Канвасу нужны шейдерные каналы TexCoord1–3. Они добавляются автоматически, когда фигура включается под канвасом |
| Backdrop-фигуры рисуются без эффекта | Добавьте Vectra Backdrop Feature в активный URP Renderer |
| Стекло не размывает другой UI | Фоновый UI рисуйте базовой камерой, стекло — overlay-камерой |
| Нет текста из SVG | Импортируйте TextMeshPro Essential Resources или назначьте шрифт / font map |
| `.svg` импортируется другим импортёром | Выберите Vectra Svg Importer на файле или включите его в Project Settings |
