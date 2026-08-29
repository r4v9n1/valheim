@echo off
setlocal EnableExtensions
title PhysicalWater E3 Live Field Probe

:menu
cls
echo.
echo ============================================================
echo   PhysicalWater E3 Live Field Probe
echo ============================================================
echo.
echo   [1] Prepare/build/install diagnostic candidate
echo   [2] Collect probe results after closing Valheim
echo   [3] Exit
echo.
choice /C 123 /N /M "Choose 1, 2 or 3: "
if errorlevel 3 goto :eof
if errorlevel 2 goto collect
if errorlevel 1 goto prepare

:prepare
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0E3-LIVE-FIELD-PROBE.ps1" -Mode prepare
set ERR=%ERRORLEVEL%
echo.
if not "%ERR%"=="0" (
  echo PREPARE FAILED - exit code %ERR%
) else (
  echo PREPARE COMPLETED.
)
echo.
pause
goto menu

:collect
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0E3-LIVE-FIELD-PROBE.ps1" -Mode collect
set ERR=%ERRORLEVEL%
echo.
if not "%ERR%"=="0" (
  echo COLLECTION FAILED - exit code %ERR%
) else (
  echo COLLECTION COMPLETED.
)
echo.
pause
goto menu
