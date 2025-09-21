using WaterLily,StaticArrays,Plots
using JSON, ParametricBodies

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
        # Using formula: |ax + by + c| / sqrt(a² + b²)
        # Line: (y2-y1)x - (x2-x1)y + x2*y1 - y2*x1 = 0
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

struct PrecomputedPolyline{T}
    px::Vector{T}
    py::Vector{T}
    n::Int
end

struct OptimizedPolyData{T}
    polylines::Vector{PrecomputedPolyline{T}}
    scale::T
    cx::T
    cy::T
    x_center::T
    y_center::T
end

function precompute_polylines(polylines, L, T; tolerance=nothing, max_points=nothing)
    # Pre-process all polylines once
    processed_polylines = PrecomputedPolyline{T}[]
    all_px = T[]
    all_py = T[]

    # Auto-calculate tolerance if not provided (0.5% of expected size)
    if tolerance === nothing
        tolerance = T(0.005)  # Default tolerance for simplification
    end

    total_original_points = 0
    total_simplified_points = 0

    for polyline in polylines
        if haskey(polyline, "points") && !isempty(polyline["points"])
            px = [T(p["x"]) for p in polyline["points"]]
            py = [T(p["y"]) for p in polyline["points"]]

            original_count = length(px)
            total_original_points += original_count

            # Apply Douglas-Peucker simplification
            keep_indices = douglas_peucker(px, py, tolerance)
            simplified_px = px[keep_indices]
            simplified_py = py[keep_indices]

            # Further reduce if max_points specified
            if max_points !== nothing && length(simplified_px) > max_points
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

            simplified_count = length(simplified_px)
            total_simplified_points += simplified_count

            push!(processed_polylines, PrecomputedPolyline(simplified_px, simplified_py, simplified_count))
            append!(all_px, simplified_px)
            append!(all_py, simplified_py)
        end
    end

    println("Polyline simplification: $total_original_points points → $total_simplified_points points ($(round(100*total_simplified_points/total_original_points, digits=1))% retained)")

    if isempty(all_px)
        throw("No points found in any polyline")
    end

    x_min, x_max = extrema(all_px)
    y_min, y_max = extrema(all_py)

    scale = T(min(6L/(x_max-x_min), 3L/(y_max-y_min)))
    cx = T(4L)
    cy = T(2L)
    x_center = T((x_min + x_max) / 2)
    y_center = T((y_min + y_max) / 2)

    return OptimizedPolyData(processed_polylines, scale, cx, cy, x_center, y_center)
end

function make_sim(;L=2^5,U=1,Re=500,mem=Array,T=Float32)

    # Try to load multiple polylines from JSON, fallback to circle
    body = try
        script_dir = @__DIR__
        curve_path = joinpath(script_dir, "JSON", "curve_json.json")

        if isfile(curve_path)
            curve_data = JSON.parsefile(curve_path)

            # Check for new multiple polylines schema
            if haskey(curve_data, "polylines") && !isempty(curve_data["polylines"])
                polylines = curve_data["polylines"]
                println("Loading custom JSON body with $(length(polylines)) polylines")

                # Pre-compute all polyline data once with simplification
                poly_data = precompute_polylines(polylines, L, T,
                                                tolerance=T(SIMPLIFY_TOLERANCE),
                                                max_points=MAX_POINTS_PER_POLY)

                # Create optimized SDF function with performance enhancements
                @fastmath @inline function multi_poly_sdf(x, t)
                    px_sim = x[1]
                    py_sim = x[2]

                    # Transform to original coordinates (optimized)
                    px_orig = (px_sim - poly_data.cx) / poly_data.scale + poly_data.x_center
                    py_orig = (py_sim - poly_data.cy) / poly_data.scale + poly_data.y_center

                    min_dist_to_any = T(Inf)

                    # Process each pre-computed polyline
                    @inbounds for poly in poly_data.polylines
                        # Distance to this polygon boundary
                        min_dist = T(Inf)
                        @inbounds for i in 1:poly.n
                            j = i == poly.n ? 1 : i + 1
                            # Distance to line segment
                            ax, ay = poly.px[i], poly.py[i]
                            bx, by = poly.px[j], poly.py[j]

                            # Vector calculations (optimized)
                            vx, vy = bx - ax, by - ay
                            wx, wy = px_orig - ax, py_orig - ay

                            # Project point onto line segment (avoid division by storing reciprocal)
                            dot_wv = wx*vx + wy*vy
                            dot_vv = vx*vx + vy*vy + T(1e-10)
                            t_seg = max(T(0), min(T(1), dot_wv / dot_vv))

                            # Closest point calculation
                            closest_x = ax + t_seg * vx
                            closest_y = ay + t_seg * vy

                            # Distance calculation (avoid sqrt when possible)
                            dx = px_orig - closest_x
                            dy = py_orig - closest_y
                            dist_sq = dx*dx + dy*dy
                            min_dist = min(min_dist, sqrt(dist_sq))
                        end

                        # Ray casting for inside/outside test (optimized)
                        inside = false
                        j = poly.n
                        @inbounds for i in 1:poly.n
                            py_i, py_j = poly.py[i], poly.py[j]
                            if ((py_i > py_orig) != (py_j > py_orig))
                                px_i, px_j = poly.px[i], poly.px[j]
                                if px_orig < (px_j - px_i) * (py_orig - py_i) / (py_j - py_i) + px_i
                                    inside = !inside
                                end
                            end
                            j = i
                        end

                        # SDF for this polygon
                        poly_sdf = inside ? -min_dist : min_dist
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

                # Scale to simulation domain (8L x 4L) and center
                scale = T(min(6L/(x_max-x_min), 3L/(y_max-y_min)))  # Leave some margin
                cx = T(4L)  # center x
                cy = T(2L)  # center y

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
        else
            throw("JSON file not found")
        end
    catch e
        println("Warning: Failed to load custom body ($(e)), using circle fallback")
        R = T(L/8)
        AutoBody((x,t) -> hypot(x[1]-T(4L), x[2]-T(2L)) - R)
    end

    # make a simulation
    Simulation((8L,4L),(U,0),L;U,ν=U*L/Re,body,T,mem)
end

# ========== SIMULATION PARAMETERS ==========
# Tunable parameters - adjust these for performance/quality tradeoff
const SIM_L = 2^3          # Grid resolution (2^4=16, 2^5=32, 2^6=64, 2^7=128)
const SIM_U = 1            # Flow velocity
const SIM_Re = 50        # Reynolds number (lower = faster, higher = more realistic)
const SIM_T = Float32      # Precision (Float32 = faster, Float64 = more accurate)

# Polyline simplification parameters (MAJOR SPEED IMPACT)
const SIMPLIFY_TOLERANCE = 0.05   # Douglas-Peucker tolerance (higher = fewer points, faster)
const MAX_POINTS_PER_POLY = 10    # Maximum points per polyline (lower = faster)

# Animation parameters
const ANIM_DURATION = 10   # Simulation time units
const ANIM_CLIMS = (-10,10) # Color limits for visualization
const PLOT_BODY = true     # Show body outline in animation

# using CUDA
# intialize and run
sim = make_sim(L=SIM_L, U=SIM_U, Re=SIM_Re, T=SIM_T)#;mem=CuArray)
gif_path = sim_gif!(sim, duration=ANIM_DURATION, clims=ANIM_CLIMS, plotbody=PLOT_BODY)

# Open the generated GIF automatically
try
    # Extract the actual file path from the AnimatedGif object
    actual_path = gif_path.filename
    if Sys.iswindows()
        # Use cmd /c to open the file with default application
        run(`cmd /c start "" "$actual_path"`)
    elseif Sys.isapple()
        run(`open $actual_path`)
    else
        run(`xdg-open $actual_path`)
    end
    println("Opened GIF viewer for: $actual_path")
catch e
    println("Could not automatically open GIF viewer: $e")
    println("GIF object: $gif_path")
end