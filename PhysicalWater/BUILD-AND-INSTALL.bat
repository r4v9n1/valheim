@echo off
setlocal
cd /d "%~dp0"
echo.
echo   PhysicalWater 0.3.11 - Local Build
echo.
powershell -ExecutionPolicy Bypass -File ".\install.ps1"
if errorlevel 1 (
  echo.
  echo BUILD FAILED
  pause
  exit /b 1
) else (
  echo.
echo BUILD SUCCEEDED - PhysicalWater 0.3.11 was built and installed locally.
  pause
)
