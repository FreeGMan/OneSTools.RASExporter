#!/usr/bin/env bash
# Сборка релизного комплекта OneSTools.Ras.
#
# Гейты (locked-mode restore, build 0 warnings, тесты, аудит безопасности) →
# pack NuGet-библиотек → self-contained single-file publish по каждому RID →
# архивы с deploy-активами, SHA256SUMS.txt, RELEASE_NOTES.md.
#
# Использование:
#   bash tools/build-release.sh [--version 0.1.0] [--rids linux-x64,win-x64] [--out DIR]
#                               [--skip-tests] [--skip-security] [--tag]
#
# Артефакты (по умолчанию artifacts/v<VERSION>/):
#   nuget/     OneSTools.Ras.Client + OneSTools.Ras.Exporter.Core (.nupkg, локальный фид)
#   publish/   содержимое bin/ по RID (промежуточный результат publish)
#   archives/  OneSTools.Ras.Exporter-<VERSION>-<rid>.tar.gz|zip
#   RELEASE_NOTES.md, SHA256SUMS.txt
#
# Требуется сеть на шаге publish: runtime-pack'ы целевого RID скачиваются из NuGet.org
# (кросс-сборка win-x64 с Linux-хоста поддерживается).
set -euo pipefail

usage() { sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//'; }

ROOT=$(git rev-parse --show-toplevel)
cd "$ROOT"

VERSION=""
RID_LIST="linux-x64 win-x64"
OUT_DIR=""
SKIP_TESTS=0
SKIP_SECURITY=0
DO_TAG=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        --version) VERSION="${2:?--version требует значение}"; shift 2 ;;
        --rids) RID_LIST=${2//,/ }; shift 2 ;;
        --out) OUT_DIR="${2:?--out требует значение}"; shift 2 ;;
        --skip-tests) SKIP_TESTS=1; shift ;;
        --skip-security) SKIP_SECURITY=1; shift ;;
        --tag) DO_TAG=1; shift ;;
        -h|--help) usage; exit 0 ;;
        *) echo "Неизвестный аргумент: $1" >&2; usage; exit 2 ;;
    esac
done

[[ -n $VERSION ]] || VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -1)
[[ -n $VERSION ]] || { echo "Версия не задана: --version или <Version> в Directory.Build.props" >&2; exit 1; }
OUT_DIR=$(realpath -m "${OUT_DIR:-artifacts/v$VERSION}")

command -v dotnet >/dev/null || { echo "Требуется .NET SDK" >&2; exit 1; }
[[ $SKIP_SECURITY -eq 1 ]] || command -v jq >/dev/null || {
    echo "Для tools/security-check.sh нужен jq (или --skip-security)" >&2; exit 1; }

for rid in $RID_LIST; do
    case $rid in
        linux-*|win-*|osx-*) ;;
        *) echo "Неподдерживаемый RID: $rid" >&2; exit 1 ;;
    esac
done

SOL=OneSTools.Ras.sln
EXPORTER=src/OneSTools.Ras.Exporter
LIBS=(src/OneSTools.Ras.Client src/OneSTools.Ras.Exporter.Core)
COMMIT=$(git rev-parse HEAD)
COMMIT_SHORT=${COMMIT:0:7}
BUILD_DATE=$(date -u +%Y-%m-%dT%H:%M:%SZ)
SDK_VERSION=$(dotnet --version)
HOST_DESC="$(uname -s) $(uname -r) $(uname -m)"
GATES=()

echo "OneSTools.Ras $VERSION ($COMMIT_SHORT) — сборка релиза: $HOST_DESC, .NET SDK $SDK_VERSION"
if [[ -n $(git status --porcelain) ]]; then
    echo "WARN: в рабочем дереве есть незакоммиченные изменения — в комплект войдёт коммит $COMMIT_SHORT:"
    git status --short
fi

echo
echo "== 1. restore --locked-mode (воспроизводимость supply chain) =="
dotnet restore "$SOL" --locked-mode
GATES+=("restore --locked-mode: OK")

echo
echo "== 2. build Release (0 warnings — TreatWarningsAsErrors) =="
dotnet build "$SOL" -c Release --no-restore
GATES+=("build Release: 0 warnings, 0 errors")

echo
if [[ $SKIP_TESTS -eq 1 ]]; then
    echo "== 3. тесты: ПРОПУЩЕНЫ (--skip-tests) =="
    GATES+=("тесты: пропущены")
else
    echo "== 3. тесты (unit + mock; живой стенд и ClickHouse скипуются без доступа) =="
    dotnet test "$SOL" -c Release --no-build
    GATES+=("тесты: OK")
fi

echo
if [[ $SKIP_SECURITY -eq 1 ]]; then
    echo "== 4. аудит безопасности: ПРОПУЩЕН (--skip-security) =="
    GATES+=("security-check: пропущен")
else
    echo "== 4. аудит безопасности =="
    bash tools/security-check.sh
    GATES+=("security-check: OK")
fi

echo
echo "== 5. pack NuGet-библиотек =="
mkdir -p "$OUT_DIR/nuget" "$OUT_DIR/archives" "$OUT_DIR/publish"
for lib in "${LIBS[@]}"; do
    dotnet pack "$lib" -c Release --no-build --no-restore -o "$OUT_DIR/nuget" -p:Version="$VERSION"
done
ls -1 "$OUT_DIR/nuget"/*.nupkg

# Restore с RID раскрывает runtime-специфичный граф и перезаписывает packages.lock.json —
# релизный (RID-ный) граф не должен попадать в отслеживаемые lock-файлы.
LOCK_FILES=()
while IFS= read -r lf; do LOCK_FILES+=("$lf"); done < <(git ls-files '*packages.lock.json')
LOCK_BACKUP=$(mktemp -d)
lock_key() { tr '/' '@' <<<"$1"; }
restore_locks() {
    for lf in "${LOCK_FILES[@]}"; do cp "$LOCK_BACKUP/$(lock_key "$lf")" "$lf"; done
    rm -rf "$LOCK_BACKUP"
}
trap restore_locks EXIT
for lf in "${LOCK_FILES[@]}"; do cp "$lf" "$LOCK_BACKUP/$(lock_key "$lf")"; done

echo
echo "== 6. publish self-contained single-file =="
ARCHIVES=()
for rid in $RID_LIST; do
    echo "-- $rid"
    PUB="$OUT_DIR/publish/$rid/OneSTools.Ras.Exporter"
    dotnet publish "$EXPORTER" -c Release -r "$rid" --self-contained true \
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
        -p:InvariantGlobalization=true -p:Version="$VERSION" -o "$PUB"

    # Стендовая конфигурация из репозитория и XML-документация в релиз не идут:
    # оператор намеренно копирует appsettings.production.example.json в appsettings.json.
    rm -f "$PUB/appsettings.json" "$PUB"/*.xml

    PKG="OneSTools.Ras.Exporter-$VERSION-$rid"
    STAGE="$OUT_DIR/pkg/$PKG"
    mkdir -p "$STAGE/bin"
    cp -a "$PUB/." "$STAGE/bin/"
    # Шаблон конфигурации — рядом с бинарником: хост читает appsettings.json из каталога exe.
    cp deploy/appsettings.production.example.json "$STAGE/bin/"
    cp LICENSE docs/DEPLOYMENT.md "$STAGE/"
    case $rid in
        linux-*) cp deploy/linux/*.service deploy/linux/*.env.example "$STAGE/" ;;
        win-*) cp deploy/windows/*.cmd deploy/windows/*.ps1 "$STAGE/" ;;
    esac

    if [[ $rid == linux-* || $rid == osx-* ]]; then
        tar --owner=0 --group=0 --numeric-owner -czf "$OUT_DIR/archives/$PKG.tar.gz" -C "$OUT_DIR/pkg" "$PKG"
        ARCHIVES+=("archives/$PKG.tar.gz")
    else
        (cd "$OUT_DIR/pkg" && zip -qr "$OUT_DIR/archives/$PKG.zip" "$PKG")
        ARCHIVES+=("archives/$PKG.zip")
    fi
    rm -rf "$STAGE"
    du -sh "$OUT_DIR/${ARCHIVES[-1]}"
done
rmdir "$OUT_DIR/pkg" 2>/dev/null || true
restore_locks
trap - EXIT

# Возврат к RID-agnostic графу: obj/ после publish с RuntimeIdentifier расходится с
# закоммиченными packages.lock.json, и последующий `dotnet list --vulnerable` (шаг аудита
# вручную) падает вместо чистого результата.
echo
echo "-- restore --locked-mode (возврат к RID-agnostic графу)"
dotnet restore "$SOL" --locked-mode --verbosity quiet

echo
echo "== 7. контрольные суммы и релизные заметки =="
(
    cd "$OUT_DIR"
    find archives nuget -type f \( -name '*.tar.gz' -o -name '*.zip' -o -name '*.nupkg' \) \
        | sort | xargs -r sha256sum > SHA256SUMS.txt
)
cat "$OUT_DIR/SHA256SUMS.txt"

{
    echo "# OneSTools.Ras $VERSION — релиз"
    echo
    echo "Дата: $BUILD_DATE · коммит: \`$COMMIT_SHORT\` · сборка: $HOST_DESC, .NET SDK $SDK_VERSION"
    echo
    echo "## Гейты сборки"
    for gate in "${GATES[@]}"; do echo "- $gate"; done
    echo
    echo "## Комплект"
    echo
    for a in "${ARCHIVES[@]}"; do echo "- \`$a\`"; done
    for lib in "${LIBS[@]}"; do
        echo "- \`nuget/$(basename "$lib").$VERSION.nupkg\` — локальный NuGet-фид (на nuget.org не публиковалось)"
    done
    echo "- \`SHA256SUMS.txt\` — проверка: \`sha256sum -c SHA256SUMS.txt\` в каталоге релиза"
    echo
    echo "## Состав архива"
    echo
    echo "- \`bin/\` — self-contained single-file экспортёр (.NET на целевом сервере не нужен,"
    echo "  сборка с InvariantGlobalization) и \`bin/appsettings.production.example.json\` —"
    echo "  шаблон конфигурации кладётся рядом с бинарником (хост читает \`appsettings.json\` из каталога exe)."
    echo "- Формат журнала: \`dd.MM.yyyy HH:mm:ss [уровень] [категория] текст\` (консольный форматтер \`onestools\`)."
    echo "- Linux: \`onestools-ras-exporter.service\`, \`onestools-ras-exporter.env.example\`."
    echo "- Windows: \`run-as-task.cmd\`, \`install-scheduled-task.ps1\`, \`uninstall-scheduled-task.ps1\`."
    echo "- \`DEPLOYMENT.md\`, \`LICENSE\`."
    echo
    echo "## Область и ограничения"
    echo
    echo "- Независимая (не официальная) реализация протокола SWP; проверено на RAS 8.3.25.1520."
    echo "- 27 методов \`RasAdminClient\` из 84 методов Java API (docs/GAP_ANALYSIS.md)."
    echo "- Без TLS/SASL: эксплуатация только в изолированном контуре (docs/SECURITY.md §1)."
    echo "- Экспорт метрик best effort: после исчерпания повторов порции отбрасываются со"
    echo "  счётчиком потерь в health-логе; дисковой очереди нет."
    echo "- Публичный API не заморожен — версия 0.x."
} > "$OUT_DIR/RELEASE_NOTES.md"

if [[ $DO_TAG -eq 1 ]]; then
    echo
    echo "== 8. git tag =="
    TAG="v$VERSION"
    if git rev-parse -q --verify "refs/tags/$TAG" >/dev/null; then
        echo "Тег $TAG уже существует — не пересоздаём."
    else
        git tag -a "$TAG" -m "OneSTools.Ras $VERSION"
        echo "Создан тег $TAG."
    fi
fi

echo
echo "Комплект: $OUT_DIR"
