@echo off
rem Publishes a new version: tags the current commit and pushes it.
rem GitHub then builds Tandem.exe and attaches it to a release.
rem
rem   release.cmd v1.0.1
rem
rem Before running: change the version in src\AppInfo.cs and commit.
setlocal
cd /d "%~dp0"
if "%~1"=="" (
  echo Usage: release.cmd v1.0.1
  exit /b 1
)
git tag "%~1" || exit /b 1
git push origin main "%~1" || exit /b 1
echo.
echo Pushed %~1. GitHub is building the release now.
