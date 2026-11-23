using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using GalgameManager.Contracts.Services;
using GalgameManager.Contracts.ViewModels;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using KnowledgePicker.WordCloud;
using KnowledgePicker.WordCloud.Coloring;
using KnowledgePicker.WordCloud.Drawing;
using KnowledgePicker.WordCloud.Layouts;
using KnowledgePicker.WordCloud.Primitives;
using KnowledgePicker.WordCloud.Sizers;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using Windows.ApplicationModel.DataTransfer;

namespace GalgameManager.ViewModels;

public partial class ReportSubPage2ViewModel(
    ILocalSettingsService localSettingsService,
    IGalgameCollectionService gameService) : ObservableObject, INavigationAware
{
    public readonly List<AnnualReportTagScope> TagScopes = Enum.GetValues<AnnualReportTagScope>().ToList();

    private Dictionary<string, int> wordFrequencies = new();
    private DispatcherTimer tipCloseTimer = null!;
    private DispatcherTimer copiedTipCloseTimer = null!;
    private AnnualReportData _annualReportData = null!;
    private bool _dataReady; // 是否已拿到年度报告数据（首帧由 OnNavigatedTo 统一生成，避免重复计算）

    [ObservableProperty]
    private AnnualReportTagScope _tagScope;

    [ObservableProperty]
    private bool isWordTipOpen;

    [ObservableProperty]
    private FrameworkElement? wordTipTarget;

    [ObservableProperty]
    private TextBlock wordTipContent = new();

    [ObservableProperty]
    private Canvas? wordCloudCanvas;

    public void OnNavigatedTo(object parameter)
    {
        Debug.Assert(parameter is AnnualReportData);
        _annualReportData = (AnnualReportData)parameter;
        InitializeTimer();
        TagScope = localSettingsService.ReadSettingAsync<AnnualReportTagScope>(KeyValues.AnnualReportTagScope).Result;
        _dataReady = true;
        UpdateWordCloud();
    }

    public void OnNavigatedFrom()
    {
        _dataReady = false;
    }

    partial void OnTagScopeChanged(AnnualReportTagScope value)
    {
        localSettingsService.SaveSettingAsync(KeyValues.AnnualReportTagScope, value);
        if (_dataReady) UpdateWordCloud();
    }

    /// 按当前口径重新统计标签并重绘词云
    private void UpdateWordCloud()
    {
        wordFrequencies = AnnualReportStats.CountTags(gameService.Galgames, _annualReportData.Year, TagScope);
        GenerateWordCloud();
    }

    private void InitializeTimer()
    {
        // 悬停移出后延迟关闭：鼠标在词与词之间移动时提示不该闪
        tipCloseTimer = new DispatcherTimer();
        tipCloseTimer.Interval = TimeSpan.FromMilliseconds(200);
        tipCloseTimer.Tick += (s, e) =>
        {
            IsWordTipOpen = false;
            tipCloseTimer.Stop();
        };

        // 点击复制后的「已复制」提示，1 秒后自动收起
        copiedTipCloseTimer = new DispatcherTimer();
        copiedTipCloseTimer.Interval = TimeSpan.FromSeconds(1);
        copiedTipCloseTimer.Tick += (s, e) =>
        {
            IsWordTipOpen = false;
            copiedTipCloseTimer.Stop();
        };
    }

    private void GenerateWordCloud()
    {
        if (WordCloudCanvas == null) return;

        WordCloudCanvas.Children.Clear();

        var wordCloud = new WordCloudInput(
            wordFrequencies.Select(p => new WordCloudEntry(p.Key, p.Value)))
        {
            Width = 1200,
            Height = 600,
            MinFontSize = 16,
            MaxFontSize = 64
        };

        var sizer = new LogSizer(wordCloud);
        var typeface = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyle.Normal);
        using var engine = new SkGraphicEngine(sizer, wordCloud, typeface);
        var layout = new SpiralLayout(wordCloud);
        var colorizer = new RandomColorizer();
        var wcg = new WordCloudGenerator<SKBitmap>(wordCloud, engine, layout, colorizer);

        foreach (var (item, fontSize) in wcg.Arrange())
        {
            var textBlock = new TextBlock
            {
                Text = item.Entry.Word,
                FontSize = fontSize,
                Foreground = new SolidColorBrush(GetWinUIColor(colorizer.GetColor(item))),
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Microsoft YaHei")
            };

            Canvas.SetLeft(textBlock, item.Location.X);
            Canvas.SetTop(textBlock, item.Location.Y);

            textBlock.PointerEntered += (s, args) =>
            {
                tipCloseTimer.Stop();
                copiedTipCloseTimer.Stop();
                WordTipContent.Text = $"关键词：{item.Entry.Word}\n相关游戏数量：{item.Entry.Count}";
                WordTipTarget = textBlock;
                IsWordTipOpen = true;
            };

            textBlock.PointerExited += (s, args) =>
            {
                tipCloseTimer.Start();
            };

            // 点击复制词语。复用悬停提示原地显示「已复制」，不另弹 InfoBar；
            // 每个词本来就是独立的 TextBlock，无需另建命中检测。
            textBlock.PointerPressed += (s, args) =>
            {
                DataPackage dataPackage = new();
                dataPackage.SetText(item.Entry.Word);
                Clipboard.SetContent(dataPackage);
                tipCloseTimer.Stop();
                copiedTipCloseTimer.Stop();
                WordTipContent.Text = "AnnualReportPage_WordCopied".GetLocalized(item.Entry.Word);
                WordTipTarget = textBlock;
                IsWordTipOpen = true;
                copiedTipCloseTimer.Start();
            };

            WordCloudCanvas.Children.Add(textBlock);
        }
    }

    private Windows.UI.Color GetWinUIColor(System.Drawing.Color? color) =>
        color.HasValue ? Windows.UI.Color.FromArgb(color.Value.A, color.Value.R, color.Value.G, color.Value.B)
                      : Colors.Black;
}
