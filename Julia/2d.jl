using WaterLily,StaticArrays,Plots
using JSON3, ParametricBodies, Dates

# Set GR backend to prevent GUI windows and subprocess spawning
ENV["GKSwstype"] = "100"  # Use GR headless mode
gr()

# ========== CONFIGURATION PARAMETERS ==========
const SIMPLIFY_TOLERANCE = 0.05   # Douglas-Peucker tolerance (higher = fewer points, faster)
const MAX_POINTS_PER_POLY = 10    # Maximum points per polyline (lower = faster)

# Douglas-Peucker algorithm for polyline simplification
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
        # Point to check
        px, py = points_x[i], points_y[i]

        # Distance from point to line
        a = y2 - y1
        b = -(x2 - x1)
        c = x2*y1 - y2*x1

        dist = abs(a*px + b*py + c) / sqrt(a*a + b*b + T(1e-10))

        if dist > max_dist
            max_dist = dist
            max_idx = i
        end
    end

    # If max distance is greater than tolerance, recursively simplify
    if max_dist > tolerance
        # Recursively simplify both parts
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

function run_sim(raw_string::AbstractString; mem=Array)
    # Parse JSON to extract simulation parameters
    local L, U, Re, anim_duration, plot_body, gif_obj, gif_filename

    try
        data = JSON3.read(raw_string)
        params = data.simulation_parameters

        # Extract parameters with defaults
        L = get(params, :L, 32)
        U = get(params, :U, 1.0)
        Re = get(params, :Re, 250.0)
        anim_duration = get(params, :animation_duration, 1.0)
        plot_body = get(params, :plot_body, true)

        println("🎛️  Using simulation parameters:")
        println("   L = $L, U = $U, Re = $Re")
        println("   Animation duration = $anim_duration s, Plot body = $plot_body")

    catch e
        println("❌ Error parsing parameters: $e")
        # Fallback with default parameters
        L, U, Re = 32, 1.0, 250.0
        anim_duration, plot_body = 1.0, true
        println("🔧 Using fallback parameters: L=$L, U=$U, Re=$Re")
    end

    # Create simulation with parameters from JSON
    println("🎯 Creating simulation with: L=$L, U=$U, Re=$Re")
    sim = make_sim(raw_string; L=L, U=U, Re=Re, mem=mem)

    # Generate a unique filename for the GIF and save to package temp folder
    timestamp = Dates.format(now(), "yyyymmdd_HHMMSS")
    gif_filename = "simulation_$(timestamp).gif"

    # Create the GIF (sim_gif! returns an AnimatedGif object)
    gif_obj = sim_gif!(sim, duration=anim_duration, clims=(-10,10), plotbody=plot_body)

    # Get the actual file path from the GIF object
    actual_path = gif_obj.filename

    # Move the GIF to the package temp folder
    package_dir = ENV["APPDATA"] * "\\McNeel\\Rhinoceros\\packages\\8.0\\LilyPadGH\\0.0.1"
    temp_dir = joinpath(package_dir, "Temp")

    # Create temp directory if it doesn't exist
    if !isdir(temp_dir)
        mkpath(temp_dir)
    end

    # Move file to temp directory
    final_gif_path = joinpath(temp_dir, gif_filename)
    try
        mv(actual_path, final_gif_path)
        actual_path = final_gif_path
    catch e
        println("Warning: Could not move GIF to temp folder: $e")
        println("GIF remains at: $actual_path")
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
        println("✅ GIF saved and opened: $actual_path")
    catch e
        println("❌ Could not automatically open GIF viewer: $e")
        println("📁 GIF saved at: $actual_path")
    end

    return actual_path
end

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
            simplify_tolerance = T(0.0)  # No simplification by default
            max_points_per_poly = 1000  # Very high limit by default
        end
        println("🔧 Point reduction: tolerance=$simplify_tolerance, max_points=$max_points_per_poly")
    catch e
        println("⚠️  Could not extract point reduction params: $e")
        simplify_tolerance = T(0.0)  # No simplification by default
        max_points_per_poly = 1000  # Very high limit by default
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

            # Scale to simulation domain and center - less aggressive scaling
            domain_width = 8L
            domain_height = 4L

            # Use more reasonable scaling that preserves object size better
            scale_x = (domain_width * 0.3) / (x_max - x_min)  # Use 30% of domain width
            scale_y = (domain_height * 0.3) / (y_max - y_min)  # Use 30% of domain height
            scale = T(min(scale_x, scale_y))  # Use smaller scale to fit both dimensions

            cx = T(domain_width / 2)   # center x
            cy = T(domain_height / 2)  # center y

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
                        # Distance to line segment
                        ax, ay = px[i], py[i]
                        bx, by = px[j], py[j]

                        # Vector from a to b
                        vx, vy = bx - ax, by - ay
                        # Vector from a to point
                        wx, wy = px_orig - ax, py_orig - ay

                        # Project point onto line segment
                        t_seg = max(T(0), min(T(1), (wx*vx + wy*vy) / (vx*vx + vy*vy + T(1e-10))))
                        closest_x = ax + t_seg * vx
                        closest_y = ay + t_seg * vy

                        dist = sqrt((px_orig - closest_x)^2 + (py_orig - closest_y)^2)
                        min_dist = min(min_dist, dist)
                    end

                    # Use ray casting for inside/outside test for this polygon
                    inside = false
                    j = n
                    @inbounds for i in 1:n
                        if ((py[i] > py_orig) != (py[j] > py_orig)) &&
                           (px_orig < (px[j] - px[i]) * (py_orig - py[i]) / (py[j] - py[i]) + px[i])
                            inside = !inside
                        end
                        j = i
                    end

                    # SDF for this polygon
                    poly_sdf = inside ? -min_dist : min_dist

                    # Union operation: minimum distance (closest object)
                    min_dist_to_any = min(min_dist_to_any, poly_sdf)
                end

                return min_dist_to_any
            end

            AutoBody(multi_poly_sdf)

        # Fallback to old single polyline schema
        elseif haskey(curve_data, "points") && !isempty(curve_data["points"])
            println("Loading custom JSON body with $(length(curve_data["points"])) points (legacy format)")

            # Get curve points and normalize to simulation domain
            points = curve_data["points"]
            px = [T(p["x"]) for p in points]
            py = [T(p["y"]) for p in points]

            # Find bounds
            x_min, x_max = extrema(px)
            y_min, y_max = extrema(py)

            # Scale to simulation domain and center - less aggressive scaling
            domain_width = 8L
            domain_height = 4L

            # Use more reasonable scaling that preserves object size better
            scale_x = (domain_width * 0.3) / (x_max - x_min)  # Use 30% of domain width
            scale_y = (domain_height * 0.3) / (y_max - y_min)  # Use 30% of domain height
            scale = min(scale_x, scale_y)  # Use smaller scale to fit both dimensions

            cx = domain_width / 2   # center x
            cy = domain_height / 2  # center y

            println("🔧 Legacy scaling: object bounds ($x_min,$y_min) to ($x_max,$y_max)")
            println("🔧 Domain: $(domain_width)×$(domain_height), scale factor: $scale")

            # Simple polygon SDF using point-in-polygon
            function poly_sdf(x, t)
                px_sim = x[1]
                py_sim = x[2]

                # Transform back to original coordinates
                px_orig = (px_sim - cx) / scale + (x_min + x_max) / 2
                py_orig = (py_sim - cy) / scale + (y_min + y_max) / 2

                # Simple distance to polygon boundary
                min_dist = Inf
                n = length(px)
                for i in 1:n
                    j = i == n ? 1 : i + 1
                    # Distance to line segment
                    ax, ay = px[i], py[i]
                    bx, by = px[j], py[j]

                    # Vector from a to b
                    vx, vy = bx - ax, by - ay
                    # Vector from a to point
                    wx, wy = px_orig - ax, py_orig - ay

                    # Project point onto line segment
                    t = max(0, min(1, (wx*vx + wy*vy) / (vx*vx + vy*vy + 1e-10)))
                    closest_x = ax + t * vx
                    closest_y = ay + t * vy

                    dist = sqrt((px_orig - closest_x)^2 + (py_orig - closest_y)^2)
                    min_dist = min(min_dist, dist)
                end

                # Use ray casting for inside/outside test
                inside = false
                j = n
                for i in 1:n
                    if ((py[i] > py_orig) != (py[j] > py_orig)) &&
                        (px_orig < (px[j] - px[i]) * (py_orig - py[i]) / (py[j] - py[i]) + px[i])
                        inside = !inside
                    end
                    j = i
                end

                return inside ? -min_dist : min_dist
            end

            AutoBody(poly_sdf)
        else
            throw("No curve data found in JSON")
        end
    catch e
        println("Warning: Failed to load custom body ($(e)), using circle fallback")
        R = L/8
        AutoBody((x,t) -> hypot(x[1]-4L, x[2]-2L) - R)
    end

    # make a simulation
    Simulation((8L,4L),(U,0),L;U,ν=U*L/Re,body,T,mem)
end
