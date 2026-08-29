@echo off
setlocal
cd /d "%~dp0"
echo.
echo   PhysicalWater 0.4.5 - BATHYMETRY + CONTINUOUS SURF
echo.
powershell -ExecutionPolicy Bypass -File ".\install-0.4.5.ps1"
if errorlevel 1 (
  echo.
  echo BUILD FAILED
  pause
  exit /b 1
) else (
  echo.
  echo BUILD SUCCEEDED - PhysicalWater 0.4.5 Bathymetry + Continuous Surf rebuilt and installed.
  pause
)
