#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

# vn_mapper.db：上游 PotatoDBMapper 的 db-latest release
curl --fail --location --retry 3 --retry-all-errors \
  --output vn_mapper.db \
  https://github.com/GoldenPotato137/PotatoDBMapper/releases/download/db-latest/vn_mapper.db

# 立刻瘦身：上游库里 title 表的主键是字符串 Title，SQLite 会为此额外建一个隐式索引
# sqlite_autoindex_title_1，并且表本身仍保留 rowid——同一份 Title 字符串存了两遍。
# title 有 11.3 万行，这部分重复约 5 MiB。改成 WITHOUT ROWID 后随包发布能少 5.22 MiB，
# 且 PotatoVnDBMapper（sqlite-net-pcl）查询行为不变（已全量比对验证）。
# 脚本幂等：已经是 WITHOUT ROWID 就直接跳过；schema 与预期不符时会拒绝改动。
if ! command -v pwsh >/dev/null 2>&1; then
  echo "错误: 找不到 pwsh，无法执行 Slim-VnMapperDb.ps1 瘦身 vn_mapper.db" >&2
  exit 1
fi
pwsh -NoProfile -File Slim-VnMapperDb.ps1

# vndb 标签库：上游直接给 .gz，**保持压缩态随包发布**（运行期由 GzipJsonFile 流式解压，
# 见 GalgameManager/Helpers/GzipJsonFile.cs），所以下载完不要再解压。
curl --fail --location --retry 3 --retry-all-errors \
  --output vndb-tags-latest.json.gz \
  https://dl.vndb.org/dump/vndb-tags-latest.json.gz

# producers.json 与 vndb-tags-translation.json 不在此脚本里下载，是手工维护后直接提交的。
# 改完未压缩的 *.json 后，用下面这条把三个文件重新压成随包发布的 *.json.gz：
#
#   for f in vndb-tags-latest.json vndb-tags-translation.json producers.json; do
#       gzip -9 -n -k -f "$f"     # -n：头部不带时间戳/文件名，产物可复现
#   done
#
# 忘记重新生成会被 GalgameManager.Test/Helpers/GzipJsonFileTest.cs 抓到
# （它校验 .gz 解压后与同目录 .json 逐字节一致）。
