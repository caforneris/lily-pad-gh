@echo off
REM ============================================================
REM LilyPad-GH Package Installer
REM Double-click this file to install required Julia packages
REM ============================================================

echo.
echo ============================================================
echo LilyPad-GH Package Installer
echo ============================================================
echo.

REM Try to find Julia in common locations
set JULIA_EXE=

REM Check bundled Julia first (same folder as this script)
if exist "%~dp0..\JuliaPackage\julia-1.11.7-win64\julia-1.11.7\bin\julia.exe" (
    set JULIA_EXE=%~dp0..\JuliaPackage\julia-1.11.7-win64\julia-1.11.7\bin\julia.exe
    echo Found bundled Julia
    goto :found_julia
)

REM Check system Julia
if exist "%LocalAppData%\Programs\Julia-1.11.7\bin\julia.exe" (
    set JULIA_EXE=%LocalAppData%\Programs\Julia-1.11.7\bin\julia.exe
    echo Found system Julia 1.11.7
    goto :found_julia
)

if exist "%LocalAppData%\Programs\Julia-1.11.6\bin\julia.exe" (
    set JULIA_EXE=%LocalAppData%\Programs\Julia-1.11.6\bin\julia.exe
    echo Found system Julia 1.11.6
    goto :found_julia
)

if exist "%LocalAppData%\Programs\Julia-1.11\bin\julia.exe" (
    set JULIA_EXE=%LocalAppData%\Programs\Julia-1.11\bin\julia.exe
    echo Found system Julia 1.11
    goto :found_julia
)

if exist "%LocalAppData%\Programs\Julia-1.10\bin\julia.exe" (
    set JULIA_EXE=%LocalAppData%\Programs\Julia-1.10\bin\julia.exe
    echo Found system Julia 1.10
    goto :found_julia
)

REM Julia not found
echo ERROR: Julia not found!
echo.
echo Please install Julia from https://julialang.org/downloads/
echo Or ensure Julia is installed in one of these locations:
echo   - %LocalAppData%\Programs\Julia-1.11.7\
echo   - %LocalAppData%\Programs\Julia-1.11\
echo   - %LocalAppData%\Programs\Julia-1.10\
echo.
pause
exit /b 1

:found_julia
echo Using Julia: %JULIA_EXE%
echo.
echo Installing packages... This may take 5-10 minutes on first run.
echo.

"%JULIA_EXE%" "%~dp0install_packages.jl"

if errorlevel 1 (
    echo.
    echo ERROR: Package installation failed!
    echo See error messages above.
    pause
    exit /b 1
)

echo.
echo Installation complete!
pause
