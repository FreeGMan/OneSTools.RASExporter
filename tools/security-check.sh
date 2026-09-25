#!/usr/bin/env bash
# TODO.md T7/S5: локальная проверка безопасности зависимостей и сборки.
# Использование: bash tools/security-check.sh [solution]
set -euo pipefail

SOL="${1:-OneSTools.Ras.sln}"
STATUS=0

echo "== 1. NuGet: уязвимые пакеты (прямые и транзитивные) =="
if out=$(dotnet list "$SOL" package --vulnerable --include-transitive --format json 2>&1); then
    if ! bash tools/check-nuget-vulnerabilities.sh <<<"$out"; then
        STATUS=1
    fi
else
    echo "FAIL: сканирование NuGet не удалось: $out"
    STATUS=1
fi

echo
echo "== 2. NuGet Audit при restore (должен быть включён в Directory.Build.props) =="
if grep -q "<NuGetAudit>true</NuGetAudit>" Directory.Build.props; then
    echo "OK: NuGetAudit=true, Mode=$(grep -oP '(?<=<NuGetAuditMode>).*(?=</)' Directory.Build.props)"
else
    echo "FAIL: NuGetAudit не включён"; STATUS=1
fi

echo
echo "== 3. Lock-файлы (воспроизводимость supply chain) =="
missing=0
while read -r proj; do
    case "$proj" in *.csproj)
        if [[ ! -f "$(dirname "$proj")/packages.lock.json" ]]; then
            echo "MISSING: $(dirname "$proj")/packages.lock.json"; missing=1
        fi ;;
    esac
done < <(dotnet sln "$SOL" list)
if [[ $missing -eq 0 ]]; then echo "OK: lock-файлы есть у всех проектов"; else STATUS=1; fi
if ! dotnet restore "$SOL" --locked-mode --verbosity quiet; then
    echo "FAIL: locked-mode restore не прошёл"; STATUS=1
fi

echo
echo "== 4. Секреты в индексе git (грубый поиск по отслеживаемым файлам) =="
if git ls-files | xargs grep -niE "(password|pwd)\\s*[=:]\\s*['\"][^'\"\\s$\\{]{4,}" 2>/dev/null | grep -viE "clickhouse|changeme|placeholder|your-|dev-only" ; then
    echo "WARN: проверьте найденное выше (dev-значения допустимы по решению T7/Q3)"; STATUS=1
else
    echo "OK: явных production-секретов в индексе нет"
fi

echo
if [[ $STATUS -eq 0 ]]; then echo "RESULT: PASS"; else echo "RESULT: FAIL (см. выше)"; fi
exit $STATUS
