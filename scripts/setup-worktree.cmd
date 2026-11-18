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
  ".claude\launch.json"
  "backend\EventRegistration.Api\appsettings.Development.Local.json"
  "facerog-engine\.env.local"
  "facerog-engine\.env"
) do (
  if exist "%MAIN%\%%~f" (
    if exist "%WT%\%%~f" (echo   skip  %%~f) else (copy /y "%MAIN%\%%~f" "%WT%\%%~f" >nul && echo   copy  %%~f)
  )
)

rem No local dev-server config in the main checkout either: start from the shared template.
if not exist "%WT%\.claude\launch.json" (copy /y "%WT%\.claude\launch.example.json" "%WT%\.claude\launch.json" >nul && echo   copy  .claude\launch.example.json -^> .claude\launch.json)

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

rem The linked node_modules is the main checkout's: it can lag this worktree's package-lock.json.
if exist "%WT%\frontend\web\node_modules" (
  pushd "%WT%\frontend\web"
  call npm ls --depth=0 >nul 2>&1
  if errorlevel 1 (
    echo   WARN  frontend\web\node_modules is missing packages or out of date.
    echo         Run "npm ci" in %MAIN%\frontend\web, or "npm install" if this worktree changed package.json.
  ) else (echo   ok    frontend\web\node_modules matches package.json)
  popd
)

echo Done.
endlocal
