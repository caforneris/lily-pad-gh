using WaterLily, Plots, JSON

# Component ID matching the C# component
cID = "Circle2DFlowComponent"

# Function to read boundary and curve data from JSON files
function read_geometry_data(boundary_json_path, curve_json_path=nothing)
    # Read boundary data
    boundary_data = JSON.parsefile(boundary_json_path)

    # Read curve data if provided
    curve_data = nothing
    if curve_json_path !== nothing && isfile(curve_json_path)
        curve_data = JSON.parsefile(curve_json_path)
    end

    return boundary_data, curve_data
end

# Function to create polyline from curve points
function create_polyline_body(curve_points)
    # Extract x,y coordinates from the curve points
    x_coords = [p["x"] for p in curve_points]
    y_coords = [p["y"] for p in curve_points]

    # Create a closed polyline by connecting the last point to the first
    if (x_coords[1], y_coords[1]) != (x_coords[end], y_coords[end])
        push!(x_coords, x_coords[1])
        push!(y_coords, y_coords[1])
    end

    # Create the polyline as a body
    # This function checks if a point is inside the polyline
    function inside_polyline(x, t)
        px, py = x
        n = length(x_coords)
        inside = false

        p1x, p1y = x_coords[1], y_coords[1]
        for i in 1:n-1
            p2x, p2y = x_coords[i+1], y_coords[i+1]
            if py > min(p1y, p2y)
                if py <= max(p1y, p2y)
                    if px <= max(p1x, p2x)
                        if p1y != p2y
                            xinters = (py - p1y) * (p2x - p1x) / (p2y - p1y) + p1x
                        end
                        if p1x == p2x || px <= xinters
                            inside = !inside
                        end
                    end
                end
            end
            p1x, p1y = p2x, p2y
        end

        return inside ? -1.0 : 1.0  # Negative inside, positive outside
    end

    return AutoBody(inside_polyline)
end

# Main simulation function adapted for boundary and custom curves
function circle_flow_with_boundary(boundary_json_path, curve_json_path=nothing;
                                   Re=250, U=1, n=192, m=128, duration=10, mem=Array)

    # Read geometry data
    boundary_data, curve_data = read_geometry_data(boundary_json_path, curve_json_path)

    # Extract boundary dimensions
    width = boundary_data["width"]
    height = boundary_data["height"]
    center_x = boundary_data["center"]["x"]
    center_y = boundary_data["center"]["y"]

    println("Boundary: $(width) x $(height) centered at ($(center_x), $(center_y))")

    # Create body from curve data or use default circle
    if curve_data !== nothing && haskey(curve_data, "points")
        println("Using custom curve with $(length(curve_data["points"])) points")
        body = create_polyline_body(curve_data["points"])
        # Estimate characteristic length from curve bounds
        x_coords = [p["x"] for p in curve_data["points"]]
        y_coords = [p["y"] for p in curve_data["points"]]
        char_length = max(maximum(x_coords) - minimum(x_coords),
                          maximum(y_coords) - minimum(y_coords)) / 2
    else
        # Default to circle if no curve provided
        println("Using default circle")
        radius = min(width, height) / 8
        char_length = radius
        body = AutoBody((x,t)->√sum(abs2, x .- (center_x, center_y)) - radius)
    end

    # Calculate kinematic viscosity
    ν = U * char_length / Re

    println("Reynolds number: $Re")
    println("Velocity: $U")
    println("Grid resolution: $n x $m")
    println("Kinematic viscosity: $ν")
    println("Simulation duration: $duration")

    # Create simulation with specified boundary and body
    sim = Simulation((n, m), (U, 0), char_length; ν=ν, body=body, mem=mem)

    return sim
end

# Example usage with JSON files
function run_simulation_from_json(boundary_json_path, curve_json_path=nothing; kwargs...)
    # Initialize the simulation
    sim = circle_flow_with_boundary(boundary_json_path, curve_json_path; kwargs...)

    # Log the residual
    WaterLily.logger(cID)

    # For GPU simulations, reduce debug logging
    # using Logging; disable_logging(Logging.Debug)

    # Run the simulation with visualization
    duration = get(kwargs, :duration, 10)
    sim_gif!(sim, duration=duration, clims=(-5,5), plotbody=true)

    # Plot the logger results
    plot_logger("$(cID).log")

    return sim
end

# Main execution
if abspath(PROGRAM_FILE) == @__FILE__
    # File paths pointing to the JSON folder
    json_dir = joinpath(dirname(@__DIR__), "JSON")
    boundary_json = joinpath(json_dir, "boundary_json.json")
    curve_json = joinpath(json_dir, "curve_json.json")

    # Check if files exist
    if !isfile(boundary_json)
        println("Error: boundary_json.json not found at $boundary_json")
        println("Please export from Grasshopper component to JSON folder.")
        exit(1)
    end

    # Run simulation
    if isfile(curve_json)
        println("Running simulation with custom curve from JSON folder...")
        println("Boundary file: $boundary_json")
        println("Curve file: $curve_json")
        run_simulation_from_json(boundary_json, curve_json)
    else
        println("Running simulation with boundary only (no curve file found)...")
        println("Boundary file: $boundary_json")
        run_simulation_from_json(boundary_json, nothing)
    end
end