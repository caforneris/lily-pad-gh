# ========================================
# FILE: RunServer.jl
# DESC: HTTP server with real-time frame output for UI display.
#       Handles package installation, simulation orchestration, and HTTP request routing.
# ========================================

# =======================
# Region: Package Installation
# =======================
#region Package Installation

println("🔧 LilyPadGH Server - Checking environment...")

using Pkg

const REQUIRED_PACKAGES = [
    "StaticArrays", "Plots", "JSON3", "HTTP", "Interpolations", "GeometryBasics"
]

const SPECIAL_PACKAGES = ["WaterLily", "ParametricBodies"]

# Note: Ensures all required packages are installed before starting the server.
# Activates project environment if Project.toml exists, otherwise installs to global environment.
function ensure_packages_installed()
    try
        project_file = joinpath(@__DIR__, "Project.toml")
        if isfile(project_file)
            println("📁 Activating project environment...")
            Pkg.activate(@__DIR__)
        end

        for pkg in REQUIRED_PACKAGES
            try
                @eval using $(Symbol(pkg))
            catch
                println("⬇️ Installing $pkg...")
                Pkg.add(pkg)
            end
        end

        for pkg in SPECIAL_PACKAGES
            try
                @eval using $(Symbol(pkg))
            catch
                println("⬇️ Installing $pkg...")
                Pkg.add(pkg)
            end
        end

        println("✅ All packages ready!")
        return true
    catch e
        println("❌ Package installation failed: $e")
        return false
    end
end

if !ensure_packages_installed()
    println("🛑 Could not install required packages.")
    exit(1)
end

#endregion

# =======================
# Region: Load Simulation Code
# =======================
#region Load Simulation Code

# Note: Includes the main simulation logic from 2d.jl.
# This provides the run_sim() and make_sim() functions.
include("2d.jl")
using HTTP

#endregion

# =======================
# Region: Global Server State
# =======================
#region Global Server State

# Note: Ref for thread-safe server shutdown control.
# Set to false via /shutdown endpoint to gracefully stop the server.
const SERVER_RUNNING = Ref(true)

#endregion

# =======================
# Region: HTTP Request Handlers
# =======================
#region HTTP Request Handlers

# Note: Main HTTP request router.
# Handles simulation requests, status checks, and shutdown commands.
function handle_request(req::HTTP.Request)
    println("📨 Received $(req.method) request to $(req.target)")

    if req.method == "POST" && (req.target == "/" || req.target == "/process")
        try
            msg = String(req.body)
            println("📄 Processing simulation request...")
            println("📏 Request body length: $(length(msg)) characters")

            # CRITICAL: Call run_sim with the full JSON payload.
            # This function handles all parameter extraction, simulation, and GIF generation.
            gif_path = run_sim(msg)

            # Note: Try to send HTTP response, but don't fail if client has timed out.
            # Long simulations may exceed HTTP timeout, but simulation completes successfully anyway.
            try
                return HTTP.Response(200, "Simulation completed. GIF saved at: $gif_path")
            catch e
                println("⚠️ Could not send HTTP response (client may have timed out): $e")
                println("✅ Simulation completed successfully anyway. GIF saved at: $gif_path")
                return HTTP.Response(200, "")
            end

        catch e
            println("❌ Error processing request: $e")
            bt = catch_backtrace()
            println("Stacktrace:")
            Base.show_backtrace(stdout, bt)
            println()
            
            try
                return HTTP.Response(400, "Error processing simulation: $(e)")
            catch
                return HTTP.Response(400, "")
            end
        end

    elseif req.method == "GET" && req.target == "/status"
        return HTTP.Response(200, "Server is running on port 8080")

    elseif req.method == "GET" && req.target == "/shutdown"
        println("🛑 Shutdown requested")
        SERVER_RUNNING[] = false
        return HTTP.Response(200, "Server shutting down...")

    else
        println("❓ Unknown endpoint: $(req.method) $(req.target)")
        return HTTP.Response(404, "Unknown endpoint. Use POST / or GET /status or GET /shutdown")
    end
end

#endregion

# =======================
# Region: Server Startup
# =======================
#region Server Startup

println("🚀 Starting Julia HTTP server on localhost:8080")
println("📋 Available endpoints:")
println("  POST /        - Process simulation data")
println("  POST /process - Process simulation data (alternative)")
println("  GET /status   - Check server status")
println("  GET /shutdown - Shutdown server")

try
    # Note: Suppress HTTP connection error logging for timeout scenarios.
    # The simulation completes successfully even if HTTP response fails due to long duration.
    server = HTTP.serve!(handle_request, "127.0.0.1", 8080; verbose=false, access_log=nothing)

    while SERVER_RUNNING[]
        sleep(0.1)
    end

    println("🛑 Stopping server...")
    close(server)
    println("✅ Server stopped")

catch e
    println("❌ Server error: $e")
    exit(1)
end

#endregion