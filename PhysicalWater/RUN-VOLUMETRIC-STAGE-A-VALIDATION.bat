@echo off
setlocal
cd /d "%~dp0"
echo.
echo  PhysicalWater 0.6 development - Volumetric Stage-A Validation
echo.
powershell -ExecutionPolicy Bypass -File ".\RUN-VOLUMETRIC-STAGE-A-VALIDATION.ps1"
if errorlevel 1 (
  echo.
  echo VALIDATION FAILED
  pause
  exit /b 1
) else (
  echo.
  echo VALIDATION PASSED
  pause
)
