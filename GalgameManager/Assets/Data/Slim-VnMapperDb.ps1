<#
.SYNOPSIS
    把 vn_mapper.db 的 title 表重建为 WITHOUT ROWID 并 VACUUM，缩小随包发布的数据库。

.DESCRIPTION
    上游 PotatoDBMapper 的 db-latest release 里，title 表的 DDL 是：

        CREATE TABLE "title" ("VndbId" integer , "Title" varchar primary key not null )

    Title 是非整数主键，SQLite 因此额外建了一个隐式索引 sqlite_autoindex_title_1，
    并且表本身仍然保留 rowid——同一份 Title 字符串等于存了两遍。
    title 有 11.3 万行，这部分重复占用约 5 MiB。

    本脚本把 title 重建为 WITHOUT ROWID（主键即聚簇索引，去掉隐式索引与 rowid），
    再 VACUUM 回收页面。实测 11,685,888 B -> 6,209,536 B，省 5,476,352 B（5.22 MiB，-46.9%）。
    改造用 PotatoVnDBMapper（sqlite-net-pcl）真实加载验证过：map 65,975 条与
    title 113,254 行全量一致，TryGetMapsWithName / TryGetMapAsync /
    Db.Table<TitleModel>() 查询结果不变。

    map 表**不动**：它的主键是 "VndbId" integer primary key autoincrement，
    本身就是 rowid 表 + 别名，没有重复索引；改成 WITHOUT ROWID 只多省 12,288 B，
    却要移除 autoincrement 语义，不值得。

    SQLite 引擎用仓库已经依赖的 SQLitePCLRaw.lib.e_sqlite3 原生库（NuGet 缓存内），
    不引入新依赖、不需要装 sqlite3.exe。C# 通过 Add-Type 内联，P/Invoke 该 DLL。
    整个改造在**同一个连接**的一个事务里完成（早期版本每次 Exec 都开关连接，
    sqlite3_close_v2 会把未提交的事务回滚掉，事务根本续不上）。

.PARAMETER Path
    要处理的 vn_mapper.db，默认脚本同目录的 vn_mapper.db。

.PARAMETER NativeLib
    e_sqlite3.dll 路径。默认从 NuGet 缓存按项目引用的 2.1.13 版本定位。

.EXAMPLE
    pwsh -File Assets/Data/Slim-VnMapperDb.ps1
#>
[CmdletBinding()]
param(
    [string]$Path = (Join-Path $PSScriptRoot 'vn_mapper.db'),
    [string]$NativeLib
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Path)) { throw "找不到数据库: $Path" }
$Path = (Resolve-Path -LiteralPath $Path).Path

if (-not $NativeLib) {
    $pkg = Join-Path $env:USERPROFILE '.nuget\packages\sqlitepclraw.lib.e_sqlite3\2.1.13\runtimes\win-x64\native\e_sqlite3.dll'
    if (-not (Test-Path -LiteralPath $pkg)) {
        throw "找不到 e_sqlite3.dll（$pkg）。先执行 dotnet restore 还原 SQLitePCLRaw.lib.e_sqlite3 2.1.13。"
    }
    $NativeLib = $pkg
}
if (-not (Test-Path -LiteralPath $NativeLib)) { throw "找不到原生库: $NativeLib" }

$before = (Get-Item -LiteralPath $Path).Length

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

// 单个持久连接上的极简 SQLite 封装。事务必须留在同一连接内，
// 否则 sqlite3_close_v2 会回滚未提交的事务。
public sealed class SlimDb : IDisposable
{
    [DllImport("e_sqlite3", EntryPoint = "sqlite3_open_v2")]
    private static extern int sqlite3_open_v2(string filename, out IntPtr db, int flags, IntPtr zvfs);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_exec")]
    private static extern int sqlite3_exec(IntPtr db, string sql, IntPtr cb, IntPtr arg, out IntPtr errmsg);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_prepare_v2")]
    private static extern int sqlite3_prepare_v2(IntPtr db, string sql, int nByte, out IntPtr stmt, out IntPtr tail);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_step")]
    private static extern int sqlite3_step(IntPtr stmt);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_column_int64")]
    private static extern long sqlite3_column_int64(IntPtr stmt, int i);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_column_text")]
    private static extern IntPtr sqlite3_column_text(IntPtr stmt, int i);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_finalize")]
    private static extern int sqlite3_finalize(IntPtr stmt);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_close_v2")]
    private static extern int sqlite3_close_v2(IntPtr db);

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_free")]
    private static extern void sqlite3_free(IntPtr p);

    private IntPtr _db;

    public SlimDb(string path)
    {
        int rc = sqlite3_open_v2(path, out _db, 6 /* READWRITE | CREATE */, IntPtr.Zero);
        if (rc != 0) { _db = IntPtr.Zero; throw new Exception("open 失败 (rc=" + rc + "): " + path); }
    }

    public static void SetLibrary(string lib)
    {
        NativeLibrary.SetDllImportResolver(typeof(SlimDb).Assembly,
            (name, asm, path) => name == "e_sqlite3" ? NativeLibrary.Load(lib) : IntPtr.Zero);
    }

    public void Exec(string sql, string what)
    {
        if (sqlite3_exec(_db, sql, IntPtr.Zero, IntPtr.Zero, out IntPtr err) != 0)
        {
            string m = Marshal.PtrToStringUTF8(err) ?? "?";
            sqlite3_free(err);
            throw new Exception(what + ": " + m);
        }
    }

    public string Scalar(string sql, string what)
    {
        if (sqlite3_prepare_v2(_db, sql, -1, out IntPtr stmt, out _) != 0)
            throw new Exception("prepare 失败: " + what);
        try
        {
            if (sqlite3_step(stmt) != 100) return string.Empty;
            IntPtr p = sqlite3_column_text(stmt, 0);
            return p == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(p) ?? string.Empty;
        }
        finally { sqlite3_finalize(stmt); }
    }

    public long Count(string sql, string what)
    {
        if (sqlite3_prepare_v2(_db, sql, -1, out IntPtr stmt, out _) != 0)
            throw new Exception("prepare 失败: " + what);
        try
        {
            if (sqlite3_step(stmt) != 100) return -1;
            return sqlite3_column_int64(stmt, 0);
        }
        finally { sqlite3_finalize(stmt); }
    }

    public void Dispose()
    {
        if (_db != IntPtr.Zero) { sqlite3_close_v2(_db); _db = IntPtr.Zero; }
        GC.SuppressFinalize(this);
    }
}
'@ -ReferencedAssemblies System.Runtime.InteropServices

[SlimDb]::SetLibrary($NativeLib)

$conn = $null
try {
    $conn = New-Object SlimDb -ArgumentList $Path

    # ---------- 只读体检：确认 schema 符合预期，不符合就拒绝改 ----------
    $ddl = $conn.Scalar("SELECT sql FROM sqlite_master WHERE type='table' AND name='title'", "读 title DDL")
    if (-not $ddl) { throw "title 表不存在，中止（不想动未知结构的库）" }
    if ($ddl -match 'WITHOUT\s+ROWID') {
        Write-Host "title 已经是 WITHOUT ROWID，无需处理。"
        exit 0
    }
    if ($ddl -notmatch '"Title"\s+varchar\s+primary\s+key') {
        throw "title 的 DDL 与预期不符（主键不是 Title varchar），中止：$ddl"
    }
    $beforeRows = $conn.Count("SELECT COUNT(*) FROM title", "统计 title 行数")

    # ---------- 改造：单连接单事务，失败整体回滚 ----------
    $conn.Exec("BEGIN;", "BEGIN")
    try {
        $conn.Exec(@'
CREATE TABLE "title_new" (
"VndbId" integer ,
"Title" varchar primary key not null ) WITHOUT ROWID;
'@, "建 title_new")
        $conn.Exec("INSERT OR IGNORE INTO title_new (VndbId, Title) SELECT VndbId, Title FROM title;", "拷贝数据")
        $conn.Exec("DROP TABLE title;", "DROP TABLE title")
        $conn.Exec("ALTER TABLE title_new RENAME TO title;", "ALTER TABLE RENAME")
        $conn.Exec("COMMIT;", "COMMIT")
    }
    catch {
        try { $conn.Exec("ROLLBACK;", "ROLLBACK") } catch { }
        throw
    }

    # ---------- 压缩：VACUUM INTO 临时库再替换原文件 ----------
    # 不用原地 VACUUM：改造后 DROP TABLE 会留下约 2300 个空闲页，原地 VACUUM 虽然把
    # freelist 清零，但实测文件大小不回落（page_count 不降），必须走 VACUUM INTO
    # 生成一个紧凑的新库再整体替换才可靠。
    $tmp = "$Path.vacuuming"
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force }
    $conn.Exec("VACUUM INTO '$($tmp.Replace('\', '\\'))';", "VACUUM INTO")
    $conn.Dispose()
    $conn = $null
    Move-Item -LiteralPath $tmp -Destination $Path -Force
    $conn = New-Object SlimDb -ArgumentList $Path

    # ---------- 校验 ----------
    $afterRows = $conn.Count("SELECT COUNT(*) FROM title", "复查 title 行数")
    $ddlAfter = $conn.Scalar("SELECT sql FROM sqlite_master WHERE type='table' AND name='title'", "复查 DDL")
    $autoindex = $conn.Count("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='sqlite_autoindex_title_1'", "复查隐式索引")
    $freeAfter = $conn.Count("PRAGMA freelist_count", "复查 freelist")
}
finally {
    if ($conn) { $conn.Dispose() }
}

$after = (Get-Item -LiteralPath $Path).Length
Write-Host ("vn_mapper.db 瘦身完成: {0}" -f $Path)
Write-Host ("  title 行数      : {0:N0} -> {1:N0} {2}" -f $beforeRows, $afterRows, $(if ($beforeRows -eq $afterRows) { "(一致)" } else { "(不一致!!)" }))
Write-Host ("  隐式索引        : {0} 个 {1}" -f $autoindex, $(if ($autoindex -eq 0) { "(已消除)" } else { "(仍存在!!)" }))
Write-Host ("  WITHOUT ROWID   : {0}" -f $(if ($ddlAfter -match 'WITHOUT\s+ROWID') { "是" } else { "否!!" }))
Write-Host ("  文件大小        : {0:N0} B -> {1:N0} B   省 {2:N0} B ({3:N2} MiB, -{4:N1}%)" -f `
        $before, $after, ($before - $after), (($before - $after) / 1MB), (100.0 * ($before - $after) / $before))

Write-Host ("  空闲页(freelist): {0} 个 {1}" -f $freeAfter, $(if ($freeAfter -eq 0) { "(已回收)" } else { "(未回收!!)" }))
if ($beforeRows -ne $afterRows -or $autoindex -ne 0 -or $freeAfter -ne 0) { throw "校验未通过，请检查" }
