@echo off
rem Prepares a git worktree of this repo: copies the untracked local files from the main
rem checkout and links its large dependencies. Run from anywhere inside the worktree.
rem Safe to re-run: anything already in place is skipped.
setlocal

for /f "delims=" %%i in ('git rev-parse --path-format^=absolute --git-common-dir') do set "COMMON=%%i"
for /f "delims=" %%i in ('git rev-parse --show-toplevel') do set "WT=%%i"
if not defined COMMON (echo Not inside a git repository. & exit /b 1)
for %%i in ("%COMMON%\..") do set "MAIN=%%~fi"
for %%i in ("%WT%") do set "WT=%%~fi"

if /i "%MAIN%"=="%WT%" (echo This is the main checkout, not a worktree. Nothing to do. & exit /b 0)
echo Main checkout: %MAIN%
echo Worktree:      %WT%

rem Untracked local files (git ignores them, so a new worktree lacks them).
for %%f in (
  "start-all.ps1"
  "CLAUDE.md"
  "backend\EventRegistration.Api\appsettings.Development.Local.json"
  "facerog-engine\.env.local"
  "facerog-engine\.env"
) do (
  if exist "%MAIN%\%%~f" (
    if exist "%WT%\%%~f" (echo   skip  %%~f) else (copy /y "%MAIN%\%%~f" "%WT%\%%~f" >nul && echo   copy  %%~f)
  )
)

rem Large dependency folders: directory junctions to the main checkout's.
for %%d in (
  "facerog-engine\.venv"
  "frontend\web\node_modules"
) do (
  if exist "%MAIN%\%%~d" (
    if exist "%WT%\%%~d" (echo   skip  %%~d) else (mklink /J "%WT%\%%~d" "%MAIN%\%%~d" >nul && echo   link  %%~d)
  )
)

rem Face model weights: hard links (their folder is tracked, so it can't be a junction).
set "WEIGHTS=facerog-engine\app\ml\weights"
for %%w in ("%MAIN%\%WEIGHTS%\*.onnx") do (
  if exist "%WT%\%WEIGHTS%\%%~nxw" (echo   skip  %WEIGHTS%\%%~nxw) else (mklink /H "%WT%\%WEIGHTS%\%%~nxw" "%%~fw" >nul && echo   link  %WEIGHTS%\%%~nxw)
)

echo Done.
endlocal
