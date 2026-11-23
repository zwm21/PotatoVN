using System.Collections.Generic;
using GalgameManager.Helpers;
using Newtonsoft.Json;
using Windows.Graphics;

namespace GalgameManager.Test.Helpers;

/// <summary>
/// WindowPlacementHelper 测试。覆盖正常恢复、最小宽高 clamp、副屏断开越界回退与非法/null 数据兜底。<br/>
/// 另含一次 Newtonsoft 往返：窗口位置是要存进设置数据库的，记录结构体必须能被正确
/// 序列化/反序列化，否则恢复时永远读到 null。
/// </summary>
[TestFixture]
public class WindowPlacementHelperTest
{
    private static readonly RectInt32 Primary = new(0, 0, 1920, 1080);
    private static readonly RectInt32 Secondary = new(1920, 0, 1920, 1080);

    /// <summary>双屏：主屏 [0,1920)，副屏 [1920,3840)</summary>
    private static List<RectInt32> BothScreens => [Primary, Secondary];

    /// <summary>只剩主屏：原先存在副屏上的窗口全部越界</summary>
    private static List<RectInt32> PrimaryOnly => [Primary];

    /// <summary>两块屏之间留了 [1920,2000) 的空隙，用于验证中心点落在空隙里会被判越界</summary>
    private static List<RectInt32> GappedScreens => [Primary, new RectInt32(2000, 0, 1920, 1080)];

    private static readonly WindowPlacement SensibleFallback = new(100, 100, 1200, 800, false);

    [SetUp]
    public void SetUp() => WindowPlacementHelper.Reset();

    [TearDown]
    public void TearDown() => WindowPlacementHelper.Reset();

    [Test]
    public void Sanitize_ShouldKeepSavedPlacement_WhenFullyOnScreen()
    {
        WindowPlacement saved = new(200, 150, 1400, 900, false);

        WindowPlacement result =
            WindowPlacementHelper.Sanitize(saved, SensibleFallback, BothScreens);

        Assert.That(result, Is.EqualTo(saved));
    }

    [Test]
    public void Sanitize_ShouldKeepSavedPlacement_WhenOnSecondaryScreen()
    {
        // 副屏在线时，存在副屏上的位置是完全合法的
        WindowPlacement saved = new(2000, 100, 1400, 900, false);

        WindowPlacement result =
            WindowPlacementHelper.Sanitize(saved, SensibleFallback, BothScreens);

        Assert.That(result, Is.EqualTo(saved));
    }

    [Test]
    public void Sanitize_ShouldClampWidthAndHeight_WhenSavedSizeTooSmall()
    {
        WindowPlacement saved = new(300, 300, 320, 240, false);

        WindowPlacement result =
            WindowPlacementHelper.Sanitize(saved, SensibleFallback, BothScreens);

        Assert.That(result.Width, Is.EqualTo(WindowPlacementHelper.MinWidth));
        Assert.That(result.Height, Is.EqualTo(WindowPlacementHelper.MinHeight));
        // clamp 不该顺带改动位置
        Assert.That(result.X, Is.EqualTo(300));
        Assert.That(result.Y, Is.EqualTo(300));
    }

    [Test]
    public void Sanitize_ShouldFallBack_WhenSecondaryScreenIsGone()
    {
        // 窗口原本在副屏，副屏被拔掉后中心点落到主屏右侧之外
        WindowPlacement saved = new(2000, 100, 1400, 900, false);

        WindowPlacement result =
            WindowPlacementHelper.Sanitize(saved, SensibleFallback, PrimaryOnly);

        Assert.That(result, Is.EqualTo(SensibleFallback));
    }

    [Test]
    public void Sanitize_ShouldFallBack_WhenSavedPlacementIsNull()
    {
        WindowPlacement result =
            WindowPlacementHelper.Sanitize(null, SensibleFallback, BothScreens);

        Assert.That(result, Is.EqualTo(SensibleFallback));
    }

    [Test]
    public void Sanitize_ShouldClampToMinSize_WhenSavedSizeIsZeroOrNegative()
    {
        // 0 或负尺寸一定是坏数据，但 clamp 之后是个合法的 800x600 窗口，
        // 位置又确实在主屏上，所以规整后即可用，不必回退默认值。
        WindowPlacement zero = new(100, 100, 0, 0, false);
        WindowPlacement negative = new(100, 100, -1200, -800, false);

        Assert.That(WindowPlacementHelper.Sanitize(zero, SensibleFallback, BothScreens),
            Is.EqualTo(new WindowPlacement(100, 100, WindowPlacementHelper.MinWidth,
                WindowPlacementHelper.MinHeight, false)));
        Assert.That(WindowPlacementHelper.Sanitize(negative, SensibleFallback, BothScreens),
            Is.EqualTo(new WindowPlacement(100, 100, WindowPlacementHelper.MinWidth,
                WindowPlacementHelper.MinHeight, false)));
    }

    [Test]
    public void Sanitize_ShouldFallBack_WhenSavedPositionIsUnreachable()
    {
        // 位置离谱到任何工作区都装不下（且尺寸也非法）时，整条存档作废
        WindowPlacement saved = new(int.MaxValue, int.MaxValue, -1, -1, false);

        WindowPlacement result =
            WindowPlacementHelper.Sanitize(saved, SensibleFallback, BothScreens);

        Assert.That(result, Is.EqualTo(SensibleFallback));
    }

    [Test]
    public void Sanitize_ShouldKeepClampedValue_WhenNoWorkAreaIsGiven()
    {
        // 拿不到显示器信息时不做越界判定，但不能因此丢掉尺寸 clamp
        WindowPlacement saved = new(5000, 5000, 100, 100, false);

        WindowPlacement result =
            WindowPlacementHelper.Sanitize(saved, SensibleFallback, null);

        Assert.That(result.Width, Is.EqualTo(WindowPlacementHelper.MinWidth));
        Assert.That(result.Height, Is.EqualTo(WindowPlacementHelper.MinHeight));
        Assert.That(result.X, Is.EqualTo(5000));
    }

    [Test]
    public void Sanitize_ShouldPreserveIsMaximizedFlag()
    {
        WindowPlacement saved = new(200, 150, 1400, 900, true);

        WindowPlacement result =
            WindowPlacementHelper.Sanitize(saved, SensibleFallback, BothScreens);

        Assert.That(result.IsMaximized, Is.True);
    }

    [Test]
    public void Sanitize_ShouldFallBackWithDefaultFlag_WhenSavedDataIsUnusable()
    {
        // 回退值自身的 IsMaximized 必须原样带走，否则会把窗口强推成最大化
        WindowPlacement result =
            WindowPlacementHelper.Sanitize(null, SensibleFallback, PrimaryOnly);

        Assert.That(result.IsMaximized, Is.False);
    }

    [Test]
    public void Clamp_ShouldNotTouchAlreadyValidSize()
    {
        WindowPlacement placement = new(10, 20, 1600, 1000, false);

        Assert.That(WindowPlacementHelper.Clamp(placement), Is.EqualTo(placement));
    }

    [Test]
    public void Clamp_ShouldRaiseEachDimensionIndependently()
    {
        // 只矮不窄：宽度够高就绝不能被 clamp 顺手改掉
        WindowPlacement placement = new(0, 0, 1600, 300, false);

        WindowPlacement result = WindowPlacementHelper.Clamp(placement);

        Assert.That(result.Width, Is.EqualTo(1600));
        Assert.That(result.Height, Is.EqualTo(WindowPlacementHelper.MinHeight));
    }

    [Test]
    public void Clamp_ShouldUseTheSameLowerBoundAsMainWindowXaml()
    {
        // 下限必须与 MainWindow.xaml 的 MinWidth/MinHeight 一致：否则恢复出 800 宽的窗口
        // 会被系统按 XAML 的 MinWidth 抬到 960，存档值和实际窗口尺寸就对不上了。
        WindowPlacement result = WindowPlacementHelper.Clamp(new WindowPlacement(0, 0, 100, 100, false));

        Assert.That(result.Width, Is.EqualTo(960));
        Assert.That(result.Height, Is.EqualTo(600));
    }

    [Test]
    public void IsOnAnyWorkArea_ShouldBeTrue_WhenCenterIsOnSecondaryScreen()
    {
        WindowPlacement placement = new(2000, 100, 1400, 900, false);

        Assert.That(WindowPlacementHelper.IsOnAnyWorkArea(placement, BothScreens), Is.True);
    }

    [Test]
    public void IsOnAnyWorkArea_ShouldBeFalse_WhenCenterIsInTheGapBetweenScreens()
    {
        // 窗口横跨主屏右缘与屏间空隙，中心点 1950 落在 [1920,2000) 的空隙里
        WindowPlacement placement = new(1900, 400, 100, 100, false);

        Assert.That(WindowPlacementHelper.IsOnAnyWorkArea(placement, GappedScreens), Is.False);
    }

    [Test]
    public void IsOnAnyWorkArea_ShouldBeTrue_WhenCenterIsJustInsideLeftEdgeOfGapScreen()
    {
        // 同一个布局下，中心点 2050 落在副屏内，必须算可见
        WindowPlacement placement = new(2000, 400, 100, 100, false);

        Assert.That(WindowPlacementHelper.IsOnAnyWorkArea(placement, GappedScreens), Is.True);
    }

    [Test]
    public void IsOnAnyWorkArea_ShouldBeFalse_WhenWindowIsFarOffScreen()
    {
        WindowPlacement placement = new(-5000, -5000, 1200, 800, false);

        Assert.That(WindowPlacementHelper.IsOnAnyWorkArea(placement, BothScreens), Is.False);
    }

    [Test]
    public void IsOnAnyWorkArea_ShouldBeFalse_WhenWorkAreaListIsEmpty()
    {
        WindowPlacement placement = new(0, 0, 1200, 800, false);

        Assert.That(WindowPlacementHelper.IsOnAnyWorkArea(placement, new List<RectInt32>()), Is.False);
    }

    [Test]
    public void IsOnAnyWorkArea_ShouldTreatRightAndBottomEdgeAsOutside()
    {
        // 右/下边界是开区间：中心点正好压在边界上时不算落在该工作区内
        RectInt32 area = new(0, 0, 1000, 1000);
        WindowPlacement onRightEdge = new(1000, 100, 100, 100, false);   // 中心 x = 1050
        WindowPlacement onBottomEdge = new(100, 1000, 100, 100, false);  // 中心 y = 1050
        List<RectInt32> single = [area];

        Assert.That(WindowPlacementHelper.IsOnAnyWorkArea(onRightEdge, single), Is.False);
        Assert.That(WindowPlacementHelper.IsOnAnyWorkArea(onBottomEdge, single), Is.False);
    }

    [Test]
    public void Track_ShouldUpdateLastRestored_AndResetShouldClearIt()
    {
        Assert.That(WindowPlacementHelper.LastRestored, Is.Null, "初始状态不应有记忆矩形");

        WindowPlacement placement = new(10, 20, 1600, 1000, false);
        WindowPlacementHelper.Track(placement);

        Assert.That(WindowPlacementHelper.LastRestored, Is.EqualTo(placement));

        WindowPlacementHelper.Reset();

        Assert.That(WindowPlacementHelper.LastRestored, Is.Null);
    }

    [Test]
    public void WindowPlacement_ShouldRoundTripThroughJson()
    {
        // 设置数据库用 Newtonsoft 存取窗口位置；记录结构体必须能原样往返
        WindowPlacement original = new(120, 340, 1560, 940, true);

        string json = JsonConvert.SerializeObject(original);
        WindowPlacement? restored = JsonConvert.DeserializeObject<WindowPlacement?>(json);

        Assert.That(restored, Is.EqualTo(original));
    }
}
