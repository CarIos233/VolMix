@echo off
rem VolMix build helper for the packaged release (recompiles this copy).
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0source\build.ps1" -OutputDirectory "%~dp0." %*
if errorlevel 1 (
  echo.
  echo Build FAILED.
  exit /b 1
)
echo.
echo Build finished. Output: %~dp0VolMix.exe
endlocal
