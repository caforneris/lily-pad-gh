@echo off
REM LilyPadGH Installer - Run before opening Grasshopper

setlocal

set SOURCE_DIR=%~dp0LilyPadGH
set DEPLOY_PATH=%appdata%\McNeel\Rhinoceros\packages\8.0\LilyPadGH
set JULIA_EXE=%DEPLOY_PATH%\0.0.1\JuliaPackage\julia-1.11.7-win64\julia-1.11.7\bin\julia.exe
set JULIA_PROJECT=%DEPLOY_PATH%\0.0.1\JuliaPackage\julia-1.11.7-win64\julia-1.11.7

echo.
echo ========================================
echo   LilyPadGH Installer
echo ========================================
echo.

REM Copy plugin to Rhino packages
echo Copying LilyPadGH to Rhino packages...
robocopy "%SOURCE_DIR%" "%DEPLOY_PATH%" /E /NFL /NDL /NJH /NJS /NC /NS

if errorlevel 8 (
    echo ERROR: Copy failed.
    pause
    exit /b 1
)
echo Copy successful.
echo.

REM Copy Project.toml to Julia folder
echo Copying Project.toml...
copy /Y "%~dp0..\Julia\Project.toml" "%JULIA_PROJECT%\Project.toml" >nul

REM Install Julia packages from copied location
echo Installing Julia packages...
"%JULIA_EXE%" --project="%JULIA_PROJECT%" -e "using Pkg; Pkg.instantiate()"

if errorlevel 1 (
    echo ERROR: Julia package installation failed.
    pause
    exit /b 1
)

echo.
echo Installation complete! You can now open Grasshopper.
pause
