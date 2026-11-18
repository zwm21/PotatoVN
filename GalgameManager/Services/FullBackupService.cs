using System.IO.Compression;
using System.Text;
using GalgameManager.Helpers;
using Newtonsoft.Json;

namespace GalgameManager.Services;

/// <summary>
/// 完整备份的待处理请求。写入系统临时目录的标记文件，由 <see cref="App.OnLaunched"/>
/// 在打开任何数据库之前读取并执行——运行中的实例独占 pvn_data.db / pvn_settings.db，
/// 备份与恢复都只能在"没有实例在跑"的时刻进行。
/// </summary>
public class FullBackupRequest
{
    public const string BackupKind = "backup";
    public const string RestoreKind = "restore";

    /// <see cref="BackupKind"/> 或 <see cref="RestoreKind"/></summary>
    public string Kind { get; set; } = string.Empty;

    /// 备份时的目标文件夹，或恢复时的备份 zip 路径</summary>
    public string Path { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 完整备份/恢复结果。<see cref="MessageKey"/> 是本地化 key，由调用方本地化后展示。
/// </summary>
public class FullBackupResult
{
    public bool Success { get; init; }
    public string MessageKey { get; init; } = string.Empty;
    public string? MessageArg { get; init; }

    public static FullBackupResult Ok(string messageKey, string? messageArg = null) =>
        new() { Success = true, MessageKey = messageKey, MessageArg = messageArg };

    public static FullBackupResult Fail(string messageKey, string? messageArg = null) =>
        new() { Success = false, MessageKey = messageKey, MessageArg = messageArg };
}

/// <summary>
/// 完整备份：把 UserData 与 Settings 两个文件夹整体打包/恢复，
/// 覆盖现有「导出数据」只迁移游戏数据、不迁移软件设置的缺口。<br/>
/// 该类不依赖 DI（构造时注入路径），因此可以在启动早期（尚未解析任何服务时）直接使用，也便于测试。
/// </summary>
public sealed class FullBackupService
{
    public const string BackupFileExtension = ".pvnBackup.zip";
    public const string BackupKind = FullBackupRequest.BackupKind;
    public const string RestoreKind = FullBackupRequest.RestoreKind;

    private const string UserDataEntry = "UserData";
    private const string SettingsEntry = "Settings";
    private const string TempEntry = "Temp";
    private const string ImportBackupFolderName = "ImportBackup";
    private const string MarkerFileName = "PotatoVN_FullBackup.json";

    /// 崩溃残留的待处理请求超过该时长后直接丢弃，避免下次启动意外恢复数据</summary>
    private static readonly TimeSpan RequestExpiration = TimeSpan.FromMinutes(30);

    private readonly DirectoryInfo _localFolder;
    private readonly DirectoryInfo _settingsFolder;
    private readonly DirectoryInfo _tempFolder;
    private readonly string _markerPath;

    public FullBackupService(DirectoryInfo localFolder, DirectoryInfo settingsFolder, DirectoryInfo tempFolder,
        string? markerPath = null)
    {
        _localFolder = localFolder;
        _settingsFolder = settingsFolder;
        _tempFolder = tempFolder;
        _markerPath = markerPath ?? DefaultMarkerPath;
    }

    /// 待处理请求的标记文件，放在系统临时目录（一定在 UserData/Settings 之外）</summary>
    public static string DefaultMarkerPath => Path.Combine(Path.GetTempPath(), MarkerFileName);

    public static FullBackupService CreateDefault()
    {
        DirectoryInfo localFolder = new(AppStoragePaths.LocalDataPath);
        return new FullBackupService(localFolder, LocalSettingsService.GetSettingsFolder(localFolder),
            new(AppStoragePaths.TempPath));
    }

    /// 进度上报：(本地化 key, 参数)</summary>
    public Task<FullBackupResult?> RunPendingAsync(Action<string, string?>? report = null)
    {
        FullBackupRequest? request = ReadRequest();
        if (request is null)
        {
            // 标记损坏时一并清掉，否则每次启动都会反复尝试
            if (File.Exists(_markerPath)) DeleteRequest();
            return Task.FromResult<FullBackupResult?>(null);
        }

        if (DateTime.Now - request.CreatedAt > RequestExpiration)
        {
            DeleteRequest();
            return Task.FromResult<FullBackupResult?>(null);
        }

        bool valid = !string.IsNullOrWhiteSpace(request.Path) &&
                     (request.Kind == RestoreKind
                         ? File.Exists(request.Path)
                         : request.Kind == BackupKind && Directory.Exists(request.Path));
        if (!valid)
        {
            DeleteRequest();
            return Task.FromResult<FullBackupResult?>(FullBackupResult.Fail(request.Kind == RestoreKind
                ? "SettingsPage_Other_FullRestore_Missing"
                : "SettingsPage_Other_FullBackup_PathInvalid", request.Path));
        }

        return Task.Run(() =>
        {
            try
            {
                FullBackupResult result = request.Kind == RestoreKind
                    ? Restore(request.Path, report)
                    : Backup(request.Path, report);
                DeleteRequest();
                return (FullBackupResult?)result;
            }
            catch (Exception e)
            {
                // 无论成败都清掉标记，否则每次启动都会反复重试同一次操作
                DeleteRequest();
                return FullBackupResult.Fail(request.Kind == RestoreKind
                    ? "SettingsPage_Other_FullRestore_Fail"
                    : "SettingsPage_Other_FullBackup_Fail", e.Message);
            }
        });
    }

    public void RequestBackup(string targetDirectory) => WriteRequest(new FullBackupRequest
    {
        Kind = BackupKind, Path = targetDirectory, CreatedAt = DateTime.Now
    });

    public void RequestRestore(string backupFilePath) => WriteRequest(new FullBackupRequest
    {
        Kind = RestoreKind, Path = backupFilePath, CreatedAt = DateTime.Now
    });

    public bool HasPendingRequest() => File.Exists(_markerPath);

    private FullBackupResult Backup(string targetDirectory, Action<string, string?>? report)
    {
        string zipPath = Path.Combine(targetDirectory, CreateFileName());
        if (File.Exists(zipPath)) return FullBackupResult.Fail("SettingsPage_Other_FullBackup_FileExist", zipPath);

        var count = 0;
        try
        {
            using FileStream stream = new(zipPath, FileMode.CreateNew);
            using ZipArchive archive = new(stream, ZipArchiveMode.Create);
            foreach ((DirectoryInfo root, string entryName) in new[]
                     { (_localFolder, UserDataEntry), (_settingsFolder, SettingsEntry) })
            {
                if (!root.Exists) continue;
                report?.Invoke("FullBackup_Progress_Collecting", entryName);
                foreach (string file in Directory.EnumerateFiles(root.FullName, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(root.FullName, file);
                    // 非便携（且非 MSIX）时 Temp 位于 UserData 内部，不应进入备份
                    if (relative.Split('\\', '/')[0].Equals(TempEntry, StringComparison.OrdinalIgnoreCase)) continue;
                    archive.CreateEntryFromFile(file, $"{entryName}/{relative.Replace('\\', '/')}",
                        CompressionLevel.Optimal);
                    count++;
                }
            }

            if (count == 0)
            {
                stream.Dispose();
                File.Delete(zipPath);
                return FullBackupResult.Fail("SettingsPage_Other_FullBackup_NoData");
            }
        }
        catch
        {
            if (File.Exists(zipPath)) File.Delete(zipPath); // 不要留下打不开的半成品
            throw;
        }

        return FullBackupResult.Ok("SettingsPage_Other_FullBackup_Success", zipPath);
    }

    private FullBackupResult Restore(string zipPath, Action<string, string?>? report)
    {
        // 先整体校验，再动任何文件：只接受 UserData/ 与 Settings/ 顶层条目，且不允许写出目标目录
        List<(string EntryName, DirectoryInfo Root, string Relative)> entries = [];
        using (ZipArchive archive = ZipFile.OpenRead(zipPath))
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue; // 目录条目
                string fullName = entry.FullName.Replace('\\', '/');
                DirectoryInfo? root = null;
                string? relative = null;
                if (fullName.StartsWith($"{UserDataEntry}/", StringComparison.OrdinalIgnoreCase))
                {
                    root = _localFolder;
                    relative = fullName[UserDataEntry.Length..].TrimStart('/');
                }
                else if (fullName.StartsWith($"{SettingsEntry}/", StringComparison.OrdinalIgnoreCase))
                {
                    root = _settingsFolder;
                    relative = fullName[SettingsEntry.Length..].TrimStart('/');
                }

                if (root is null || relative is null) continue; // 其他顶层条目直接忽略
                if (!IsSafeRelativePath(relative))
                    return FullBackupResult.Fail("SettingsPage_Other_FullRestore_WrongFile", zipPath);
                // 只记条目名： ZipArchiveEntry 隶属于打开它的 ZipArchive，不能跨 using 持有
                entries.Add((entry.FullName, root, relative));
            }
        }

        if (entries.Count == 0) return FullBackupResult.Fail("SettingsPage_Other_FullRestore_NotBackup", zipPath);

        DirectoryInfo importBackup = new(Path.Combine(_tempFolder.FullName, ImportBackupFolderName));
        if (importBackup.Exists) importBackup.Delete(true);
        importBackup.Create();

        List<DirectoryInfo> cleared = [];
        try
        {
            // 1. 把当前两个文件夹备份出来，任一步失败都能完整回滚
            report?.Invoke("FullBackup_Progress_BackingUp", null);
            foreach (DirectoryInfo root in new[] { _localFolder, _settingsFolder }.Where(r => r.Exists))
                FolderOperations.Copy(root.FullName, Path.Combine(importBackup.FullName, root.Name));

            // 2. 清空目标目录
            foreach (DirectoryInfo root in new[] { _localFolder, _settingsFolder }.Where(r => r.Exists))
            {
                FolderOperations.Delete(root.FullName);
                root.Create();
                cleared.Add(root);
            }

            // 3. 解压
            report?.Invoke("FullBackup_Progress_Replacing", null);
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            foreach ((string entryName, DirectoryInfo root, string relative) in entries)
            {
                ZipArchiveEntry? entry = archive.GetEntry(entryName) ??
                                         throw new InvalidDataException($"备份包中找不到条目 {entryName}");
                string dest = Path.Combine(root.FullName, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, true);
            }
        }
        catch (Exception e)
        {
            report?.Invoke("FullBackup_Progress_RollingBack", null);
            try
            {
                foreach (DirectoryInfo root in cleared)
                {
                    FolderOperations.Delete(root.FullName);
                    root.Create();
                    string source = Path.Combine(importBackup.FullName, root.Name);
                    if (Directory.Exists(source)) FolderOperations.Copy(source, root.FullName);
                }
            }
            catch
            {
                // 回滚失败时 Temp/ImportBackup 里仍留有完整数据，交给用户手动还原
            }

            return FullBackupResult.Fail("SettingsPage_Other_FullRestore_Fail", e.Message);
        }

        // 成功后保留 Temp/ImportBackup 作为后悔药（与现有「导入数据」行为一致，不清）
        return FullBackupResult.Ok("SettingsPage_Other_FullRestore_Success", zipPath);
    }

    private static bool IsSafeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return false;
        if (Path.IsPathRooted(relativePath)) return false;
        return !relativePath.Replace('\\', '/').Split('/').Any(part => part is "..");
    }

    private static string CreateFileName() =>
        $"PotatoVN_{DateTime.Now:yy-MM-dd_HH-mm-ss}{BackupFileExtension}";

    private void WriteRequest(FullBackupRequest request)
    {
        string? dir = Path.GetDirectoryName(_markerPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_markerPath, JsonConvert.SerializeObject(request), Encoding.UTF8);
    }

    private FullBackupRequest? ReadRequest()
    {
        try
        {
            if (!File.Exists(_markerPath)) return null;
            return JsonConvert.DeserializeObject<FullBackupRequest>(File.ReadAllText(_markerPath));
        }
        catch
        {
            return null;
        }
    }

    private void DeleteRequest()
    {
        try
        {
            if (File.Exists(_markerPath)) File.Delete(_markerPath);
        }
        catch
        {
            // 删不掉也不影响启动，下次会被过期规则清掉
        }
    }
}
