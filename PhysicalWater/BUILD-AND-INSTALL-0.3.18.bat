@echo off
setlocal
cd /d "%~dp0"
echo.
echo   PhysicalWater 0.3.18 - AUTHORITATIVE PHYSICS WATER CUTOVER
echo.
powershell -ExecutionPolicy Bypass -File ".\install-0.3.18.ps1"
if errorlevel 1 (
  echo.
  echo BUILD FAILED
  pause
  exit /b 1
) else (
  echo.
  echo BUILD SUCCEEDED - PhysicalWater 0.3.18 rebuilt and installed: no vanilla water simulation/render fallback.
  pause
)
