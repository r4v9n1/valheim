@echo off
setlocal
title PhysicalWater 0.6 Stage-B1 Validation
echo.
echo PhysicalWater 0.6 development - Stage-B1 Long-Run / Density Validation
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0RUN-VOLUMETRIC-STAGE-B1-VALIDATION.ps1"
if errorlevel 1 (
  echo.
  echo STAGE-B1 VALIDATION FAILED
  pause
  exit /b 1
)
echo.
echo STAGE-B1 VALIDATION PASSED
pause
exit /b 0
