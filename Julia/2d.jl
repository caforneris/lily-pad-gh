# ========================================
# FILE: 2d.jl
# DESC: WaterLily CFD simulation with JSON-driven geometry and UI integration.
# --- REVISIONS ---
# - 2025-11-04 (CRITICAL FIX):
#   - `sim_gif!` is asynchronous. The script was moving the GIF *while it was
#     still being written*, causing a corrupt file and the C# GDI+ error.
#   - Added `wait_for_file_completion` function to poll the temp GIF.
# - 2025-11-04 (ATOMIC FIX):
#   - Re-implemented two-stage atomic move to handle cross-volume copy
#     from `AppData\Local` (Julia temp) to `AppData\Roaming` (C# temp).
# - 2025-11-04 (MULTITHREADING FIX 4 - DEFINITIVE):
#   - Fixed `CompositeException` (TaskFailedException) crash.
#   - My previous `struct` fix was buggy (used a non-concrete Vector-of-Tuples).
#   - Created a new *concrete* `struct SimplePolyline{T}`.
#   - The main `MultiPolySDF` functor now holds a `Vector{SimplePolyline{T}}`.
#   - This is fully concrete, thread-safe, and performant. This will
#     fix the crash and make the custom geometry render.
# ========================================

using WaterLily, StaticArrays, Plots
using JSON3, ParametricBodies, Dates

# =======================
# Region: Configuration
# =======================
#region Configuration

ENV["GKSwstype"] = "100"
gr()

#endregion

# ==================================
# Region: Douglas-Peucker Algorithm
# ==================================
#region Douglas-Peucker Algorithm

# Note: Polyline simplification using perpendicular distance tolerance.
function douglas_peucker(points_x::Vector{T}, points_y::Vector{T}, tolerance::T) where T
    n = length(points_x)
    if n <= 2
        return collect(1:n)
    end

    max_dist = T(0)
    max_idx = 1

    x1, y1 = points_x[1], points_y[1]
    x2, y2 = points_x[n], points_y[n]

    for i in 2:(n-1)
        px, py = points_x[i], points_y[i]
        a = y2 - y1
        b = -(x2 - x1)
        c = x2*y1 - y2*x1
        dist = abs(a*px + b*py + c) / sqrt(a*a + b*b + T(1e-10))

        if dist > max_dist
            max_dist = dist
            max_idx = i
        end
    end

    if max_dist > tolerance
        left_indices = douglas_peucker(points_x[1:max_idx], points_y[1:max_idx], tolerance)
        right_indices = douglas_peucker(points_x[max_idx:n], points_y[max_idx:n], tolerance)

        result = left_indices
        for idx in right_indices[2:end]
            push!(result, max_idx - 1 + idx)
        end
        return result
    else
        return [1, n]
    end
end

# Note: Combines Douglas-Peucker simplification with point count limiting.
function simplify_polyline_points(px::Vector{T}, py::Vector{T}, tolerance::T, max_points::Int) where T
    keep_indices = douglas_peucker(px, py, tolerance)
    simplified_px = px[keep_indices]
    simplified_py = py[keep_indices]

    if length(simplified_px) > max_points
        step = max(1, length(simplified_px) ÷ max_points)
        reduced_indices = 1:step:length(simplified_px)
        if reduced_indices[end] != length(simplified_px)
            reduced_indices = vcat(collect(reduced_indices), length(simplified_px))
        end
        simplified_px = simplified_px[reduced_indices]
        simplified_py = simplified_py[reduced_indices]
    end

    return simplified_px, simplified_py
end

#endregion

# ===========================
# Region: File Stability Helper
# ===========================
#region File Stability Helper

# Note: Polls a file path to ensure it's no longer being written to.
# This is critical because `sim_gif!` is async.
function wait_for_file_completion(file_path::String, timeout_seconds::Int = 60)
    println("  Waiting for GIF generation to complete...")
    println("    - Polling file: $file_path")
    
    check_interval = 0.2 # seconds
    stability_duration = 1.0 # seconds
    
    start_time = time()
    last_size = -1
    last_size_time = time()

    while time() - start_time < timeout_seconds
        if !isfile(file_path)
            sleep(check_interval)
            continue
        end

        try
            current_size = filesize(file_path)
            if current_size != last_size
                # File size has changed, reset timer
                last_size = current_size
                last_size_time = time()
                println("    - File size: $current_size bytes (writing...)")
            elseif current_size > 0 && (time() - last_size_time > stability_duration)
                # File size has been stable for 1.0s and is not empty
                println("    - File size stable at $current_size bytes.")
                println("  ✅ GIF generation complete.")
                return true
            end
        catch e
            # File might be temporarily locked, just ignore and retry
            println("    - File locked, retrying...")
        end
        
        sleep(check_interval)
    end

    println("  ⚠️ WARNING: File completion wait timed out after $timeout_seconds seconds.")
    return false
end

#endregion

# ===========================
# Region: Geometry Functor (Thread-Safe)
# ===========================
#region Geometry Functor

# --- MULTITHREADING FIX ---
# Define a concrete struct for a single polyline
struct SimplePolyline{T<:AbstractFloat}
    px::Vector{T}
    py::Vector{T}
    n::Int
end

# Define a callable struct (functor) to hold all geometry parameters.
# It holds a Vector of the concrete SimplePolyline struct. This is thread-safe.
struct MultiPolySDF{T<:AbstractFloat, V<:AbstractVector{SimplePolyline{T}}}
    polylines::V
    scale::T
    cx::T
    cy::T
    x_center::T
    y_center::T
    _T_0::T
    _T_1::T
    _T_inf::T
    _T_eps::T
end

# Make the struct callable for WaterLily's AutoBody
@fastmath function (sdf::MultiPolySDF)(x, t)
    px_sim = x[1]
    py_sim = x[2]

    # Unpack values from the struct
    px_orig = (px_sim - sdf.cx) / sdf.scale + sdf.x_center
    py_orig = (py_sim - sdf.cy) / sdf.scale + sdf.y_center

    min_dist_to_any = sdf._T_inf # Use pre-calculated value

    @inbounds for poly in sdf.polylines
        
        min_dist = sdf._T_inf # Use pre-calculated value
        
        # Unpack the simple polyline
        px = poly.px
        py = poly.py
        n = poly.n

        @inbounds for i in 1:n
            j = i == n ? 1 : i + 1
            ax, ay = px[i], py[i]
            bx, by = px[j], py[j]

            vx, vy = bx - ax, by - ay
            wx, wy = px_orig - ax, py_orig - ay

            # Use pre-calculated values
            t_seg = max(sdf._T_0, min(sdf._T_1, (wx*vx + wy*vy) / (vx*vx + vy*vy + sdf._T_eps)))
            closest_x = ax + t_seg * vx
            closest_y = ay + t_seg * vy

            dist = sqrt((px_orig - closest_x)^2 + (py_orig - closest_y)^2)
            min_dist = min(min_dist, dist)
        end

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

#endregion

# ===========================
# Region: Main Simulation Runner
# ===========================
#region Main Simulation Runner

# Note: Orchestrates the entire simulation pipeline from JSON parsing to GIF generation.
function run_sim(raw_string::AbstractString; mem=Array)
    println("\n" * "="^80)
    println("🔍 RAW JSON RECEIVED FROM C#")
    println("="^80)
    println(raw_string)
    println("="^80 * "\n")

    # CRITICAL: Default values for all parameters in case JSON parsing fails.
    inlet_velocity = 1.0
    reynolds_number = 250.0
    grid_res_x = 192
    grid_res_y = 128
    domain_width = 2.0
    domain_height = 1.0
    characteristic_length = 0.01
    kinematic_viscosity = 1.004e-6
    time_step = 0.01
    total_time = 10.0
    color_scale_min = -10.0
    color_scale_max = 10.0
    show_body = true
    waterlily_L = 32
    simplify_tolerance = 0.0
    max_points_per_poly = 1000
    object_scale_factor = 0.3
    ui_gif_path = ""
    sim_precision = "Float32"

    try
        data = JSON3.read(raw_string)
        if haskey(data, :simulation_parameters)
            params = data.simulation_parameters

            println("\n" * "="^80)
            println("📊 COMPREHENSIVE PARAMETER EXTRACTION")
            println("="^80)
            println("All keys in params: ", collect(keys(params)))

            ui_gif_path = ""
            possible_keys = [:ui_gif_path, :uiGifPath, "ui_gif_path", "uiGifPath"]
            for key in possible_keys
                if haskey(params, key)
                    ui_gif_path = string(params[key])
                    println("✅ Found UI GIF path using key: $key → \"$ui_gif_path\"")
                    break
                end
            end
            if isempty(ui_gif_path)
                println("⚠ UI GIF path NOT FOUND in any format - will use external viewer")
            end

            inlet_velocity = Float64(get(params, :inlet_velocity, 1.0))
            reynolds_number = Float64(get(params, :reynolds_number, 250.0))
            kinematic_viscosity = Float64(get(params, :kinematic_viscosity, 1.004e-6))
            domain_width = Float64(get(params, :domain_width, 2.0))
            domain_height = Float64(get(params, :domain_height, 1.0))
            grid_res_x = Int(get(params, :grid_resolution_x, 192))
            grid_res_y = Int(get(params, :grid_resolution_y, 128))
            characteristic_length = Float64(get(params, :characteristic_length, 0.01))
            time_step = Float64(get(params, :time_step, 0.01))
            total_time = Float64(get(params, :total_simulation_time, 10.0))
            color_scale_min = Float64(get(params, :color_scale_min, -10.0))
            color_scale_max = Float64(get(params, :color_scale_max, 10.0))
            show_body = Bool(get(params, :show_body, true))
            simplify_tolerance = Float64(get(params, :simplify_tolerance, 0.0))
            max_points_per_poly = Int(get(params, :max_points_per_poly, 1000))
            object_scale_factor = Float64(get(params, :object_scale_factor, 0.3))
            waterlily_L = Int(get(params, :waterlily_l, 32))
            sim_precision = string(get(params, :precision_type, "Float32"))

            println("\n📊 EXTRACTED COMPREHENSIVE SETTINGS:")
            println("FLUID PROPERTIES:")
            println("  Inlet Velocity: $inlet_velocity m/s")
            println("  Kinematic Viscosity: $kinematic_viscosity m²/s")
            println("  Reynolds Number: $reynolds_number")
            println("\nDOMAIN CONFIGURATION:")
            println("  Physical Size: $(domain_width)m × $(domain_height)m")
            println("  Grid Resolution: $(grid_res_x) × $(grid_res_y) cells")
            println("  Characteristic Length: $characteristic_length m")
            println("\nTIME CONFIGURATION:")
            println("  Time Step: $time_step s")
            println("  ⚠️ CRITICAL - Total Simulation Time: $total_time seconds")
            println("\nVISUALIZATION:")
            println("  Color Scale: [$color_scale_min, $color_scale_max]")
            println("  Show Body: $show_body")
            println("\nADVANCED:")
            println("  WaterLily L: $waterlily_L")
            println("  Precision: $sim_precision")
            println("  Simplify Tolerance: $simplify_tolerance")
            println("  Max Points/Poly: $max_points_per_poly")
            println("  Object Scale: $(object_scale_factor*100)%")
            println("="^80 * "\n")
        else
            println("⚠️ WARNING: No simulation_parameters found in JSON, using defaults")
        end
    catch e
        println("❌ Error parsing JSON: $e")
        println("Using default parameters")
    end
    
    T_float = (sim_precision == "Float64") ? Float64 : Float32

    println("🎯 Creating simulation with user-defined parameters...")
    sim = make_sim(raw_string, T_float; # Pass the concrete type
        grid_x=grid_res_x,
        grid_y=grid_res_y,
        domain_width=domain_width,
        domain_height=domain_height,
        L=waterlily_L,
        U=inlet_velocity,
        Re=reynolds_number,
        ν=kinematic_viscosity,
        object_scale=object_scale_factor,
        simplify_tol=simplify_tolerance,
        max_points=max_points_per_poly,
        mem=mem)

    total_cells = grid_res_x * grid_res_y
    estimated_seconds_per_sim_second = total_cells < 10000 ? 0.02 :
                                        total_cells < 50000 ? 0.05 :
                                        total_cells < 100000 ? 0.1 : 0.2
    estimated_real_time = total_time * estimated_seconds_per_sim_second

    println("🎬 Generating animation (async)...")
    println("  ⚠️ CRITICAL - Simulation duration: $total_time seconds")
    println("  Grid cells: $total_cells")
    println("  Estimated real time: $(round(estimated_real_time, digits=1))s")

    gif_obj = sim_gif!(sim,
        duration=total_time,
        clims=(color_scale_min, color_scale_max),
        plotbody=show_body)

    temp_gif_path = gif_obj.filename

    # --- CRITICAL FIX ---
    # Wait for the async sim_gif! to finish writing before we move the file.
    wait_for_file_completion(temp_gif_path)
    # --- END FIX ---

    println("\n" * "="^80)
    println("🚦 GIF OUTPUT MODE SELECTION")
    println("="^80)
    println("ui_gif_path value: \"$ui_gif_path\"")
    println("Will use UI mode (no external viewer)? ", !isempty(ui_gif_path))
    println("="^80 * "\n")

    # Note: UI mode moves GIF to specified path for integrated display.
    # External mode moves GIF to package temp folder and opens system viewer.
    if !isempty(ui_gif_path)
        println("🎨 UI MODE ACTIVATED - NO EXTERNAL VIEWER")
        println("  Source: $temp_gif_path")

        try
            ui_dir = dirname(ui_gif_path)
            if !isdir(ui_dir)
                mkpath(ui_dir)
            end

            # --- ATOMIC FIX 2025-11-04 ---
            # Define a staging path in the same directory as the final path.
            staging_gif_path = ui_gif_path * ".staging"

            # 1. Move from temp (e.g., Local\Temp) to staging (e.g., Roaming\Temp).
            #    This is the slow, non-atomic part. C# is NOT watching this file.
            println("  Step 1 (Slow Copy): Moving from source to staging path...")
            println("    - $staging_gif_path")
            mv(temp_gif_path, staging_gif_path, force=true)

            # 2. Rename from staging to final path.
            #    This is an atomic (instantaneous) rename as it's in the same directory.
            #    C# will see the file appear at this exact moment, fully complete.
            println("  Step 2 (Atomic Rename): Moving from staging to final path...")
            println("    - $ui_gif_path")
            mv(staging_gif_path, ui_gif_path, force=true)
            # --- END ATOMIC FIX ---

            if isfile(ui_gif_path)
                println("✅ GIF moved successfully")
                println("🚫 EXTERNAL VIEWER DISABLED")
                return ui_gif_path
            else
                error("GIF move verification failed")
            end
        catch e
            println("❌ UI MODE FAILED: $e")
            println("Falling through to external viewer...")
        end
    end

    println("="^80)
    println("🖼️ FALLBACK MODE - EXTERNAL VIEWER")
    println("="^80)

    timestamp = Dates.format(now(), "yyyymmdd_HHMMSS")
    gif_filename = "simulation_$(timestamp).gif"
    
    # Note: This path is now only used for fallback mode.
    fallback_dir = joinpath(ENV["APPDATA"], "McNeel", "Rhinoceros", "packages", "8.0", "LilyPadGH", "0.0.1", "Temp")

    if !isdir(fallback_dir)
        mkpath(fallback_dir)
    end

    final_gif_path = joinpath(fallback_dir, gif_filename)
    try
        mv(temp_gif_path, final_gif_path, force=true)
    catch e
        println("Warning: Could not move GIF: $e")
        final_gif_path = temp_gif_path
    end

    try
        if Sys.iswindows()
            run(`cmd /c start "" "$final_gif_path"`, wait=false)
        elseif Sys.isapple()
            run(`open $final_gif_path`, wait=false)
        else
            run(`xdg-open $final_gif_path`, wait=false)
        end
        println("✅ External viewer opened: $final_gif_path")
    catch e
        println("❌ Could not open viewer: $e")
    end

    println("="^80)
    return final_gif_path
end

#endregion

# ============================
# Region: Simulation Setup
# ============================
#region Simulation Setup

function make_sim(raw_string::AbstractString, T::DataType; # Pass T as an argument
                  grid_x=192, grid_y=128,
                  domain_width=2.0, domain_height=1.0,
                  L=32, U=1.0, Re=100, ν=1.004e-6,
                  object_scale=0.3,
                  simplify_tol=0.0, max_points=1000,
                  mem=Array)

    println("🔧 Simulation Setup:")
    println("  Domain: $(domain_width)m × $(domain_height)m")
    println("  Grid: $grid_x × $grid_y cells")
    println("  Cell size: $(domain_width/grid_x)m × $(domain_height/grid_y)m")
    println("  Object scale: $(object_scale*100)% of domain")
    println("  Precision: $T")

    body = try
        curve_data = JSON3.read(raw_string)

        if haskey(curve_data, "polylines") && !isempty(curve_data["polylines"])
            polylines = curve_data["polylines"]
            println("Processing $(length(polylines)) polylines...")

            all_px = T[]
            all_py = T[]
            
            # Use concrete type for initialization
            simplified_polylines = Vector{SimplePolyline{T}}()
            
            total_original = 0
            total_simplified = 0

            for polyline in polylines
                if haskey(polyline, "points") && !isempty(polyline["points"])
                    px_orig = [T(p["x"]) for p in polyline["points"]]
                    py_orig = [T(p["y"]) for p in polyline["points"]]
                    total_original += length(px_orig)

                    if simplify_tol > 0.0 && max_points < length(px_orig)
                        px, py = simplify_polyline_points(px_orig, py_orig, T(simplify_tol), max_points)
                        println("  Simplified: $(length(px_orig)) → $(length(px)) points")
                    else
                        px, py = px_orig, py_orig
                    end
                    total_simplified += length(px)

                    push!(simplified_polylines, SimplePolyline(px, py, length(px))) # Push the new struct
                    append!(all_px, px)
                    append!(all_py, py)
                end
            end

            println("Total points: $total_original → $total_simplified")

            if isempty(all_px)
                throw("No points found")
            end

            x_min, x_max = extrema(all_px)
            y_min, y_max = extrema(all_py)

            scale_x = (grid_x * object_scale) / (x_max - x_min)
            scale_y = (grid_y * object_scale) / (y_max - y_min)
            scale = T(min(scale_x, scale_y))

            cx = T(grid_x / 2)
            cy = T(grid_y / 2)
            
            x_center = T((x_min + x_max) / 2)
            y_center = T((y_min + y_max) / 2)

            println("Scaling: $(round(scale, digits=3))x, centered at ($cx, $cy)")

            # --- MULTITHREADING FIX ---
            # Pre-calculate concrete values to avoid capturing the Type variable `T`
            _T_0 = T(0)
            _T_1 = T(1)
            _T_inf = T(Inf)
            _T_eps = T(1e-10)
            
            # Instantiate the thread-safe functor
            sdf_functor = MultiPolySDF(
                simplified_polylines,
                scale, cx, cy,
                x_center, y_center,
                _T_0, _T_1, _T_inf, _T_eps
            )

            AutoBody(sdf_functor)
            # --- END FIX ---
        else
            throw("No polylines")
        end
    catch e
        println("Using fallback circle: $e")
        R = min(grid_x, grid_y) / 8
        AutoBody((x,t) -> hypot(x[1]-grid_x/2, x[2]-grid_y/2) - R)
    end

    println("✅ Creating WaterLily simulation:")
    println("  Grid: $(grid_x)×$(grid_y)")
    println("  U: $U m/s")
    println("  ν: $ν m²/s")
    println("  Re: $Re")

    Simulation((grid_x, grid_y), (U, 0), L; U, ν, body, T, mem)
end

#endregion