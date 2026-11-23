using Windows.Graphics;

namespace GalgameManager.Helpers;

/// <summary>
/// 记忆的主窗口矩形。<br/>
/// X/Y 是窗口左上角的屏幕坐标，Width/Height 是窗口尺寸，三者都以
/// <see cref="AppWindow.Position"/> / <see cref="AppWindow.Size"/> 的口径存取，
/// 因此存与恢复走同一套 API，单位天然对齐。
/// </summary>
/// <param name="X">窗口左上角屏幕横坐标</param>
/// <param name="Y">窗口左上角屏幕纵坐标</param>
/// <param name="Width">窗口宽度</param>
/// <param name="Height">窗口高度</param>
/// <param name="IsMaximized">记忆该矩形时窗口是否处于最大化</param>
public readonly record struct WindowPlacement(int X, int Y, int Width, int Height, bool IsMaximized);

/// <summary>
/// 主窗口位置与尺寸的记忆逻辑。纯静态、无运行期状态依赖，便于单测。<br/>
/// 关键顺序约束：<b>恢复时必须先 MoveAndResize 到普通窗口矩形，最后再 Maximize</b>——
/// 顺序反了会被系统把尺寸覆盖回最大化前的值；<b>保存时只在 Restored 状态下更新矩形</b>，
/// 否则最大化会把普通尺寸冲掉，取消最大化后就回不到原来的大小。
/// </summary>
public static class WindowPlacementHelper
{
    /// <summary>
    /// 窗口最小宽度。必须与 <c>MainWindow.xaml</c> 的 <c>MinWidth</c> 保持一致：
    /// 下限设得比 XAML 小的话，恢复出来的窗口会被系统按 XAML 下限抬回去，
    /// 存档值和实际窗口尺寸就对不上了。
    /// </summary>
    public const int MinWidth = 960;

    /// <summary>窗口最小高度，须与 <c>MainWindow.xaml</c> 的 <c>MinHeight</c> 一致</summary>
    public const int MinHeight = 600;

    /// <summary>
    /// 最近一次「普通（非最大化）」窗口矩形。最大化期间不更新它，
    /// 这样取消最大化后仍能回到进入最大化之前的尺寸与位置。
    /// </summary>
    public static WindowPlacement? LastRestored { get; private set; }

    /// <summary>
    /// 记录一次普通窗口矩形。调用方必须确保只在
    /// <see cref="OverlappedPresenterState.Restored"/> 状态下调用。
    /// </summary>
    public static void Track(WindowPlacement placement)
    {
        LastRestored = placement;
    }

    /// <summary>
    /// 丢弃记忆（用于窗口被销毁、不希望下次启动沿用旧位置的场景）。
    /// </summary>
    public static void Reset()
    {
        LastRestored = null;
    }

    /// <summary>
    /// 把存档值规整为可以安全恢复的矩形：先 clamp 到最小尺寸，再校验矩形中心点
    /// 是否仍落在某个显示器工作区内。任何一步不通过都回退 <paramref name="fallback"/>。
    /// </summary>
    /// <param name="saved">从设置里读出的存档，null 表示从未保存过</param>
    /// <param name="fallback">兜底矩形（通常是主屏工作区减去一定边距）</param>
    /// <param name="workAreas">所有显示器的工作区矩形；传 null 或空集合时跳过越界校验</param>
    public static WindowPlacement Sanitize(WindowPlacement? saved, WindowPlacement fallback,
        IReadOnlyList<RectInt32>? workAreas = null)
    {
        if (saved is not { } placement) return fallback;
        WindowPlacement clamped = Clamp(placement);
        if (workAreas is { Count: > 0 } && !IsOnAnyWorkArea(clamped, workAreas))
            return fallback;
        return clamped;
    }

    /// <summary>
    /// 把宽高 clamp 到最小可用尺寸。窗口被拖到极小、或存档被外部篡改时兜底。
    /// </summary>
    public static WindowPlacement Clamp(WindowPlacement placement) => placement with
    {
        Width = Math.Max(placement.Width, MinWidth),
        Height = Math.Max(placement.Height, MinHeight)
    };

    /// <summary>
    /// 矩形中心点是否落在任一显示器工作区内。<br/>
    /// 副屏被拔掉或关闭时，存放在副屏上的窗口中心点会落到所有工作区之外，
    /// 此时必须回退默认值，否则窗口会开在看不见的地方。
    /// </summary>
    public static bool IsOnAnyWorkArea(WindowPlacement placement, IReadOnlyList<RectInt32> workAreas)
    {
        if (workAreas.Count == 0) return false;
        // 用 long 累加避免极端坐标下 int 溢出
        long centerX = placement.X + placement.Width / 2;
        long centerY = placement.Y + placement.Height / 2;
        return workAreas.Any(area =>
            centerX >= area.X && centerX < area.X + area.Width &&
            centerY >= area.Y && centerY < area.Y + area.Height);
    }
}
