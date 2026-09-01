@echo off
setlocal
cd /d "%~dp0"
echo.
echo  PhysicalWater 0.6 development - Volumetric Stage-B FLIP Validation
echo.
powershell -ExecutionPolicy Bypass -File ".\RUN-VOLUMETRIC-STAGE-B-VALIDATION.ps1"
if errorlevel 1 (
  echo.
  echo STAGE-B VALIDATION FAILED
  pause
  exit /b 1
) else (
  echo.
  echo STAGE-B VALIDATION PASSED
  pause
)
