@echo off
setlocal
cd /d "%~dp0"
echo.
echo   PhysicalWater 0.3.12 - Unity Bundle + Local Build
echo.
powershell -ExecutionPolicy Bypass -File ".\install-0.3.12.ps1"
if errorlevel 1 (
  echo.
  echo BUILD FAILED
  pause
  exit /b 1
) else (
  echo.
  echo BUILD SUCCEEDED - PhysicalWater 0.3.12 AssetBundle and DLL were rebuilt and installed locally.
  pause
)
