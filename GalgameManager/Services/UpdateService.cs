using Windows.Storage;
using Windows.System;
using GalgameManager.Contracts.Services;
using GalgameManager.Enums;
using GalgameManager.Helpers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using GalgameManager.Models;
using Newtonsoft.Json;

namespace GalgameManager.Services;

public class UpdateService : IUpdateService
{
    // 更新相关常量
    private const string VERSION_CHECK_URL = "https://potatovn.net/version.json";
    private const string STORE_UPDATE_URL = "https://apps.microsoft.com/detail/9p9cbkd5hr3w";
    private const string SIDELOAD_STABLE_DOWNLOAD_URL = "https://download.potatovn.net/release";
    private const string SIDELOAD_BETA_DOWNLOAD_URL = "https://download.potatovn.net/flight-released";

    private readonly ILocalSettingsService _localSettingsService;
    private readonly IInfoService _infoService;

    // 本次启动是否已经更新失败过的标记
    private bool _updateFailedThisSession;
    // 本次启动是否已经取消过更新的标记
    private bool _updateCancelledThisSession;
    // 本次启动是否已经执行过更新的标记
    private bool _updatePerformedThisSession;
    private Version? _targetVersion; //本次启动检查出来的最新版本

    public event Action<bool>? SettingBadgeEvent;

    public UpdateService(ILocalSettingsService localSettingsService, IInfoService infoService)
    {
        _localSettingsService = localSettingsService;
        _infoService = infoService;
    }

    private static async Task<StorageFolder> GetTempRootAsync()
    {
        return await StorageFolder.GetFolderFromPathAsync(AppStoragePaths.TempPath);
    }

    private class VersionJson
    {
        [JsonProperty("released")]
        public string Release { get; set; } = string.Empty;
        [JsonProperty("released-msstore")]
        public string MsStore { get; set; } = string.Empty;
        [JsonProperty("flight-released")]
        public string Beta { get; set; } = string.Empty;
    }

    public async Task<Version?> GetLatestVersionAsync()
    {
        if (_targetVersion is not null) return _targetVersion;
        try
        {
            HttpClient client = Utils.GetDefaultHttpClient();
            HttpResponseMessage response = await client.GetAsync(VERSION_CHECK_URL);
            VersionJson? version = JsonConvert.DeserializeObject<VersionJson>(await response.Content.ReadAsStringAsync());
            if (version is null) throw new PvnException("Version Json is null");
            Version stableVersion = Version.Parse(version.Release);
            Version betaVersion = Version.Parse(version.Beta);
            Version storeVersion = Version.Parse(version.MsStore);
            Version currentVersion = Version.Parse(RuntimeHelper.GetVersion());

            bool needsUpdate; // 判断当前版本是否需要更新
            var isStoreVersion = App.IsStoreVersion();
            var isBetaVersion = await _localSettingsService.ReadSettingAsync<bool>(KeyValues.IsBetaChannel);

            _targetVersion = isBetaVersion ? betaVersion : stableVersion;
            _targetVersion = isStoreVersion ? storeVersion : _targetVersion;
            if (isStoreVersion) // 商店版：只检查稳定版更新
            {
                needsUpdate = currentVersion < storeVersion;
                var updateType = "stable";
                await _localSettingsService.SaveSettingAsync(KeyValues.UpdateType, updateType);
                await _localSettingsService.SaveSettingAsync(KeyValues.UpdateUrl, GetDownloadUrl(updateType, isStoreVersion, storeVersion));
            }
            else
            {
                if (isBetaVersion) // 侧载版：根据当前版本类型决定更新策略
                {
                    // 测试版：检查是否有新的测试版，如果没有则检查稳定版
                    if (currentVersion < betaVersion)
                    {
                        needsUpdate = true;
                        var updateType = "beta";
                        await _localSettingsService.SaveSettingAsync(KeyValues.UpdateType, updateType);
                        await _localSettingsService.SaveSettingAsync(KeyValues.UpdateUrl, GetDownloadUrl(updateType, isStoreVersion, betaVersion));
                    }
                    else if (currentVersion < stableVersion)
                    {
                        needsUpdate = true;
                        var updateType = "stable";
                        await _localSettingsService.SaveSettingAsync(KeyValues.UpdateType, updateType);
                        await _localSettingsService.SaveSettingAsync(KeyValues.UpdateUrl, GetDownloadUrl(updateType, isStoreVersion, stableVersion));
                    }
                    else
                        needsUpdate = false;
                }
                else
                {
                    // 侧载正式版：只检查稳定版更新
                    needsUpdate = currentVersion < stableVersion;
                    var updateType = "stable";
                    await _localSettingsService.SaveSettingAsync(KeyValues.UpdateType, updateType);
                    await _localSettingsService.SaveSettingAsync(KeyValues.UpdateUrl, GetDownloadUrl(updateType, isStoreVersion, stableVersion));
                }
            }

            await _localSettingsService.SaveSettingAsync(KeyValues.LastUpdateCheckResult, needsUpdate);
            return _targetVersion;
        }
        catch (Exception e)
        {
            _infoService.DeveloperEvent(e: e);
            return null;
        }
    }

    public async Task<bool> IsUpdateAvailableAsync()
    {
        Version? v = await GetLatestVersionAsync();
        if (v is null) return false;
        Version currentVersion = Version.Parse(RuntimeHelper.GetVersion());
        return v > currentVersion;
    }

    /// <summary>
    /// 根据更新类型获取相应的下载URL
    /// </summary>
    /// <param name="updateType">更新类型：stable 或 beta</param>
    /// <param name="isStoreVersion">是否为商店版</param>
    /// <param name="version">目标下载版本</param>
    /// <returns>下载URL</returns>
    private static string GetDownloadUrl(string updateType, bool isStoreVersion, Version version)
    {
        if (isStoreVersion) return STORE_UPDATE_URL;
        Architecture processArch = RuntimeInformation.ProcessArchitecture;
        var archSuffix = processArch switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "ARM64",
            _ => "x64",
        };
        var url = updateType == "beta" ? SIDELOAD_BETA_DOWNLOAD_URL : SIDELOAD_STABLE_DOWNLOAD_URL;
        return $"{url}/{version}_{archSuffix}.msix";
    }

    /// <summary>
    /// 检查是否有新版本且未被忽略
    /// </summary>
    /// <returns>有可用更新返回版本号，否则返回null</returns>
    public async Task<string?> GetAvailableUpdateVersionAsync()
    {
        try
        {
            // 如果本次启动已经更新失败过、用户已取消过或已经执行过更新，不再返回可用更新
            if (_updateFailedThisSession || _updateCancelledThisSession || _updatePerformedThisSession) return null;

            Version? newestVersion = await GetLatestVersionAsync();
            if (newestVersion is null) return null;
            if (newestVersion <= Version.Parse(RuntimeHelper.GetVersion()))
                return null; // 没有新版本
            List<string> ignoredVersions = await _localSettingsService.ReadSettingAsync<List<string>>(KeyValues.IgnoredUpdateVersions) ?? [];
            if (ignoredVersions.Contains(newestVersion.ToString()))
                return null; // 该版本已被忽略
            return newestVersion.ToString();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 显示更新确认对话框
    /// </summary>
    /// <returns>用户选择：0=取消，1=立即更新，2=忽略这个版本</returns>
    public async Task<int> ShowUpdateConfirmationAsync()
    {
        try
        {
            var isStoreVersion = App.IsStoreVersion();

            var title = "UpdateService_UpdateAvailable_Title".GetLocalized();
            var content = isStoreVersion
                ? "UpdateService_StoreUpdate_Content".GetLocalized()
                : "UpdateService_SideloadUpdate_Content".GetLocalized();

            ContentDialog updateDialog = new()
            {
                XamlRoot = App.MainWindow!.Content.XamlRoot,
                RequestedTheme = App.MainWindow.Content is FrameworkElement element ? element.RequestedTheme : ElementTheme.Default,
                Title = title,
                Content = content,
                PrimaryButtonText = "UpdateService_Update_Confirm".GetLocalized(),
                SecondaryButtonText = "UpdateService_Update_Ignore".GetLocalized(),
                CloseButtonText = "UpdateService_Update_Cancel".GetLocalized(),
                DefaultButton = ContentDialogButton.Primary
            };

            ContentDialogResult result = await updateDialog.ShowAsync();
            return result switch
            {
                ContentDialogResult.Primary => 1,    // 立即更新
                ContentDialogResult.Secondary => 2,  // 忽略这个版本
                _ => 0                               // 取消/稍后提醒
            };
        }
        catch (Exception ex)
        {
            _infoService.Info(InfoBarSeverity.Error, "UpdateService_Dialog_Error".GetLocalized(), ex.Message);
            return 0;
        }
    }

    /// <summary>
    /// 忽略指定版本的更新
    /// </summary>
    /// <param name="version">要忽略的版本号</param>
    public async Task IgnoreVersionAsync(string version)
    {
        try
        {
            List<string> ignoredVersions = await _localSettingsService.ReadSettingAsync<List<string>>(KeyValues.IgnoredUpdateVersions) ?? new List<string>();
            if (!ignoredVersions.Contains(version))
            {
                ignoredVersions.Add(version);
                await _localSettingsService.SaveSettingAsync(KeyValues.IgnoredUpdateVersions, ignoredVersions);
            }
        }
        catch (Exception ex)
        {
            _infoService.Info(InfoBarSeverity.Error, "UpdateService_IgnoreVersion_Error".GetLocalized(), ex.Message);
        }
    }

    /// <summary>
    /// 执行更新操作
    /// </summary>
    public async Task PerformUpdateAsync()
    {
        try
        {
            _updatePerformedThisSession = true; // 设置本次启动已执行更新标记，避免再次弹窗
            var isStoreVersion = App.IsStoreVersion();
            if (isStoreVersion)
                await OpenStoreForUpdateAsync();
            else
            {
                await Launcher.LaunchUriAsync(new Uri(
                    await _localSettingsService.ReadSettingAsync<string>(KeyValues.UpdateUrl) ??
                    throw new InvalidOperationException("No update URL found.")));
            }
        }
        catch (Exception ex)
        {
            // 设置本次启动更新失败标记
            _updateFailedThisSession = true;
            _infoService.Info(InfoBarSeverity.Error, "UpdateService_Update_Error".GetLocalized(), ex.Message);

            // 对于侧载版，如果自动更新失败，提供手动安装选项
            if (!App.IsStoreVersion())
            {
                await ShowManualInstallOptionsAsync();
            }
        }
    }

    /// <summary>
    /// 打开商店进行更新
    /// </summary>
    private async Task OpenStoreForUpdateAsync()
    {
        try
        {
            // 使用ms-windows-store协议直接打开商店应用，避免通过浏览器
            var storeProtocolUrl = "ms-windows-store://pdp/?productid=9p9cbkd5hr3w";
            await Launcher.LaunchUriAsync(new Uri(storeProtocolUrl));
            _infoService.Info(InfoBarSeverity.Informational, "UpdateService_Store_Opened".GetLocalized());
        }
        catch (Exception ex)
        {
            // 设置本次启动更新失败标记
            _updateFailedThisSession = true;
            _infoService.Info(InfoBarSeverity.Error, "UpdateService_Store_Error".GetLocalized(), ex.Message);
        }
    }

    /// <summary>
    /// 设置用户取消更新标记
    /// </summary>
    public void SetUpdateCancelledThisSession()
    {
        _updateCancelledThisSession = true;
    }

    /// <summary>
    /// 提供手动安装选项（直接打开MSIX文件）
    /// </summary>
    private async Task ShowManualInstallOptionsAsync()
    {
        try
        {
            // 查找MSIX文件
            StorageFolder tempFolder = await GetTempRootAsync();
            StorageFolder? updateFolder = await tempFolder.GetFolderAsync("UpdateDownload");
            IReadOnlyList<StorageFile>? files = await updateFolder.GetFilesAsync();
            StorageFile? msixFile = files.FirstOrDefault(f => f.FileType.ToLower() == ".msix");

            if (msixFile == null)
            {
                _infoService.Info(InfoBarSeverity.Error, "UpdateService_Manual_Install_File_NotFound".GetLocalized());
                return;
            }

            ContentDialog manualInstallDialog = new()
            {
                XamlRoot = App.MainWindow!.Content.XamlRoot,
                RequestedTheme = App.MainWindow.Content is FrameworkElement element ? element.RequestedTheme : ElementTheme.Default,
                Title = "UpdateService_Manual_Install_Title".GetLocalized(),
                Content = "UpdateService_Manual_Install_MSIX_Content".GetLocalized(),
                PrimaryButtonText = "UpdateService_Manual_Install_Open_MSIX".GetLocalized(),
                SecondaryButtonText = "UpdateService_Manual_Install_OpenFolder".GetLocalized(),
                CloseButtonText = "UpdateService_Update_Cancel".GetLocalized(),
                DefaultButton = ContentDialogButton.Primary
            };

            ContentDialogResult result = await manualInstallDialog.ShowAsync();

            switch (result)
            {
                case ContentDialogResult.Primary:
                    // 直接打开MSIX文件进行安装
                    await OpenMsixFileAsync(msixFile);
                    break;
                case ContentDialogResult.Secondary:
                    // 打开包含更新文件的文件夹
                    await OpenUpdateFolderAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            _infoService.Info(InfoBarSeverity.Error, "UpdateService_Manual_Install_Error".GetLocalized(), ex.Message);
        }
    }

    /// <summary>
    /// 直接打开MSIX文件进行安装
    /// </summary>
    private async Task OpenMsixFileAsync(StorageFile msixFile)
    {
        try
        {
            await Launcher.LaunchFileAsync(msixFile);
            _infoService.Info(InfoBarSeverity.Informational, "UpdateService_MSIX_File_Opened".GetLocalized());
        }
        catch (Exception ex)
        {
            _infoService.Info(InfoBarSeverity.Error, "UpdateService_MSIX_File_Open_Error".GetLocalized(), ex.Message);
        }
    }

    /// <summary>
    /// 打开包含更新文件的文件夹
    /// </summary>
    private async Task OpenUpdateFolderAsync()
    {
        try
        {
            StorageFolder tempFolder = await GetTempRootAsync();
            StorageFolder? updateFolder = await tempFolder.GetFolderAsync("UpdateDownload");

            await Launcher.LaunchFolderAsync(updateFolder);
            _infoService.Info(InfoBarSeverity.Informational, "UpdateService_Update_Folder_Opened".GetLocalized());
        }
        catch (Exception ex)
        {
            _infoService.Info(InfoBarSeverity.Error, "UpdateService_Update_Folder_Error".GetLocalized(), ex.Message);
        }
    }

    public async Task UpdateSettingsBadgeAsync()
    {
        // 每次都检查是否有可用更新（未被忽略的且本次启动未失败或取消的）
        var availableVersion = await GetAvailableUpdateVersionAsync();
        SettingBadgeEvent?.Invoke(availableVersion != null);
    }
}
