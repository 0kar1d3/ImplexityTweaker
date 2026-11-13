using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ImplexityTweaker.Services;

/// <summary>
/// Централизованные анимации интерфейса: плавный вход страниц, каскадное появление
/// карточек, отклик навигации на наведение и мягкое открытие окон.
///
/// Все эффекты уважают:
///  - системную настройку Windows «Показывать анимации в Windows»
///    (<see cref="SystemParameters.ClientAreaAnimation"/> / <see cref="SystemParameters.MenuAnimation"/>);
///  - пользовательский выключатель в разделе «Настройки» (<see cref="Enabled"/>).
///
/// Когда анимации отключены, все методы мгновенно приводят элементы в конечное
/// состояние — интерфейс никогда не остаётся «полупрозрачным».
/// </summary>
public static class UiAnimation
{
    /// <summary>Пользовательский выключатель анимаций (берётся из AppSettings.EnableAnimations).</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>Разрешены ли анимации прямо сейчас: пользователь + системная настройка Windows.</summary>
    public static bool IsActive =>
        Enabled && SystemParameters.ClientAreaAnimation && SystemParameters.MenuAnimation;

    // Стандартные тайминги: короткие, чтобы интерфейс ощущался быстрым, а не «вязким».
    private const int PageDurationMs = 230;
    private const int CardDurationMs = 300;
    private const int CardStaggerMs = 45;
    private const int MaxCardStagger = 8;
    private const int HoverDurationMs = 160;
    private const int PulseDurationMs = 200;
    private const int WindowDurationMs = 240;

    // =====================================================================
    // Вход страницы и карточек
    // =====================================================================

    /// <summary>
    /// Проигрывает вход страницы: лёгкое всплытие самой страницы и каскадное
    /// появление карточек. Вызывается централизованно при навигации,
    /// поэтому не требует правок в XAML страниц.
    /// </summary>
    public static void AnimatePageEntrance(FrameworkElement? page)
    {
        if (page == null)
            return;

        void Run()
        {
            if (!IsActive)
                return;

            FadeSlideIn(page, offsetY: 10, durationMs: PageDurationMs);
            StaggerCards(page, CardDurationMs, CardStaggerMs, MaxCardStagger);
        }

        // Дерево страницы строится асинхронно после Frame.Navigate, поэтому
        // запускаем эффект на Loaded (или сразу, если страница уже загружена).
        if (page.IsLoaded)
            page.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Run));
        else
            page.Loaded += (_, _) => page.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Run));
    }

    /// <summary>Находит все <see cref="Wpf.Ui.Controls.Card"/> и проявляет их по очереди (сверху вниз).</summary>
    public static void StaggerCards(DependencyObject? root, int durationMs = CardDurationMs,
        int staggerMs = CardStaggerMs, int maxStagger = MaxCardStagger)
    {
        if (root == null || !IsActive)
            return;

        var cards = new List<FrameworkElement>();
        CollectCards(root, cards);
        if (cards.Count == 0)
            return;

        for (var i = 0; i < cards.Count; i++)
        {
            // Обход визуального дерева идёт сверху вниз — получаем естественный каскад.
            var delay = Math.Min(i, maxStagger) * staggerMs;
            FadeSlideIn(cards[i], offsetY: 10, durationMs: durationMs, delayMs: delay);
        }
    }

    /// <summary>
    /// Каскадно проявляет уже созданные элементы списка (строки результатов поиска,
    /// пункты выпадающего списка) — то же поведение, что и у карточек страниц,
    /// но для любых элементов, а не только для <see cref="Wpf.Ui.Controls.Card"/>.
    /// </summary>
    public static void StaggerElements(IEnumerable<FrameworkElement?>? elements, double offsetY = 12,
        int durationMs = CardDurationMs, int staggerMs = CardStaggerMs, int maxStagger = MaxCardStagger)
    {
        if (elements == null || !IsActive)
            return;

        var index = 0;

        foreach (var element in elements)
        {
            if (element == null)
                continue;

            FadeSlideIn(element, offsetY: offsetY, durationMs: durationMs,
                delayMs: Math.Min(index, maxStagger) * staggerMs);

            index++;
        }
    }

    private static void CollectCards(DependencyObject root, List<FrameworkElement> result)
    {
        int count;
        try
        {
            count = VisualTreeHelper.GetChildrenCount(root);
        }
        catch
        {
            // Узел ещё не в визуальном дереве — пропускаем ветку.
            return;
        }

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Wpf.Ui.Controls.Card card)
                result.Add(card);

            CollectCards(child, result);
        }
    }

    // =====================================================================
    // Базовые примитивы
    // =====================================================================

    /// <summary>
    /// Проявляет элемент: прозрачность 0→1, смещение по Y вниз→0 и (опционально)
    /// масштаб от <paramref name="fromScale"/> к 1. Безопасно вызывается всегда.
    /// </summary>
    public static void FadeSlideIn(FrameworkElement? element, double offsetY = 16, int durationMs = 260,
        int delayMs = 0, double fromOpacity = 0, double fromScale = 1)
    {
        if (element == null)
            return;

        if (!IsActive)
        {
            // Мгновенно в конечное состояние, чтобы элемент не «залип» прозрачным.
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(durationMs);
        var begin = TimeSpan.FromMilliseconds(Math.Max(0, delayMs));

        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(fromOpacity, 1, duration)
        {
            BeginTime = begin,
            EasingFunction = Ease(EasingMode.EaseOut),
            FillBehavior = FillBehavior.HoldEnd
        });

        if (Math.Abs(offsetY) > 0.01)
        {
            var translate = EnsureTranslate(element);
            translate.BeginAnimation(TranslateTransform.YProperty, Slide(offsetY, 0, duration, begin));
        }

        if (Math.Abs(fromScale - 1.0) > 0.001)
        {
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            var scale = EnsureScale(element);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Slide(fromScale, 1, duration, begin));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Slide(fromScale, 1, duration, begin));
        }
    }

    /// <summary>Плавно сдвигает элемент по X (отклик наведения на кнопки навигации).</summary>
    public static void SlideX(FrameworkElement? element, double to, int durationMs = HoverDurationMs)
    {
        if (element == null || !IsActive)
            return;

        var translate = EnsureTranslate(element);
        translate.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(to, TimeSpan.FromMilliseconds(durationMs))
            {
                EasingFunction = Ease(EasingMode.EaseOut),
                FillBehavior = FillBehavior.HoldEnd
            });
    }

    /// <summary>Лёгкий «pop»: элемент слегка увеличивается до 100% (активная вкладка меню).</summary>
    public static void PulseScale(FrameworkElement? element, double from = 0.97, int durationMs = PulseDurationMs)
    {
        if (element == null || !IsActive)
            return;

        element.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = EnsureScale(element);
        var duration = TimeSpan.FromMilliseconds(durationMs);

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(from, 1, duration)
        {
            EasingFunction = Ease(EasingMode.EaseOut),
            FillBehavior = FillBehavior.HoldEnd
        });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(from, 1, duration)
        {
            EasingFunction = Ease(EasingMode.EaseOut),
            FillBehavior = FillBehavior.HoldEnd
        });
    }

    // =====================================================================
    // Окна
    // =====================================================================

    /// <summary>
    /// Готовит окно к плавному появлению: прячет содержимое ДО первого кадра,
    /// чтобы не было вспышки уже отрисованного интерфейса. Вызывается в конструкторе.
    /// </summary>
    public static void PrepareWindowOpen(Window? window)
    {
        if (!IsActive || window?.Content is not FrameworkElement content)
            return;

        content.Opacity = 0;
    }

    /// <summary>Проявляет содержимое окна: прозрачность 0→1 и (опционально) мягкий рост.</summary>
    public static void AnimateWindowOpen(Window? window, bool withScale = true, int durationMs = WindowDurationMs)
    {
        if (window?.Content is not FrameworkElement content)
            return;

        FadeSlideIn(content, offsetY: 0, durationMs: durationMs, fromScale: withScale ? 0.985 : 1.0);
    }

    // =====================================================================
    // Вспомогательное: анимации и трансформации
    // =====================================================================

    private static IEasingFunction Ease(EasingMode mode) => new CubicEase { EasingMode = mode };

    private static DoubleAnimation Slide(double from, double to, TimeSpan duration, TimeSpan begin) =>
        new(from, to, duration)
        {
            BeginTime = begin,
            EasingFunction = Ease(EasingMode.EaseOut),
            FillBehavior = FillBehavior.HoldEnd
        };

    /// <summary>Возвращает TransformGroup элемента, сохраняя уже установленную трансформацию.</summary>
    private static TransformGroup EnsureGroup(FrameworkElement element)
    {
        if (element.RenderTransform is TransformGroup group)
            return group;

        var newGroup = new TransformGroup();
        var existing = element.RenderTransform;
        if (existing != null && !ReferenceEquals(existing, Transform.Identity))
            newGroup.Children.Add(existing);

        element.RenderTransform = newGroup;
        return newGroup;
    }

    private static TranslateTransform EnsureTranslate(FrameworkElement element)
    {
        var group = EnsureGroup(element);
        foreach (var child in group.Children)
        {
            if (child is TranslateTransform translate)
                return translate;
        }

        var created = new TranslateTransform();
        group.Children.Add(created);
        return created;
    }

    private static ScaleTransform EnsureScale(FrameworkElement element)
    {
        var group = EnsureGroup(element);
        foreach (var child in group.Children)
        {
            if (child is ScaleTransform scale)
                return scale;
        }

        var created = new ScaleTransform(1, 1);
        group.Children.Add(created);
        return created;
    }
}
