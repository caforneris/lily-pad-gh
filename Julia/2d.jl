# ========================================
# FILE: 2d.jl
# DESC: WaterLily CFD simulation with JSON-driven geometry and UI integration.
#       Saves final GIF to specified path for in-UI display.
# --- REVISIONS ---
# - 2025-10-10: Updated GIF handling for UI integration.
#   - Accepts ui_gif_path from JSON to save GIF directly to C# dialog location.
#   - Removed external GIF viewer launching - display happens in C# UI.
#   - Maintains live PNG frame output for real-time display.
# ========================================

using WaterLily,StaticArrays,Plots
using JSON3, ParametricBodies, Dates

# =======================
# Part 1 — Configuration
# =======================

# Set GR backend to prevent GUI windows and subprocess spawning
ENV["GKSwstype"] = "100"  # Use GR headless mode
gr()

# ==================================
# Part 2 — Douglas-Peucker Algorithm
# ==================================

"""
    douglas_peucker(points_x, points_y, tolerance)

Simplifies a polyline using the Douglas-Peucker algorithm.
Returns indices of points to keep.
"""
function douglas_peucker(points_x::Vector{T}, points_y::Vector{T}, tolerance::T) where T
    n = length(points_x)
    if n <= 2
        return collect(1:n)  # Keep all points if 2 or fewer
    end

    # Find point with maximum distance from line between first and last
    max_dist = T(0)
    max_idx = 1

    # Line from first to last point
    x1, y1 = points_x[1], points_y[1]
    x2, y2 = points_x[n], points_y[n]

    # Check distance for each intermediate point
    for i in 2:(n-1)
        px, py = points_x[i], points_y[i]

        # Distance from point to line using formula: |ax + by + c| / sqrt(a² + b²)
        a = y2 - y1
        b = -(x2 - x1)
        c = x2*y1 - y2*x1

        dist = abs(a*px + b*py + c) / sqrt(a*a + b*b + T(1e-10))

        if dist > max_dist
            max_dist = dist
            max_idx = i
        end
    end

    # If max distance exceeds tolerance, recursively simplify
    if max_dist > tolerance
        left_indices = douglas_peucker(points_x[1:max_idx], points_y[1:max_idx], tolerance)
        right_indices = douglas_peucker(points_x[max_idx:n], points_y[max_idx:n], tolerance)

        # Combine results (avoid duplicating the middle point)
        result = left_indices
        for idx in right_indices[2:end]
            push!(result, max_idx - 1 + idx)
        end
        return result
    else
        # Keep only first and last points
        return [1, n]
    end
end

"""
    simplify_polyline_points(px, py, tolerance, max_points)

Simplifies a polyline using Douglas-Peucker, then enforces max point count.
"""
function simplify_polyline_points(px::Vector{T}, py::Vector{T}, tolerance::T, max_points::Int) where T
    # Apply Douglas-Peucker simplification
    keep_indices = douglas_peucker(px, py, tolerance)
    simplified_px = px[keep_indices]
    simplified_py = py[keep_indices]

    # Further reduce if max_points specified
    if length(simplified_px) > max_points
        # Keep every nth point to reach max_points
        step = max(1, length(simplified_px) ÷ max_points)
        reduced_indices = 1:step:length(simplified_px)
        # Always include last point
        if reduced_indices[end] != length(simplified_px)
            reduced_indices = vcat(collect(reduced_indices), length(simplified_px))
        end
        simplified_px = simplified_px[reduced_indices]
        simplified_py = simplified_py[reduced_indices]
    end

    return simplified_px, simplified_py
end

# ===========================
# Part 3 — Main Simulation Runner
# ===========================

"""
    run_sim(raw_string; mem=Array)

Executes the simulation with parameters and geometry from JSON.
Saves GIF to specified UI path for in-dialog display.
"""
function run_sim(raw_string::AbstractString; mem=Array)
    # Parse JSON to extract simulation parameters and UI paths
    local L, U, Re, anim_duration, plot_body, gif_obj, ui_gif_path

    try
        data = JSON3.read(raw_string)
        params = data.simulation_parameters

        # Extract simulation parameters with defaults
        L = get(params, :L, 32)
        U = get(params, :U, 1.0)
        Re = get(params, :Re, 250.0)
        anim_duration = get(params, :animation_duration, 1.0)
        plot_body = get(params, :plot_body, true)
        
        # CRITICAL: C# JsonSerializer uses camelCase, so we need to check both naming conventions
        ui_gif_path = ""
        if haskey(params, :uiGifPath)
            ui_gif_path = string(params[:uiGifPath])
        elseif haskey(params, :ui_gif_path)
            ui_gif_path = string(params[:ui_gif_path])
        end
        
        ui_frame_path = ""
        if haskey(params, :uiFramePath)
            ui_frame_path = string(params[:uiFramePath])
        elseif haskey(params, :ui_frame_path)
            ui_frame_path = string(params[:ui_frame_path])
        end
        
        # CRITICAL DEBUG - Force display what we received
        println("\n" * "="^80)
        println("🔍 CRITICAL DEBUG - PARAMETER EXTRACTION")
        println("="^80)
        println("Raw params object type: ", typeof(params))
        println("Has uiGifPath key (camelCase)? ", haskey(params, :uiGifPath))
        println("Has ui_gif_path key (snake_case)? ", haskey(params, :ui_gif_path))
        if haskey(params, :uiGifPath)
            println("Raw uiGifPath value: ", params[:uiGifPath])
            println("Raw value type: ", typeof(params[:uiGifPath]))
        end
        if haskey(params, :ui_gif_path)
            println("Raw ui_gif_path value: ", params[:ui_gif_path])
            println("Raw value type: ", typeof(params[:ui_gif_path]))
        end
        println("Final ui_gif_path string: \"$ui_gif_path\"")
        println("UI path is empty? ", isempty(ui_gif_path))
        println("UI path length: ", length(ui_gif_path))
        println("="^80)
        println("🎛️  SIMULATION PARAMETERS:")
        println("   L = $L, U = $U, Re = $Re")
        println("   Animation duration = $anim_duration s")
        println("   Plot body = $plot_body")
        println("="^80 * "\n")

    catch e
        println("❌ Error parsing parameters: $e")
        println("📋 Raw JSON string preview: $(first(raw_string, min(200, length(raw_string))))...")
        # Fallback with default parameters
        L, U, Re = 32, 1.0, 250.0
        anim_duration, plot_body = 1.0, true
        ui_gif_path = ""
        println("🔧 Using fallback parameters: L=$L, U=$U, Re=$Re")
    end

    # Create simulation with parameters from JSON
    println("🎯 Creating simulation with: L=$L, U=$U, Re=$Re")
    sim = make_sim(raw_string; L=L, U=U, Re=Re, mem=mem)

    # Create the GIF (sim_gif! returns an AnimatedGif object)
    gif_obj = sim_gif!(sim, duration=anim_duration, clims=(-10,10), plotbody=plot_body)

    # Determine final GIF path
    actual_path = gif_obj.filename
    
    if !isempty(ui_gif_path)
        # Move GIF to UI-specified path for in-dialog display
        try
            # Ensure directory exists
            ui_dir = dirname(ui_gif_path)
            if !isdir(ui_dir)
                mkpath(ui_dir)
            end
            
            # Wait a moment to ensure GIF is fully written
            sleep(0.5)
            
            # Copy instead of move to avoid file locking issues
            cp(actual_path, ui_gif_path, force=true)
            
            # Delete original temp file
            try
                rm(actual_path)
            catch
                println("⚠️  Could not delete original temp file")
            end
            
            actual_path = ui_gif_path
            println("✅ GIF saved to UI path: $actual_path")
            println("🚫 External viewer disabled - GIF will display in C# UI")
            
            # Return without opening external viewer
            return actual_path
        catch e
            println("⚠️  Could not copy GIF to UI path: $e")
            println("📁 GIF remains at: $actual_path")
        end
    end
    
    # Only open external viewer if NO UI path was provided (fallback mode)
    println("⚠️  No UI path provided - opening external viewer (legacy mode)")
    
    # Fallback: save to package temp folder (legacy behaviour)
    timestamp = Dates.format(now(), "yyyymmdd_HHMMSS")
    gif_filename = "simulation_$(timestamp).gif"
    
    package_dir = ENV["APPDATA"] * "\\McNeel\\Rhinoceros\\packages\\8.0\\LilyPadGH\\0.0.1"
    temp_dir = joinpath(package_dir, "Temp")

    if !isdir(temp_dir)
        mkpath(temp_dir)
    end

    final_gif_path = joinpath(temp_dir, gif_filename)
    try
        mv(actual_path, final_gif_path, force=true)
        actual_path = final_gif_path
    catch e
        println("Warning: Could not move GIF to temp folder: $e")
    end
    
    # Open external viewer only in fallback mode
    try
        if Sys.iswindows()
            run(`cmd /c start "" "$actual_path"`, wait=false)
        elseif Sys.isapple()
            run(`open $actual_path`, wait=false)
        else
            run(`xdg-open $actual_path`, wait=false)
        end
        println("✅ GIF opened in external viewer: $actual_path")
    catch e
        println("❌ Could not open external viewer: $e")
        println("📁 GIF saved at: $actual_path")
    end

    return actual_path
end

# ============================
# Part 4 — Simulation Setup
# ============================

"""
    make_sim(raw_string; L, U, Re, mem, T)

Creates a WaterLily simulation with custom geometry from JSON.
Handles multiple polylines with point simplification.
"""
function make_sim(raw_string::AbstractString; L=2^5,U=1,Re=100,mem=Array,T=Float32)

    println("running")

    # Extract point reduction parameters from JSON
    local simplify_tolerance, max_points_per_poly
    try
        data = JSON3.read(raw_string)
        if haskey(data, :simulation_parameters)
            params = data.simulation_parameters
            simplify_tolerance = T(get(params, :simplify_tolerance, 0.0))  # No simplification by default
            max_points_per_poly = get(params, :max_points_per_poly, 1000)  # Very high limit by default
        else
            simplify_tolerance = T(0.0)
            max_points_per_poly = 1000
        end
        println("🔧 Point reduction: tolerance=$simplify_tolerance, max_points=$max_points_per_poly")
    catch e
        println("⚠️  Could not extract point reduction params: $e")
        simplify_tolerance = T(0.0)
        max_points_per_poly = 1000
    end

    # Try to load custom JSON body, fallback to circle
    body = try
        script_dir = @__DIR__
        curve_data = JSON3.read(raw_string)

        @show typeof(curve_data)
        @show propertynames(curve_data)

        # Check for new multiple polylines schema
        if haskey(curve_data, "polylines") && !isempty(curve_data["polylines"])
            polylines = curve_data["polylines"]
            println("Loading custom JSON body with $(length(polylines)) polylines")

            # Process all polylines with simplification
            all_px = T[]
            all_py = T[]
            simplified_polylines = []
            total_original = 0
            total_simplified = 0

            for polyline in polylines
                if haskey(polyline, "points") && !isempty(polyline["points"])
                    px_orig = [T(p["x"]) for p in polyline["points"]]
                    py_orig = [T(p["y"]) for p in polyline["points"]]
                    total_original += length(px_orig)

                    # Simplify the polyline using parameters from JSON
                    if simplify_tolerance > 0.0 && max_points_per_poly < length(px_orig)
                        px, py = simplify_polyline_points(px_orig, py_orig, simplify_tolerance, max_points_per_poly)
                        println("  📉 Simplified polyline: $(length(px_orig)) → $(length(px)) points")
                    else
                        px, py = px_orig, py_orig
                        println("  ✅ No simplification: keeping all $(length(px)) points")
                    end
                    total_simplified += length(px)

                    push!(simplified_polylines, (px, py))
                    append!(all_px, px)
                    append!(all_py, py)
                end
            end

            println("Simplified: $total_original points → $total_simplified points ($(round(100*total_simplified/total_original, digits=1))% retained)")

            if isempty(all_px)
                throw("No points found in any polyline")
            end

            x_min, x_max = extrema(all_px)
            y_min, y_max = extrema(all_py)

            # Scale to simulation domain and center
            domain_width = 8L
            domain_height = 4L

            # Use 30% of domain for object size
            scale_x = (domain_width * 0.3) / (x_max - x_min)
            scale_y = (domain_height * 0.3) / (y_max - y_min)
            scale = T(min(scale_x, scale_y))

            cx = T(domain_width / 2)
            cy = T(domain_height / 2)

            println("🔧 Scaling: object bounds ($x_min,$y_min) to ($x_max,$y_max)")
            println("🔧 Domain: $(domain_width)×$(domain_height), scale factor: $scale")

            # Create optimized SDF function for multiple polylines (union)
            @fastmath @inline function multi_poly_sdf(x, t)
                px_sim = x[1]
                py_sim = x[2]

                # Transform back to original coordinates
                px_orig = (px_sim - cx) / scale + (x_min + x_max) / 2
                py_orig = (py_sim - cy) / scale + (y_min + y_max) / 2

                min_dist_to_any = T(Inf)

                # Process each simplified polyline
                @inbounds for (px, py) in simplified_polylines

                    # Distance to this polygon boundary
                    min_dist = T(Inf)
                    n = length(px)
                    @inbounds for i in 1:n
                        j = i == n ? 1 : i + 1
                        ax, ay = px[i], py[i]
                        bx, by = px[j], py[j]

                        vx, vy = bx - ax, by - ay
                        wx, wy = px_orig - ax, py_orig - ay

                        t_seg = max(T(0), min(T(1), (wx*vx + wy*vy) / (vx*vx + vy*vy + T(1e-10))))
                        closest_x = ax + t_seg * vx
                        closest_y = ay + t_seg * vy

                        dist = sqrt((px_orig - closest_x)^2 + (py_orig - closest_y)^2)
                        min_dist = min(min_dist, dist)
                    end

                    # Ray casting for inside/outside test
                    inside = false
                    j = n
                    @inbounds for i in 1:n
                        if ((py[i] > py_orig) != (py[j] > py_orig)) &&
                           (px_orig < (px[j] - px[i]) * (py_orig - py[i]) / (py[j] - py[i]) + px[i])
                            inside = !inside
                        end
                        j = i
                    end

                    poly_sdf = inside ? -min_dist : min_dist
                    min_dist_to_any = min(min_dist_to_any, poly_sdf)
                end

                return min_dist_to_any
            end

            AutoBody(multi_poly_sdf)

        else
            throw("No curve data found in JSON")
        end
    catch e
        println("Warning: Failed to load custom body ($(e)), using circle fallback")
        R = L/8
        AutoBody((x,t) -> hypot(x[1]-4L, x[2]-2L) - R)
    end

    # Create and return simulation
    Simulation((8L,4L),(U,0),L;U,ν=U*L/Re,body,T,mem)
end