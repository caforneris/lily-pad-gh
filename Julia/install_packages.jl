# ============================================================
# LilyPad-GH Package Installation Script
# Run this once to install required Julia packages
# ============================================================

println("=" ^ 60)
println("LilyPad-GH Package Installer")
println("=" ^ 60)
println()
println("This will install the required Julia packages for LilyPad-GH.")
println("Packages will be installed to: $(DEPOT_PATH[1])")
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

# Install each package
for pkg in required_packages
    println("Installing $pkg...")
    try
        Pkg.add(pkg)
        println("  ✓ $pkg installed successfully")
    catch e
        println("  ✗ Error installing $pkg: $e")
    end
end

println()
println("=" ^ 60)
println("Precompiling packages...")
println("=" ^ 60)

# Precompile to speed up first run
try
    Pkg.precompile()
    println("✓ Precompilation complete")
catch e
    println("⚠ Precompilation warning: $e")
end

println()
println("=" ^ 60)
println("Installation complete!")
println("=" ^ 60)
println()
println("You can now use LilyPad-GH in Grasshopper.")
println("Press Enter to exit...")
readline()
