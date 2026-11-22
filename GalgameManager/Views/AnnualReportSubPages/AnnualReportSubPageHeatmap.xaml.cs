using GalgameManager.Contracts.Services;
using GalgameManager.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace GalgameManager.Views;

/// <summary>
/// 「足迹」分页：GitHub 贡献图风格的日历热力图 + 当日游玩明细。
/// </summary>
public sealed partial class AnnualReportSubPageHeatmap : Page
{
    public AnnualReportSubPageHeatmapViewModel ViewModel
    {
        get;
    }

    public AnnualReportSubPageHeatmap()
    {
        ViewModel = new AnnualReportSubPageHeatmapViewModel(App.GetService<IGalgameCollectionService>());
        DataContext = ViewModel;
        InitializeComponent();
        ViewModel.HeatmapHost = HeatmapHost;
        ViewModel.WeekdayHost = WeekdayHost;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.OnNavigatedTo(e.Parameter);
    }
}
