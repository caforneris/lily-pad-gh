@echo off
REM ============================================================
REM LilyPad-GH Package Installer
REM Double-click this file to install required Julia packages
REM ============================================================
REM
REM MANUAL INSTALLATION (if this script doesn't work):
REM --------------------------------------------------
REM 1. Open Julia from command line or Start menu
REM 2. Press ] to enter package mode (prompt changes to pkg>)
REM 3. Type these commands one at a time:
REM      add HTTP
REM      add JSON3
REM      add Plots
REM      add StaticArrays
REM      add WaterLily
REM      add ParametricBodies
REM 4. Press Backspace to exit package mode
REM 5. Type: using Pkg; Pkg.precompile()
REM 6. Close Julia
REM
REM INSTALLING JULIA (if not installed):
REM ------------------------------------
REM 1. Go to https://julialang.org/downloads/
REM 2. Download Julia 1.10 or newer (1.11.7 recommended)
REM 3. Run the installer
REM 4. Default location: %LocalAppData%\Programs\Julia-1.11.7\
REM 5. After installation, run this script again
REM
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
echo ============================================================
echo ERROR: Julia not found!
echo ============================================================
echo.
echo Julia is required to run LilyPad-GH simulations.
echo.
echo TO INSTALL JULIA:
echo   1. Go to https://julialang.org/downloads/
echo   2. Download Julia 1.10 or newer (1.11.7 recommended)
echo   3. Run the installer (use default settings)
echo   4. Run this script again after installation
echo.
echo Expected locations:
echo   - %LocalAppData%\Programs\Julia-1.11.7\
echo   - %LocalAppData%\Programs\Julia-1.11\
echo   - %LocalAppData%\Programs\Julia-1.10\
echo.
echo ============================================================
echo.
pause
exit /b 1

:found_julia
echo Using Julia: %JULIA_EXE%
echo.
echo Installing packages... This may take 5-10 minutes on first run.
echo.
echo If this fails, you can install packages manually:
echo   1. Open Julia
echo   2. Press ] to enter package mode
echo   3. Type: add HTTP
echo   4. Type: add JSON3
echo   5. Type: add Plots
echo   6. Type: add StaticArrays
echo   7. Type: add WaterLily
echo   8. Type: add ParametricBodies
echo   9. Press Backspace, then type: using Pkg; Pkg.precompile()
echo.

"%JULIA_EXE%" "%~dp0install_packages.jl"

if errorlevel 1 (
    echo.
    echo ============================================================
    echo WARNING: Package installation may have had issues.
    echo ============================================================
    echo.
    echo If you see errors above, try manual installation:
    echo   1. Open Julia from Start menu
    echo   2. Follow the manual steps shown above
    echo.
)

echo.
pause
