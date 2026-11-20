using System.Xml.Linq;
using GalgameManager.Helpers;

namespace GalgameManager.Test;

[TestFixture]
public class ProjectTest
{
    [Test]
    public void Settings_DataTransferEntryPointsAreRemoved_FullBackupCommandsRemain()
    {
        Type viewModel = typeof(GalgameManager.ViewModels.SettingsViewModel);
        string[] removedProperties =
        [
            "ExportDataCommand", "ImportDataCommand", "SelectAutoExportPathCommand",
            "IsAutoExportEnabled", "AutoExportInterval", "MaxBackupNumber",
        ];
        string[] removedTypes =
        [
            "GalgameManager.Services.AutoExportService",
            "GalgameManager.Contracts.Services.IAutoExportService",
            "GalgameManager.Models.BgTasks.ExportTask",
            "GalgameManager.Views.ImportWindow",
        ];

        Assert.Multiple(() =>
        {
            foreach (string property in removedProperties)
                Assert.That(viewModel.GetProperty(property), Is.Null, property);
            foreach (string typeName in removedTypes)
                Assert.That(viewModel.Assembly.GetType(typeName), Is.Null, typeName);
            Assert.That(viewModel.GetProperty("FullBackupCommand"), Is.Not.Null);
            Assert.That(viewModel.GetProperty("FullRestoreCommand"), Is.Not.Null);
        });
    }

    [Test]
    public void Version_ShouldMatch_PackageAppxmanifest()
    {
        var manifestVersion = ReadPackageAppxManifestIdentityVersion();

        var runtimeVersion = RuntimeHelper.GetVersion();

        // 程序集/文件/产品版本取的是动态版本号：manifest 前三段不变、第 4 段叠加"自 manifest 改动以来的提交数"，
        // 因此这里只要求前三段一致，第 4 段允许大于等于 manifest 的 revision
        Assert.That(Version.Parse(runtimeVersion).Major, Is.EqualTo(Version.Parse(manifestVersion).Major));
        Assert.That(Version.Parse(runtimeVersion).Minor, Is.EqualTo(Version.Parse(manifestVersion).Minor));
        Assert.That(Version.Parse(runtimeVersion).Build, Is.EqualTo(Version.Parse(manifestVersion).Build));
        Assert.That(Version.Parse(runtimeVersion).Revision, Is.GreaterThanOrEqualTo(0));

        var assemblyVersion = typeof(RuntimeHelper).Assembly.GetName().Version?.ToString();
        Assert.That(assemblyVersion, Is.Not.Null);
        Assert.That(assemblyVersion, Is.EqualTo(runtimeVersion));
    }

    private static string ReadPackageAppxManifestIdentityVersion()
    {
        var manifestPath = FindPackageAppxManifestPath();

        var doc = XDocument.Load(manifestPath);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

        var identity = doc.Root?.Elements(ns + "Identity").FirstOrDefault();
        var version = identity?.Attribute("Version")?.Value;

        if (string.IsNullOrWhiteSpace(version))
        {
            Assert.Fail($"未能从 {manifestPath} 读取 Identity Version。请检查 Package.appxmanifest 是否包含 <Identity Version=.../>。");
        }

        return version!;
    }

    private static string FindPackageAppxManifestPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        for (var i = 0; i < 12 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "GalgameManager", "Package.appxmanifest");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        Assert.Fail($"未找到 GalgameManager/Package.appxmanifest。当前测试目录: {AppContext.BaseDirectory}");
        return string.Empty;
    }
}
