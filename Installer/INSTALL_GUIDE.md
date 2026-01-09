# LilyPad-GH Installation Guide

## Overview

LilyPad-GH is a Grasshopper plugin for Rhino 3D that provides 2D fluid dynamics simulation using WaterLily.jl (Julia CFD solver).

---

## For End Users: Standalone Installation

### Quick Install

1. **Double-click** `Install_LilyPadGH.bat`
2. Follow the prompts
3. Open Rhino 8 and Grasshopper
4. Find LilyPad component in the toolbar

### What the Installer Does

| Step | Action |
|------|--------|
| 1/4 | Install plugin to `%APPDATA%\McNeel\Rhinoceros\packages\8.0\LilyPadGH\0.0.1\` |
| 2/4 | Install bundled Julia 1.11.7 runtime |
| 3/4 | Add Julia to user PATH environment variable |
| 4/4 | Install Julia packages (from pre-packaged depot or via download) |

### Manual Installation (if installer fails)

1. Copy `Plugin\LilyPadGH.gha` to:
   ```
   %APPDATA%\McNeel\Rhinoceros\packages\8.0\LilyPadGH\0.0.1\
   ```

2. Copy `Plugin\Julia\` folder to the same location

3. Copy `JuliaPackage\` folder to the same location

4. Add Julia to your PATH:
   - Open System Properties > Environment Variables
   - Edit user PATH variable
   - Add: `%APPDATA%\McNeel\Rhinoceros\packages\8.0\LilyPadGH\0.0.1\JuliaPackage\julia-1.11.7-win64\julia-1.11.7\bin`

5. Install Julia packages - run:
   ```
   %APPDATA%\McNeel\Rhinoceros\packages\8.0\LilyPadGH\0.0.1\Julia\Install_LilyPad_Packages.bat
   ```

### Manual Package Installation via Julia REPL

If the package installer doesn't work:

1. Open Julia (from Start menu or command prompt)
2. Press `]` to enter package mode (prompt changes to `pkg>`)
3. Type these commands one at a time:
   ```
   add HTTP
   add JSON3
   add Plots
   add StaticArrays
   add WaterLily
   add ParametricBodies
   ```
4. Press Backspace to exit package mode
5. Type: `using Pkg; Pkg.precompile()`
6. Close Julia

---

## For Developers: Building a Distribution Package

### Prerequisites

1. Build the LilyPad-GH plugin (Visual Studio or `dotnet build`)
2. Ensure plugin is deployed to Grasshopper packages folder
3. Have Julia packages installed on your machine

### Creating a Distribution

1. Run `Package_Distribution.bat`
2. Choose whether to include pre-packaged Julia depot (~5GB)
3. The script creates `LilyPadGH_Distribution/` folder
4. Zip the folder and share with testers

### Distribution Folder Structure

```
LilyPadGH_Distribution/
|-- Install_LilyPadGH.bat     # Full installer for end users
|-- README.txt                # Quick start instructions
|-- Plugin/
|   |-- LilyPadGH.gha         # Grasshopper plugin assembly
|   +-- Julia/                # Julia scripts and package installer
|       |-- RunServer.jl      # HTTP server (port 8080)
|       |-- 2d.jl             # 2D CFD simulation
|       |-- 2d_MultiBody.jl   # Multi-body support
|       |-- install_packages.jl
|       +-- Install_LilyPad_Packages.bat
|-- JuliaPackage/             # Bundled Julia 1.11.7 runtime (~320MB)
|   +-- julia-1.11.7-win64/
|       +-- julia-1.11.7/
|           +-- bin/julia.exe
+-- JuliaDepot/               # Pre-packaged Julia packages (optional ~5GB)
    |-- packages/             # Package source code
    |-- compiled/             # Precompiled code
    |-- artifacts/            # Binary artifacts (GR, etc.)
    +-- registries/           # Package registry
```

### With vs Without Pre-Packaged Depot

| Option | Size | Install Time | Reliability |
|--------|------|--------------|-------------|
| With JuliaDepot | ~5.5 GB | Fast (copy only) | High - no network needed |
| Without JuliaDepot | ~400 MB | Slow (5-10 min download) | Depends on network/servers |

**Recommendation:** Include JuliaDepot for beta testing to minimize support issues.

---

## Required Julia Packages

| Package | Purpose |
|---------|---------|
| HTTP | HTTP server for Grasshopper communication |
| JSON3 | JSON parsing for geometry data |
| Plots | Visualization and GIF generation |
| StaticArrays | High-performance array operations |
| WaterLily | CFD solver core |
| ParametricBodies | Body definition for simulations |

---

## Troubleshooting

### "Module not found" or "Package not found"

**Cause:** Julia packages not installed

**Solution:** Run `Plugin\Julia\Install_LilyPad_Packages.bat` or install manually via Julia REPL

### "Julia executable not found"

**Cause:** Julia not installed or not in PATH

**Solution:**
1. Check if Julia is installed
2. Verify PATH includes Julia bin folder
3. Restart terminal/Rhino after PATH changes

### "Server already running on port 8080"

**Cause:** Previous Julia server didn't shut down

**Solution:**
1. Open Task Manager
2. End any `julia.exe` processes
3. Try again

### Installer shows "Sharing violation"

**Cause:** Rhino has the plugin file locked

**Solution:** Close Rhino before running installer

### PATH changes not taking effect

**Cause:** Environment variables cached

**Solution:** Close and reopen all command prompts and Rhino

---

## System Requirements

- Windows 10/11
- Rhino 8 with Grasshopper
- ~6 GB disk space (with pre-packaged depot)
- ~1 GB disk space (without depot, packages download separately)

---

## File Locations After Installation

| Item | Location |
|------|----------|
| Plugin | `%APPDATA%\McNeel\Rhinoceros\packages\8.0\LilyPadGH\0.0.1\` |
| Julia Runtime | `...\LilyPadGH\0.0.1\JuliaPackage\` |
| Julia Scripts | `...\LilyPadGH\0.0.1\Julia\` |
| Julia Packages | `%USERPROFILE%\.julia\` |
| Simulation Output | `...\LilyPadGH\0.0.1\Temp\` |
