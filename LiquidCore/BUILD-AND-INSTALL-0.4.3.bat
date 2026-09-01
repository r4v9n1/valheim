@echo off
setlocal
cd /d "%~dp0"
echo.
echo   PhysicalWater 0.4.3 - VISUAL FIDELITY
echo.
powershell -ExecutionPolicy Bypass -File ".\install-0.4.3.ps1"
if errorlevel 1 (
  echo.
  echo BUILD FAILED
  pause
  exit /b 1
) else (
  echo.
  echo BUILD SUCCEEDED - PhysicalWater 0.4.3 Visual Fidelity rebuilt and installed.
  pause
)
