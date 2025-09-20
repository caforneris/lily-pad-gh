using WaterLily, Plots, JSON

# Set the plots backend for better compatibility
gr() # Use GR backend for plotting

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

# Function to create exact polyline body from curve points
function create_polyline_body(curve_points, boundary_data)
    # Extract x,y coordinates from the curve points and convert to Float32
    x_coords = Float32[p["x"] for p in curve_points]
    y_coords = Float32[p["y"] for p in curve_points]

    # Ensure the polyline is closed by connecting the last point to the first
    if (x_coords[1], y_coords[1]) != (x_coords[end], y_coords[end])
        push!(x_coords, x_coords[1])
        push!(y_coords, y_coords[1])
    end

    println("Created exact closed polyline with $(length(x_coords)) points")
    println("Polyline bounds: X[$(minimum(x_coords)), $(maximum(x_coords))], Y[$(minimum(y_coords)), $(maximum(y_coords))]")

    # Create exact polyline signed distance function
    function exact_polyline_sdf(x, t)
        px, py = x[1], x[2]

        # Point-in-polygon test using winding number (more robust than ray casting)
        winding_number = 0
        n = length(x_coords) - 1  # Exclude duplicate closing point

        for i in 1:n
            x1, y1 = x_coords[i], y_coords[i]
            x2, y2 = x_coords[i % n + 1], y_coords[i % n + 1]

            # Check if edge crosses horizontal ray from point to the right
            if y1 <= py
                if y2 > py  # Upward crossing
                    # Compute cross product to determine left/right of edge
                    cross = (x2 - x1) * (py - y1) - (y2 - y1) * (px - x1)
                    if cross > 0  # Point is left of edge
                        winding_number += 1
                    end
                end
            else
                if y2 <= py  # Downward crossing
                    # Compute cross product to determine left/right of edge
                    cross = (x2 - x1) * (py - y1) - (y2 - y1) * (px - x1)
                    if cross < 0  # Point is right of edge
                        winding_number -= 1
                    end
                end
            end
        end

        inside = winding_number != 0

        # Calculate minimum distance to any edge
        min_dist_sq = Float32(1e6)  # Large initial value
        for i in 1:n
            x1, y1 = x_coords[i], y_coords[i]
            x2, y2 = x_coords[i % n + 1], y_coords[i % n + 1]

            # Vector from point to line segment start
            dx = px - x1
            dy = py - y1

            # Line segment vector
            lx = x2 - x1
            ly = y2 - y1

            # Length squared of line segment
            len_sq = lx * lx + ly * ly

            if len_sq > Float32(1e-8)  # Avoid division by zero
                # Parameter t for closest point on line segment (clamped to [0,1])
                t_param = max(Float32(0), min(Float32(1), (dx * lx + dy * ly) / len_sq))

                # Closest point on line segment
                closest_x = x1 + t_param * lx
                closest_y = y1 + t_param * ly

                # Distance squared to closest point
                dist_sq = (px - closest_x)^2 + (py - closest_y)^2
            else
                # Degenerate line segment (point)
                dist_sq = dx^2 + dy^2
            end

            min_dist_sq = min(min_dist_sq, dist_sq)
        end

        min_dist = sqrt(min_dist_sq)

        # Return signed distance: negative inside, positive outside
        return inside ? -min_dist : min_dist
    end

    return AutoBody(exact_polyline_sdf)
end

# Main simulation function adapted for boundary and custom curves
function circle_flow_with_boundary(boundary_json_path, curve_json_path=nothing;
                                   Re=250, U=1, n=192, m=128, duration=1, mem=Array)

    # Read geometry data
    boundary_data, curve_data = read_geometry_data(boundary_json_path, curve_json_path)

    # Extract boundary dimensions and corner points
    width = boundary_data["width"]
    height = boundary_data["height"]
    center_x = boundary_data["center"]["x"]
    center_y = boundary_data["center"]["y"]

    # Get actual boundary extents from corner points
    corner_points = boundary_data["corner_points"]
    x_coords = [p["x"] for p in corner_points]
    y_coords = [p["y"] for p in corner_points]

    x_min, x_max = Float32(minimum(x_coords)), Float32(maximum(x_coords))
    y_min, y_max = Float32(minimum(y_coords)), Float32(maximum(y_coords))

    println("Computational Domain: X[$(x_min), $(x_max)], Y[$(y_min), $(y_max)]")
    println("Domain size: $(width) x $(height)")

    # Use the larger dimension as the characteristic length
    char_length = Float32(max(width, height))

    # Create a coordinate transformation function
    # Maps simulation grid coordinates (0 to n, 0 to m) to real coordinates (x_min to x_max, y_min to y_max)
    function grid_to_real(grid_x, grid_y)
        real_x = x_min + (grid_x / n) * width
        real_y = y_min + (grid_y / m) * height
        return real_x, real_y
    end

    # Create transformed body that works in grid coordinates
    if curve_data !== nothing && haskey(curve_data, "points")
        println("Using custom curve with $(length(curve_data["points"])) points")
        # Create the polyline body in real coordinates
        real_body = create_polyline_body(curve_data["points"], boundary_data)

        # Wrap it to work with grid coordinates
        body = AutoBody((x, t) -> begin
            # Transform grid coordinates to real coordinates
            grid_x, grid_y = x[1], x[2]
            real_x = x_min + (grid_x / char_length) * Float32(width)
            real_y = y_min + (grid_y / char_length) * Float32(height)

            # Evaluate the real body function using sdf
            return WaterLily.sdf(real_body, [real_x, real_y], t)
        end)
    else
        # Default to circle if no curve provided
        println("Using default circle within boundary")
        radius = Float32(min(width, height) / 8)

        body = AutoBody((x, t) -> begin
            # Transform grid coordinates to real coordinates
            grid_x, grid_y = x[1], x[2]
            real_x = x_min + (grid_x / char_length) * Float32(width)
            real_y = y_min + (grid_y / char_length) * Float32(height)

            # Distance from real center
            dist = sqrt((real_x - Float32(center_x))^2 + (real_y - Float32(center_y))^2)
            return Float32(dist - radius)
        end)
    end

    # Calculate kinematic viscosity
    ν = U * char_length / Re

    println("Reynolds number: $Re")
    println("Velocity: $U")
    println("Grid resolution: $n x $m")
    println("Characteristic length: $char_length")
    println("Kinematic viscosity: $ν")
    println("Simulation duration: $duration")

    # Create simulation with proper coordinate mapping
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
    duration = get(kwargs, :duration, 1)
    println("Starting simulation visualization...")

    # Create the animated GIF (sim_gif! automatically saves it)
    gif_filename = "$(cID)_simulation.gif"
    println("Creating animation as: $gif_filename")
    sim_gif!(sim, duration=duration, clims=(-5,5), plotbody=true, name=gif_filename)

    # Create a static plot of the final state
    println("Creating final state plot...")

    # Extract vorticity field for visualization
    vorticity = @. sim.flow.u[:,:,1] - sim.flow.u[:,:,2]
    flood(vorticity, clims=(-5,5))

    # Add body outline if available
    try
        body_plot!(sim)
    catch
        println("Could not add body outline to plot")
    end

    title!("Final Flow State - $(cID)")

    # Save the final state plot
    png_filename = "$(cID)_final_state.png"
    savefig(png_filename)
    println("Saved final state plot as: $png_filename")

    # Display the current plot
    display(current())

    # Plot the logger results if log file exists
    log_file = "$(cID).log"
    if isfile(log_file)
        println("Plotting residual convergence...")
        plot_logger(log_file)
        residual_filename = "$(cID)_residuals.png"
        savefig(residual_filename)
        println("Saved residual plot as: $residual_filename")
        display(current())
    end

    println("Simulation complete! Check the generated files:")
    println("  - Animation: $gif_filename")
    println("  - Final state: $png_filename")
    if isfile(log_file)
        println("  - Residuals: $residual_filename")
    end

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