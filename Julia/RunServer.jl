# ========================================
# FILE: RunServer.jl
# DESC: HTTP server with real-time frame output for UI display
# --- REVISIONS ---
# - 2025-09-22: Added real-time frame writing for UI display.
#   - Modified to write individual PNG frames to temp file during simulation.
#   - Maintains original GIF saving functionality alongside real-time display.
#   - Enhanced error handling for frame writing operations.
# ========================================

# PART 1: Auto Package Installation (same as before)
println("ðŸ”§ LilyPadGH Server - Checking environment...")

using Pkg

const REQUIRED_PACKAGES = [
    "StaticArrays", "Plots", "JSON3", "HTTP", "Interpolations", "GeometryBasics"
]

const SPECIAL_PACKAGES = ["WaterLily", "ParametricBodies"]

function ensure_packages_installed()
    try
        project_file = joinpath(@__DIR__, "Project.toml")
        if isfile(project_file)
            println("ðŸ“ Activating project environment...")
            Pkg.activate(@__DIR__)
        end

        for pkg in REQUIRED_PACKAGES
            try
                @eval using $(Symbol(pkg))
            catch
                println("â¬‡ï¸  Installing $pkg...")
                Pkg.add(pkg)
            end
        end

        for pkg in SPECIAL_PACKAGES
            try
                @eval using $(Symbol(pkg))
            catch
                println("â¬‡ï¸  Installing $pkg...")
                Pkg.add(pkg)
            end
        end

        println("âœ… All packages ready!")
        return true
    catch e
        println("âŒ Package installation failed: $e")
        return false
    end
end

if !ensure_packages_installed()
    println("ðŸš¨ Could not install required packages.")
    exit(1)
end

# PART 2: Load Simulation Code with Real-time Modifications
include("2d.jl")
using HTTP

# PART 3: Global Server State and Real-time Display
const SERVER_RUNNING = Ref(true)
const REALTIME_ACTIVE = Ref(false)
const UI_FRAME_PATH = Ref{String}("")

# Custom simulation function with real-time frame writing
function run_sim_with_realtime(raw_string::AbstractString; mem=Array)
    println("ðŸŽ¬ Starting simulation with real-time display...")
    
    # Parse JSON and extract UI frame path if provided
    local L, U, Re, anim_duration, plot_body, gif_obj, gif_filename
    local ui_frame_path = ""
    
    try
        data = JSON3.read(raw_string)
        params = data.simulation_parameters

        # Extract parameters
        L = get(params, :L, 32)
        U = get(params, :U, 1.0)
        Re = get(params, :Re, 250.0)
        anim_duration = get(params, :animation_duration, 1.0)
        plot_body = get(params, :plot_body, true)
        
        # Check for UI frame path
        if haskey(data, "ui_frame_path")
            ui_frame_path = string(data.ui_frame_path)
            UI_FRAME_PATH[] = ui_frame_path
            REALTIME_ACTIVE[] = true
            println("ðŸ“º Real-time display enabled: $ui_frame_path")
        end

        println("ðŸŽ›ï¸  Using simulation parameters:")
        println("   L = $L, U = $U, Re = $Re")
        println("   Animation duration = $anim_duration s, Plot body = $plot_body")

    catch e
        println("âŒ˜ Error parsing parameters: $e")
        L, U, Re = 32, 1.0, 250.0
        anim_duration, plot_body = 1.0, true
        println("ðŸ”§ Using fallback parameters: L=$L, U=$U, Re=$Re")
    end

    # Create simulation
    println("ðŸŽ¯ Creating simulation with: L=$L, U=$U, Re=$Re")
    sim = make_sim(raw_string; L=L, U=U, Re=Re, mem=mem)

    # Generate unique GIF filename
    timestamp = Dates.format(now(), "yyyymmdd_HHMMSS")
    gif_filename = "simulation_$(timestamp).gif"

    # Create the GIF with real-time frame callback
    if REALTIME_ACTIVE[]
        gif_obj = sim_gif_with_realtime!(sim, ui_frame_path, duration=anim_duration, clims=(-10,10), plotbody=plot_body)
    else
        gif_obj = sim_gif!(sim, duration=anim_duration, clims=(-10,10), plotbody=plot_body)
    end

    # Handle GIF saving (same as before)
    actual_path = gif_obj.filename
    package_dir = ENV["APPDATA"] * "\\McNeel\\Rhinoceros\\packages\\8.0\\LilyPadGH\\0.0.1"
    temp_dir = joinpath(package_dir, "Temp")

    if !isdir(temp_dir)
        mkpath(temp_dir)
    end

    final_gif_path = joinpath(temp_dir, gif_filename)
    try
        mv(actual_path, final_gif_path)
        actual_path = final_gif_path
    catch e
        println("Warning: Could not move GIF to temp folder: $e")
    end

    # Open the GIF automatically
    try
        if Sys.iswindows()
            run(`cmd /c start "" "$actual_path"`, wait=false)
        elseif Sys.isapple()
            run(`open $actual_path`, wait=false)
        else
            run(`xdg-open $actual_path`, wait=false)
        end
        println("âœ… GIF saved and opened: $actual_path")
    catch e
        println("âŒ˜ Could not automatically open GIF viewer: $e")
        println("ðŸ“ GIF saved at: $actual_path")
    end

    # Clean up real-time state
    REALTIME_ACTIVE[] = false
    
    return actual_path
end

# Custom sim_gif! function with real-time frame writing
function sim_gif_with_realtime!(sim, ui_frame_path; duration=1, clims=(-1,1), plotbody=true)
    println("ðŸŽ¥ Starting real-time simulation with frame output...")
    
    # Set plotting backend for headless operation
    ENV["GKSwstype"] = "100"
    gr()
    
    tâ‚€ = sim_time(sim)
    
    # Create frames array for final GIF
    frames = []
    frame_count = 0
    
    # Determine time step and frame interval
    dt = 0.1  # Time step for frames
    total_steps = Int(ceil(duration / dt))
    
    for i = 1:total_steps
        # Advance simulation
        sim_step!(sim, dt)
        
        # Create visualization
        @inside sim.flow.Ïƒ[I] = WaterLily.Ï‰_mag(I, sim.flow.u)
        
        # Create plot
        p = plot(
            sim.flow.Ïƒ[inside(sim.flow.Ïƒ)], 
            clims=clims,
            aspect_ratio=:equal,
            title="Flow Simulation t=$(round(sim_time(sim)-tâ‚€, digits=2))s",
            showaxis=false,
            size=(400, 300)
        )
        
        if plotbody && hasmethod(plot!, (typeof(p), typeof(sim.body)))
            try
                plot!(p, sim.body, fillcolor=:black)
            catch
                # Fallback if body plotting fails
            end
        end
        
        # Save frame for real-time display
        if REALTIME_ACTIVE[] && !isempty(ui_frame_path)
            try
                savefig(p, ui_frame_path)
                frame_count += 1
            catch e
                println("âš ï¸ Frame write error: $e")
            end
        end
        
        # Store frame for final GIF
        push!(frames, p)
        
        # Print progress
        if i % 5 == 0
            progress = round(100 * i / total_steps, digits=1)
            println("ðŸ“ˆ Progress: $progress% (frame $i/$total_steps)")
        end
    end
    
    println("ðŸŽ¬ Creating final GIF animation...")
    
    # Create final GIF from all frames
    gif_anim = @animate for frame in frames
        frame
    end
    
    REALTIME_ACTIVE[] = false
    println("âœ… Real-time simulation completed with $frame_count frames")
    
    return gif_anim
end

# PART 4: HTTP Request Handlers
function handle_request(req::HTTP.Request)
    println("ðŸ“¨ Received $(req.method) request to $(req.target)")

    if req.method == "POST" && (req.target == "/" || req.target == "/process")
        try
            msg = String(req.body)
            println("ðŸ“„ Processing simulation request...")

            # Check if this is a real-time simulation request
            if contains(msg, "ui_frame_path")
                gif_path = run_sim_with_realtime(msg)
            else
                gif_path = run_sim(msg)
            end

            return HTTP.Response(200, "Simulation completed. GIF saved at: $gif_path")

        catch e
            println("âŒ˜ Error processing request: $e")
            return HTTP.Response(400, "Error processing simulation: $(e)")
        end

    elseif req.method == "GET" && req.target == "/status"
        return HTTP.Response(200, "Server is running on port 8080")

    elseif req.method == "GET" && req.target == "/shutdown"
        println("ðŸ›‘ Shutdown requested")
        SERVER_RUNNING[] = false
        REALTIME_ACTIVE[] = false
        return HTTP.Response(200, "Server shutting down...")

    else
        println("â“ Unknown endpoint: $(req.method) $(req.target)")
        return HTTP.Response(404, "Unknown endpoint. Use POST / or GET /status or GET /shutdown")
    end
end

# PART 5: Server Startup
println("ðŸš€ Starting Julia HTTP server on localhost:8080")
println("ðŸ“‹ Available endpoints:")
println("  POST /        - Process simulation data")
println("  POST /process - Process simulation data (alternative)")
println("  GET /status   - Check server status")
println("  GET /shutdown - Shutdown server")

try
    server = HTTP.serve!(handle_request, "127.0.0.1", 8080; verbose=false)

    while SERVER_RUNNING[]
        sleep(0.1)
    end

    println("ðŸ”„ Stopping server...")
    close(server)
    println("âœ… Server stopped")

catch e
    println("âŒ˜ Server error: $e")
    exit(1)
end