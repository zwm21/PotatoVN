using System.IO.Compression;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace GalgameManager.Helpers;

/// <summary>
/// 读取随包发布的 gzip 压缩 JSON 数据文件（<c>Assets/Data/*.json.gz</c>）。
/// <para>
/// 这三个数据文件压缩前合计 2.51 MiB，gzip 后 0.64 MiB，随包直发压缩版、
/// 运行期流式解压，不落临时文件、不需要联网。目录下同时保留未压缩的 <c>*.json</c>
/// 作为源（改数据改它，再重新生成 .gz），但它们不参与发布；
/// <c>GzipJsonFileTest</c> 会校验 .gz 解压后与 .json 逐字节一致。
/// </para>
/// </summary>
public static class GzipJsonFile
{
    /// <summary>
    /// 解析指定的 gzip 压缩 JSON 文件。
    /// </summary>
    /// <param name="path">
    /// 绝对路径，或相对于应用目录（<see cref="AppContext.BaseDirectory"/>）的路径。
    /// </param>
    /// <returns>解析结果；文件不存在时返回 <c>null</c>（与旧的 <c>File.Exists</c> 短路行为一致）。</returns>
    /// <exception cref="InvalidDataException">文件存在但不是合法的 gzip 流。</exception>
    public static JToken? Parse(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string full = Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
        if (!File.Exists(full)) return null;

        using FileStream fs = File.OpenRead(full);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using StreamReader reader = new(gz, System.Text.Encoding.UTF8);
        using Newtonsoft.Json.JsonTextReader json = new(reader);
        return JToken.Load(json);
    }
}
