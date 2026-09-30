@echo off
REM Обёртка Scheduled Task: перенаправляет консольный вывод службы в файл журнала.
REM У Scheduled Task нет встроенного редиректа stdout, а экспортёр пишет логи в консоль.
if not exist "%~dp0logs" mkdir "%~dp0logs"
"%~dp0bin\OneSTools.Ras.Exporter.exe" >> "%~dp0logs\exporter.log" 2>&1
