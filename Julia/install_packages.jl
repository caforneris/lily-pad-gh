# ============================================================
# LilyPad-GH Package Installation Script
# Run this once to install required Julia packages
# ============================================================
#
# MANUAL INSTALLATION (if this script doesn't work):
# --------------------------------------------------
# 1. Open Julia from command line or Start menu
# 2. Press ] to enter package mode (prompt changes to pkg>)
# 3. Type these commands one at a time:
#      add HTTP
#      add JSON3
#      add Plots
#      add StaticArrays
#      add WaterLily
#      add ParametricBodies
# 4. Press Backspace to exit package mode
# 5. Type: using Pkg; Pkg.precompile()
# 6. Close Julia
#
# INSTALLING JULIA (if not installed):
# ------------------------------------
# 1. Go to https://julialang.org/downloads/
# 2. Download Julia 1.10 or newer (1.11.7 recommended)
# 3. Run the installer
# 4. Default install location: %LocalAppData%\Programs\Julia-1.11.7\
# 5. After installation, run this script or install packages manually
#
# ============================================================

println("=" ^ 60)
println("LilyPad-GH Package Installer")
println("=" ^ 60)
println()
println("This will install the required Julia packages for LilyPad-GH.")
println("Packages will be installed to: $(DEPOT_PATH[1])")
println()
println("If this script fails, see the comments at the top of this file")
println("for manual installation instructions.")
println()

using Pkg

# List of required packages
required_packages = [
    "HTTP",
    "JSON3",
    "Plots",
    "StaticArrays",
    "WaterLily",
    "ParametricBodies"
]

println("Required packages:")
for pkg in required_packages
    println("  - $pkg")
end
println()

println("Installing packages... (this may take several minutes on first run)")
println()

# Track success/failure
failed_packages = String[]

# Install each package
for pkg in required_packages
    println("Installing $pkg...")
    try
        Pkg.add(pkg)
        println("  [OK] $pkg installed successfully")
    catch e
        println("  [FAIL] Error installing $pkg: $e")
        push!(failed_packages, pkg)
    end
end

println()
println("=" ^ 60)
println("Precompiling packages...")
println("=" ^ 60)

# Precompile to speed up first run
try
    Pkg.precompile()
    println("[OK] Precompilation complete")
catch e
    println("[WARN] Precompilation warning: $e")
end

println()
println("=" ^ 60)

if isempty(failed_packages)
    println("Installation complete!")
    println("=" ^ 60)
    println()
    println("You can now use LilyPad-GH in Grasshopper.")
else
    println("Installation completed with errors!")
    println("=" ^ 60)
    println()
    println("The following packages failed to install:")
    for pkg in failed_packages
        println("  - $pkg")
    end
    println()
    println("Please install them manually. Open Julia and run:")
    println()
    println("  using Pkg")
    for pkg in failed_packages
        println("  Pkg.add(\"$pkg\")")
    end
    println()
end

println("Press Enter to exit...")
readline()
