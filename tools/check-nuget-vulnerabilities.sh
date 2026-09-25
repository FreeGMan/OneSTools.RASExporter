#!/usr/bin/env bash
set -euo pipefail

if ! command -v jq >/dev/null; then
    echo "FAIL: для проверки NuGet-отчёта требуется jq" >&2
    exit 1
fi

report=$(</dev/stdin)
if ! jq -e '(.sources | type == "array" and length > 0) and (.projects | type == "array") and
    ([.projects[] | .frameworks[]? |
      ((.topLevelPackages // []) + (.transitivePackages // []))[] |
      (.vulnerabilities // [])[]] | length == 0)' <<<"$report" >/dev/null; then
    echo "FAIL: уязвимые пакеты или некорректный NuGet-отчёт:" >&2
    jq -r '.projects[]? | .path as $project | .frameworks[]? |
        ((.topLevelPackages // []) + (.transitivePackages // []))[] |
        .id as $package | .vulnerabilities[]? |
        "\($project): \($package) \(.severity) \(.advisoryurl)"' <<<"$report" >&2 || true
    exit 1
fi

echo "OK: уязвимостей в каталоге NuGet не найдено"
