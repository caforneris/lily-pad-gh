using WaterLily, Plots, JSON, StaticArrays

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

    # Always ensure the polyline is properly closed by adding the first point at the end
    # This guarantees a closed polygon for the signed distance function
    if length(x_coords) > 0
        # Check if already closed (within a small tolerance)
        dx = abs(x_coords[1] - x_coords[end])
        dy = abs(y_coords[1] - y_coords[end])
        tolerance = 1e-6

        if dx > tolerance || dy > tolerance
            push!(x_coords, x_coords[1])
            push!(y_coords, y_coords[1])
            println("Closed polyline: added closing segment from ($(x_coords[end-1]), $(y_coords[end-1])) to ($(x_coords[end]), $(y_coords[end]))")
        else
            println("Polyline was already closed within tolerance")
        end
    end

    println("Created exact closed polyline with $(length(x_coords)) points")
    println("Polyline bounds: X[$(minimum(x_coords)), $(maximum(x_coords))], Y[$(minimum(y_coords)), $(maximum(y_coords))]")
    println("First point: ($(x_coords[1]), $(y_coords[1]))")
    println("Last point: ($(x_coords[end]), $(y_coords[end]))")
    println("Closed: $(x_coords[1] == x_coords[end] && y_coords[1] == y_coords[end])")

    # Show the closing segment explicitly
    if length(x_coords) >= 2
        second_last_idx = length(x_coords) - 1
        println("Closing segment: ($(x_coords[second_last_idx]), $(y_coords[second_last_idx])) → ($(x_coords[end]), $(y_coords[end]))")

        # Verify the closing distance
        closing_distance = sqrt((x_coords[end] - x_coords[second_last_idx])^2 + (y_coords[end] - y_coords[second_last_idx])^2)
        println("Closing segment length: $(closing_distance)")
    end

    # Create exact polyline signed distance function
    function exact_polyline_sdf(x, t)
        px, py = x[1], x[2]

        # Point-in-polygon test using ray casting (simpler and more reliable)
        inside = false
        crossings = 0

        for i in 1:length(x_coords)-1  # Go through all edges including the closing one
            x1, y1 = x_coords[i], y_coords[i]
            x2, y2 = x_coords[i + 1], y_coords[i + 1]

            # Ray casting algorithm: cast ray from point to the right
            # Check if edge crosses the ray
            if ((y1 > py) != (y2 > py))  # Edge crosses horizontal line through point
                # Calculate x-intersection of edge with ray
                x_intersect = x1 + (py - y1) * (x2 - x1) / (y2 - y1)
                if px < x_intersect  # Point is to the left of intersection
                    inside = !inside
                    crossings += 1
                end
            end
        end

        # Debug for specific test points (disabled)
        # if abs(px - 123.4) < 1.0 && abs(py - 144.0) < 1.0  # Near center test point
        #     println("Debug: Point ($(px), $(py)) had $(crossings) crossings, inside = $(inside)")
        # end

        # Calculate minimum distance to any edge (including the closing edge)
        min_dist_sq = Float32(1e6)  # Large initial value
        for i in 1:length(x_coords)-1  # Go through all edges including the closing one
            x1, y1 = x_coords[i], y_coords[i]
            x2, y2 = x_coords[i + 1], y_coords[i + 1]

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
        sdf_value = inside ? -min_dist : min_dist

        # Debug: occasionally print SDF values to verify it's working (disabled)
        # if rand() < 0.0001  # Print very rarely to avoid spam
        #     println("SDF at ($(px), $(py)): inside=$inside, dist=$min_dist, sdf=$sdf_value")
        # end

        return sdf_value
    end

    # Test the signed distance function at a few points
    center_x_test = (minimum(x_coords) + maximum(x_coords)) / 2
    center_y_test = (minimum(y_coords) + maximum(y_coords)) / 2

    println("Testing SDF function:")
    println("  Polyline X range: [$(minimum(x_coords)), $(maximum(x_coords))]")
    println("  Polyline Y range: [$(minimum(y_coords)), $(maximum(y_coords))]")
    println("  Center point ($(center_x_test), $(center_y_test)): $(exact_polyline_sdf([center_x_test, center_y_test], 0.0f0))")
    println("  Outside point ($(minimum(x_coords)-10), $(center_y_test)): $(exact_polyline_sdf([minimum(x_coords)-10, center_y_test], 0.0f0))")

    # Test a point we know should be inside
    inside_test_x = (x_coords[1] + x_coords[10]) / 2  # Average of first and 10th point
    inside_test_y = (y_coords[1] + y_coords[10]) / 2
    println("  Likely inside point ($(inside_test_x), $(inside_test_y)): $(exact_polyline_sdf([inside_test_x, inside_test_y], 0.0f0))")

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

    # Create body directly in real coordinates (no transformation needed)
    if curve_data !== nothing && haskey(curve_data, "points")
        println("Using custom curve with $(length(curve_data["points"])) points")
        body = create_polyline_body(curve_data["points"], boundary_data)
    else
        # Default to circle if no curve provided
        println("Using default circle within boundary")
        radius = Float32(min(width, height) / 8)
        body = AutoBody((x, t) -> begin
            # Distance from real center
            dist = sqrt((x[1] - Float32(center_x))^2 + (x[2] - Float32(center_y))^2)
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

    # Create simulation domain that exactly matches the boundary rectangle
    # WaterLily expects domain to start from origin, so we translate everything
    domain_width = Float32(width)
    domain_height = Float32(height)

    # Translate the polyline body to start from origin
    translated_body = AutoBody((x, t) -> begin
        # WaterLily coordinates are scaled, so convert back to real coordinates
        real_x = x[1] * (domain_width / char_length) + x_min
        real_y = x[2] * (domain_height / char_length) + y_min

        # Evaluate body function at real coordinates
        return WaterLily.sdf(body, [real_x, real_y], t)
    end)

    # Create simulation with domain dimensions matching boundary rectangle
    sim = Simulation((n, m), (U, 0), char_length; ν=ν, body=translated_body, mem=mem)

    # Return simulation with domain info
    return sim, (x_min, y_min, domain_width, domain_height, char_length)
end

# Example usage with JSON files
function run_simulation_from_json(boundary_json_path, curve_json_path=nothing; kwargs...)
    # Initialize the simulation
    sim, domain_info = circle_flow_with_boundary(boundary_json_path, curve_json_path; kwargs...)
    x_min, y_min, domain_width, domain_height, char_length = domain_info

    # Log the residual
    WaterLily.logger(cID)

    # For GPU simulations, reduce debug logging
    # using Logging; disable_logging(Logging.Debug)

    # Run the simulation with visualization
    duration = get(kwargs, :duration, 1)
    println("Starting simulation visualization...")

    # Create the animated GIF
    gif_filename = "$(cID)_simulation.gif"
    println("Creating animation as: $gif_filename")

    # Try to create the animation
    try
        # Method 1: Use sim_gif! with name parameter
        sim_gif!(sim, duration=duration, clims=(-5,5), plotbody=true, name=gif_filename)
        println("Animation saved successfully using sim_gif!")
    catch e1
        println("Method 1 failed: $e1")
        try
            # Method 2: Use sim_gif! without name and save manually
            println("Trying alternative method...")
            anim = sim_gif!(sim, duration=duration, clims=(-5,5), plotbody=true)
            gif(anim, gif_filename, fps=15)
            println("Animation saved successfully using manual save")
        catch e2
            println("Method 2 failed: $e2")
            try
                # Method 3: Create animation manually
                println("Trying manual animation creation...")
                anim = Animation()
                for i in 1:Int(duration*10)  # 10 frames per second
                    mom_step!(sim.flow, sim.pois)
                    flood(sim, clims=(-5,5), plotbody=true)
                    frame(anim)
                end
                gif(anim, gif_filename, fps=10)
                println("Animation created manually and saved")
            catch e3
                println("All methods failed: $e3")
                println("Skipping animation creation")
            end
        end
    end

    # Create a static plot of the final state
    println("Creating final state plot...")

    # Calculate vorticity field properly
    u = sim.flow.u[:,:,1]  # x-velocity
    v = sim.flow.u[:,:,2]  # y-velocity

    # Calculate vorticity = ∂v/∂x - ∂u/∂y
    nx, ny = size(u)
    vorticity = zeros(Float32, nx, ny)

    # Finite difference calculation of vorticity
    dx = domain_width / nx
    dy = domain_height / ny

    for i in 2:nx-1
        for j in 2:ny-1
            dvdx = (v[i+1,j] - v[i-1,j]) / (2*dx)
            dudy = (u[i,j+1] - u[i,j-1]) / (2*dy)
            vorticity[i,j] = dvdx - dudy
        end
    end

    # Create coordinate arrays for the real domain (boundary rectangle coordinates)
    x_coords = range(x_min, x_min + domain_width, length=nx)
    y_coords = range(y_min, y_min + domain_height, length=ny)

    # Plot with real boundary coordinates
    heatmap(x_coords, y_coords, vorticity',
           clims=(-5,5),
           aspect_ratio=:equal,
           xlims=(x_min, x_min + domain_width),
           ylims=(y_min, y_min + domain_height))

    # Add body outline if available
    try
        body_plot!(sim)
    catch
        println("Could not add body outline to plot")
    end

    title!("Final Flow State - $(cID)")
    xlabel!("X Coordinate")
    ylabel!("Y Coordinate")

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