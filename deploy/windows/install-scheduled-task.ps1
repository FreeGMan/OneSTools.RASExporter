<#
.SYNOPSIS
    Регистрация Scheduled Task для автозапуска OneSTools.Ras.Exporter на Windows.
.DESCRIPTION
    Экспортёр — консольный worker; запуск по загрузке ОС + перезапуск при падении.
    Журнал: <InstallDir>\logs\exporter.log (через обёртку run-as-task.cmd).
    Требуется PowerShell 5.1+ от имени администратора.
    Ротация журнала не встроена — см. DEPLOYMENT.md.
.PARAMETER InstallDir
    Каталог распакованного релизного архива (с подкаталогом bin и run-as-task.cmd).
.EXAMPLE
    .\install-scheduled-task.ps1 -InstallDir 'C:\onestools\ras-exporter' -Start
#>
[CmdletBinding()]
param(
    [string]$InstallDir = 'C:\onestools\ras-exporter',
    [string]$TaskName = 'OneSTools.Ras.Exporter',
    [string]$RunAsUser = 'SYSTEM',
    [switch]$Start
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath $InstallDir).Path
$wrapper = Join-Path $root 'run-as-task.cmd'
$exe = Join-Path $root 'bin\OneSTools.Ras.Exporter.exe'

if (-not (Test-Path -LiteralPath $wrapper)) { throw "Не найден $wrapper — распакуйте релизный архив в $InstallDir" }
if (-not (Test-Path -LiteralPath $exe)) { throw "Не найден $exe" }
if (-not (Test-Path -LiteralPath (Join-Path $root 'appsettings.json'))) {
    Write-Warning "Нет appsettings.json: скопируйте appsettings.production.example.json, замените REPLACE_* или задайте переменные окружения уровня машины."
}

$action = New-ScheduledTaskAction -Execute $wrapper -WorkingDirectory $root
$trigger = New-ScheduledTaskTrigger -AtStartup
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
    -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
$principal = New-ScheduledTaskPrincipal -UserId $RunAsUser -LogonType ServiceAccount -RunLevel Limited

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger `
    -Settings $settings -Principal $principal -Force `
    -Description 'OneSTools.Ras Exporter: метрики RAS 1С -> ClickHouse' | Out-Null

$log = Join-Path $root 'logs\exporter.log'
Write-Host "Задача '$TaskName' зарегистрирована (от $RunAsUser, рестарт при отказе: 3 x 1 мин)."
Write-Host "Журнал: $log"
if ($Start) {
    Start-ScheduledTask -TaskName $TaskName
    Write-Host "Задача запущена."
} else {
    Write-Host "Запуск: Start-ScheduledTask -TaskName '$TaskName'"
}
