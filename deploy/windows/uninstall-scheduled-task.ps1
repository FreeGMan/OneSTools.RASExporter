<#
.SYNOPSIS
    Снятие Scheduled Task OneSTools.Ras.Exporter (файлы и журнал остаются на диске).
.EXAMPLE
    .\uninstall-scheduled-task.ps1 -TaskName 'OneSTools.Ras.Exporter'
#>
[CmdletBinding()]
param(
    [string]$TaskName = 'OneSTools.Ras.Exporter',
    [switch]$RemoveLogs
)

$ErrorActionPreference = 'Stop'

$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if (-not $task) {
    Write-Host "Задача '$TaskName' не найдена."
    return
}

Write-Warning 'Остановка задачи принудительно завершает процесс: накопленные батчи метрик не дописываются (экспорт best effort).'
Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
Write-Host "Задача '$TaskName' снята."

if ($RemoveLogs) {
    $root = Split-Path -Parent $task.Actions[0].Execute
    $logDir = Join-Path $root 'logs'
    if (Test-Path -LiteralPath $logDir) {
        Remove-Item -LiteralPath $logDir -Recurse -Force
        Write-Host "Удалён каталог журнала: $logDir"
    }
} else {
    Write-Host "Конфигурация и журнал оставлены; -RemoveLogs удаляет журналы."
}
