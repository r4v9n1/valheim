@echo off
setlocal
cd /d "%~dp0"
echo.
echo   PhysicalWater 0.5.1 - AUDITED HYDRODYNAMICS CORE
echo.
powershell -ExecutionPolicy Bypass -File ".\install-0.5.1.ps1"
if errorlevel 1 (
  echo.
  echo BUILD FAILED
  pause
  exit /b 1
) else (
  echo.
  echo BUILD SUCCEEDED - PhysicalWater 0.5.1 Hydrodynamics Core rebuilt and installed.
  pause
)
