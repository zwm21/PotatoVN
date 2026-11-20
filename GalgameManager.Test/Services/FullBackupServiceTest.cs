using System.IO.Compression;
using System.Text;
using GalgameManager.Services;

namespace GalgameManager.Test.Services;

/// <summary>
/// 完整备份/恢复测试：UserData 与 Settings 两个文件夹的整包导出导入。<br/>
/// 服务通过构造函数注入路径，全部使用临时目录，不触碰真实用户数据。
/// </summary>
[TestFixture]
public class FullBackupServiceTest
{
    private const string UserDataEntry = "UserData";
    private const string SettingsEntry = "Settings";

    private string _root = null!;
    private string _marker = null!;
    private DirectoryInfo _userData = null!;
    private DirectoryInfo _settings = null!;
    private DirectoryInfo _temp = null!;
    private DirectoryInfo _target = null!;
    private FullBackupService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "potatovn-fullbackup-" + Guid.NewGuid().ToString("N"));
        _userData = new DirectoryInfo(Path.Combine(_root, UserDataEntry));
        _settings = new DirectoryInfo(Path.Combine(_root, SettingsEntry));
        _temp = new DirectoryInfo(Path.Combine(_root, "Temp"));
        _target = new DirectoryInfo(Path.Combine(_root, "Target"));
        _marker = Path.Combine(_root, "marker.json");
        foreach (DirectoryInfo dir in new[] { _userData, _settings, _temp, _target }) dir.Create();
        SeedData();
        _service = new FullBackupService(_userData, _settings, _temp, _marker);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) DeleteDirectory(_root);
    }

    private void SeedData()
    {
        Write(_userData, "pvn_data.db", "game-db");
        Write(_userData, "pvn_data-log.db", "game-log");
        Write(_userData, Path.Combine("Images", "cover.png"), "cover");
        Write(_userData, Path.Combine("Logs", "log.txt"), "log-content");
        // 非便携（且非 MSIX）时 Temp 位于 UserData 内部，不应进入备份
        Write(_userData, Path.Combine("Temp", "junk.bin"), "junk");
        Write(_settings, "pvn_settings.db", "settings-db");
        Write(_settings, "pvn_settings-log.db", "settings-log");
    }

    private static void Write(DirectoryInfo root, string relativePath, string content)
    {
        string path = Path.Combine(root.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Read(DirectoryInfo root, string relativePath) =>
        File.ReadAllText(Path.Combine(root.FullName, relativePath));

    [Test]
    public async Task Backup_PacksBothFoldersAndSkipsTemp()
    {
        List<string> progress = [];
        _service.RequestBackup(_target.FullName);

        FullBackupResult? result = await _service.RunPendingAsync((key, arg) => progress.Add(key));

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Success, Is.True);
        Assert.That(progress, Is.Not.Empty, "应上报进度");
        Assert.That(File.Exists(_marker), Is.False, "完成后应清除待处理标记");

        string zip = Directory.GetFiles(_target.FullName, "*" + FullBackupService.BackupFileExtension).Single();
        using ZipArchive archive = ZipFile.OpenRead(zip);
        List<string> entries = archive.Entries.Select(e => e.FullName).ToList();
        Assert.That(entries, Contains.Item($"{UserDataEntry}/pvn_data.db"));
        Assert.That(entries, Contains.Item($"{UserDataEntry}/pvn_data-log.db"));
        Assert.That(entries, Contains.Item($"{UserDataEntry}/Images/cover.png"));
        Assert.That(entries, Contains.Item($"{UserDataEntry}/Logs/log.txt"));
        Assert.That(entries, Contains.Item($"{SettingsEntry}/pvn_settings.db"));
        Assert.That(entries.Any(e => e.StartsWith("Temp/")), Is.False, "Temp 不应进入备份");
    }

    [Test]
    public async Task Restore_ReplacesCurrentDataWithBackup()
    {
        await RunBackupAsync();
        // 备份之后改动数据：改内容、删设置库、新增文件
        Write(_userData, "pvn_data.db", "tampered");
        File.Delete(Path.Combine(_settings.FullName, "pvn_settings.db"));
        Write(_userData, "new-game.txt", "should-be-gone");

        _service.RequestRestore(GetBackupFile());
        FullBackupResult? result = await _service.RunPendingAsync();

        Assert.That(result!.Success, Is.True, $"{result.MessageKey} {result.MessageArg}");
        Assert.That(Read(_userData, "pvn_data.db"), Is.EqualTo("game-db"));
        Assert.That(Read(_userData, "pvn_data-log.db"), Is.EqualTo("game-log"));
        Assert.That(Read(_userData, Path.Combine("Images", "cover.png")), Is.EqualTo("cover"));
        Assert.That(Read(_settings, "pvn_settings.db"), Is.EqualTo("settings-db"));
        Assert.That(File.Exists(Path.Combine(_userData.FullName, "new-game.txt")), Is.False,
            "备份中不存在的文件应被清除");
        Assert.That(File.Exists(Path.Combine(_userData.FullName, "Temp", "junk.bin")), Is.False);
        Assert.That(File.Exists(_marker), Is.False);
    }

    [Test]
    public async Task Restore_CorruptZip_RollsBackAndKeepsCurrentData()
    {
        string badZip = Path.Combine(_target.FullName, "bad.pvnBackup.zip");
        await File.WriteAllTextAsync(badZip, "this is not a zip file");

        _service.RequestRestore(badZip);
        FullBackupResult? result = await _service.RunPendingAsync();

        Assert.That(result!.Success, Is.False, $"{result.MessageKey} {result.MessageArg}");
        Assert.That(Read(_userData, "pvn_data.db"), Is.EqualTo("game-db"));
        Assert.That(Read(_settings, "pvn_settings.db"), Is.EqualTo("settings-db"));
        Assert.That(File.Exists(_marker), Is.False);
    }

    [Test]
    public async Task Restore_ExtractionFailsMidWay_RollsBackToPreImportState()
    {
        // 让解压在中途失败：先写文件 UserData/x，再写 UserData/x/y（x 已是文件，无法建目录）
        string zipPath = Path.Combine(_target.FullName, "midfail.pvnBackup.zip");
        using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            _ = archive.CreateEntry($"{UserDataEntry}/x");
            _ = archive.CreateEntry($"{UserDataEntry}/x/y");
        }

        _service.RequestRestore(zipPath);
        FullBackupResult? result = await _service.RunPendingAsync();

        Assert.That(result!.Success, Is.False, $"{result.MessageKey} {result.MessageArg}");
        Assert.That(Read(_userData, "pvn_data.db"), Is.EqualTo("game-db"));
        Assert.That(Read(_userData, Path.Combine("Images", "cover.png")), Is.EqualTo("cover"));
        Assert.That(Read(_settings, "pvn_settings.db"), Is.EqualTo("settings-db"));
        Assert.That(Directory.Exists(Path.Combine(_temp.FullName, "ImportBackup")), Is.True,
            "失败后仍应留下导入前备份供手动还原");
        Assert.That(File.Exists(_marker), Is.False);
    }

    [Test]
    public async Task Restore_ZipWithoutDataEntries_IsRejected()
    {
        string zipPath = Path.Combine(_target.FullName, "other.pvnBackup.zip");
        using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            _ = archive.CreateEntry("readme.txt");

        _service.RequestRestore(zipPath);
        FullBackupResult? result = await _service.RunPendingAsync();

        Assert.That(result!.Success, Is.False);
        Assert.That(Read(_userData, "pvn_data.db"), Is.EqualTo("game-db"));
        Assert.That(Read(_settings, "pvn_settings.db"), Is.EqualTo("settings-db"));
    }

    [Test]
    public async Task Restore_EntryEscapingDataFolder_IsRejected()
    {
        string zipPath = Path.Combine(_target.FullName, "evil.pvnBackup.zip");
        using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            _ = archive.CreateEntry($"{UserDataEntry}/pvn_data.db");
            _ = archive.CreateEntry($"{UserDataEntry}/../evil.txt");
        }

        _service.RequestRestore(zipPath);
        FullBackupResult? result = await _service.RunPendingAsync();

        Assert.That(result!.Success, Is.False);
        Assert.That(Read(_userData, "pvn_data.db"), Is.EqualTo("game-db"));
        Assert.That(File.Exists(Path.Combine(_root, "evil.txt")), Is.False, "不允许写出目标目录");
        Assert.That(Read(_settings, "pvn_settings.db"), Is.EqualTo("settings-db"));
    }

    [Test]
    public async Task RunPending_WithoutRequest_DoesNothing()
    {
        FullBackupResult? result = await _service.RunPendingAsync();

        Assert.That(result, Is.Null);
        Assert.That(Read(_userData, "pvn_data.db"), Is.EqualTo("game-db"));
    }

    [Test]
    public async Task Backup_MissingTargetDirectory_FailsAndClearsRequest()
    {
        _service.RequestBackup(Path.Combine(_root, "not-exist"));

        FullBackupResult? result = await _service.RunPendingAsync();

        Assert.That(result!.Success, Is.False);
        Assert.That(File.Exists(_marker), Is.False);
    }

    [Test]
    public async Task Restore_MissingBackupFile_FailsAndKeepsCurrentData()
    {
        _service.RequestRestore(Path.Combine(_target.FullName, "not-exist.pvnBackup.zip"));

        FullBackupResult? result = await _service.RunPendingAsync();

        Assert.That(result!.Success, Is.False);
        Assert.That(Read(_userData, "pvn_data.db"), Is.EqualTo("game-db"));
        Assert.That(File.Exists(_marker), Is.False);
    }

    [Test]
    public async Task RunPending_StaleRequest_IsIgnored()
    {
        string zipPath = await CreateBackupZipAsync();
        // 手动写入一个很早以前的标记，模拟上次崩溃残留
        await File.WriteAllTextAsync(_marker,
            $"{{\"Kind\":\"{FullBackupService.RestoreKind}\",\"Path\":\"{zipPath.Replace("\\", "\\\\")}\"," +
            "\"CreatedAt\":\"2000-01-01T00:00:00\"}", Encoding.UTF8);

        FullBackupResult? result = await _service.RunPendingAsync();

        Assert.That(result, Is.Null, "过期标记应被直接忽略");
        Assert.That(File.Exists(_marker), Is.False);
        Assert.That(Read(_userData, "pvn_data.db"), Is.EqualTo("game-db"));
    }

    [Test]
    public void Request_RoundTripsThroughMarkerFile()
    {
        _service.RequestBackup(_target.FullName);

        Assert.That(File.Exists(_marker), Is.True);
        Assert.That(_service.HasPendingRequest(), Is.True);

        _service.RequestRestore("dummy.pvnBackup.zip");

        Assert.That(File.Exists(_marker), Is.True);
    }

    private async Task<string> CreateBackupZipAsync()
    {
        _service.RequestBackup(_target.FullName);
        await _service.RunPendingAsync();
        return GetBackupFile();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task BackupAndRestore_PreservesLegacyDataTransferSettingsAndFiles(bool autoExportEnabled)
    {
        string legacySettings = $"{{\"autoExport\":{autoExportEnabled.ToString().ToLowerInvariant()}," +
                                "\"autoExportInterval\":1,\"maxBackupNumber\":3}";
        const string dataStatus = "{\"ImportGalgame\":false,\"ImportPageSettings\":false}";
        const string exportFileName = "PotatoVN_old.pvnExport.zip";
        Write(_settings, "legacy-settings.json", legacySettings);
        Write(_userData, "data.DataStatus.json", dataStatus);
        string exportPath = Path.Combine(_userData.FullName, exportFileName);
        using (ZipArchive exportArchive = ZipFile.Open(exportPath, ZipArchiveMode.Create))
        {
            using StreamWriter writer = new(exportArchive.CreateEntry("pvn_data.db").Open());
            writer.Write("legacy-export-db");
        }
        byte[] exportBytes = File.ReadAllBytes(exportPath);
        string backupPath = await CreateBackupZipAsync();
        Write(_userData, "pvn_data.db", "changed-game-db");
        Write(_settings, "legacy-settings.json", "changed-settings");
        File.WriteAllText(exportPath, "changed-export");

        _service.RequestRestore(backupPath);
        FullBackupResult? result = await _service.RunPendingAsync();

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Success, Is.True);
            Assert.That(Read(_userData, "pvn_data.db"), Is.EqualTo("game-db"));
            Assert.That(Read(_settings, "pvn_settings.db"), Is.EqualTo("settings-db"));
            Assert.That(Read(_settings, "legacy-settings.json"), Is.EqualTo(legacySettings));
            Assert.That(Read(_userData, "data.DataStatus.json"), Is.EqualTo(dataStatus));
            Assert.That(File.ReadAllBytes(exportPath), Is.EqualTo(exportBytes));
            Assert.That(Read(_userData, Path.Combine("Images", "cover.png")), Is.EqualTo("cover"));
        });
    }

    private string GetBackupFile() =>
        Directory.GetFiles(_target.FullName, "*" + FullBackupService.BackupFileExtension).Single();

    private async Task RunBackupAsync()
    {
        _service.RequestBackup(_target.FullName);
        FullBackupResult? result = await _service.RunPendingAsync();
        Assert.That(result!.Success, Is.True);
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(path, true);
    }
}
