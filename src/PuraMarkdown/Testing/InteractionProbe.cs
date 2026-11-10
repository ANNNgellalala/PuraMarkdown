using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace PuraMarkdown.Testing;

/// <summary>交互探针：在无头环境中查找控件并触发点击/选中。</summary>
public static class InteractionProbe
{
    /// <summary>查找唯一匹配的控件。</summary>
    /// <typeparam name="T">目标控件类型。</typeparam>
    /// <param name="root">根控件。</param>
    /// <param name="predicate">可选的过滤条件。</param>
    /// <returns>匹配的控件。</returns>
    public static T Single<T>(Control root, Func<T, bool>? predicate = null) where T : Control
        => ControlTreeDumper.Find<T>(root).Single(predicate ?? (_ => true));

    /// <summary>查找所有匹配的控件。</summary>
    /// <typeparam name="T">目标控件类型。</typeparam>
    /// <param name="root">根控件。</param>
    /// <returns>匹配的控件序列。</returns>
    public static IEnumerable<T> All<T>(Control root) where T : Control
        => ControlTreeDumper.Find<T>(root);

    /// <summary>Click。</summary>
    /// <param name="button">button 参数。</param>
    public static void Click(Button button)
        => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>Select。</summary>
    /// <param name="toggle">toggle 参数。</param>
    public static void Select(ToggleButton toggle)
        => toggle.IsChecked = true;
}
