using WaterLily, Plots, JSON, StaticArrays, ParametricBodies

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

# Function to create smooth NURBS body using ParametricBodies.jl interpNurbs
function create_polyline_body(curve_points, boundary_data)
    # Convert points to Float32 arrays
    points_x = Float32[p["x"] for p in curve_points]
    points_y = Float32[p["y"] for p in curve_points]

    # Ensure we have a proper closed polygon - remove any duplicate closing point first
    if length(points_x) > 0 && abs(points_x[1] - points_x[end]) < 1e-6 && abs(points_y[1] - points_y[end]) < 1e-6
        pop!(points_x)
        pop!(points_y)
        println("Removed duplicate closing point")
    end

    n_points = length(points_x)
    println("Creating interpNurbs ParametricBody with $n_points unique points")
    println("Polyline bounds: X[$(minimum(points_x)), $(maximum(points_x))], Y[$(minimum(points_y)), $(maximum(points_y))]")
    println("First point: $(points_x[1]), $(points_y[1])")
    println("Last point: $(points_x[end]), $(points_y[end])")

    # Force closure by adding first point at end for interpolation
    closed_x = vcat(points_x, points_x[1])
    closed_y = vcat(points_y, points_y[1])
    n_closed = length(closed_x)

    # Create points matrix for interpNurbs (2 x n format)
    points_matrix = SMatrix{2, n_closed, Float32}(vcat(closed_x', closed_y'))

    # Choose degree - start with linear (degree=1) for robustness
    degree = min(1, n_closed - 1)  # Use degree 1 (linear) as you suggested
    println("Using NURBS degree: $degree for $n_closed points (including closure)")

    # Create interpolating NURBS curve using interpNurbs
    try
        nurbs_curve = ParametricBodies.interpNurbs(points_matrix; p=degree)
        println("Successfully created interpNurbs curve")

        # Define curve function using interpNurbs evaluation
        function curve(u, t)
            # Ensure u is in [0, 1]
            u_wrapped = mod(u, 1.0)

            try
                point = nurbs_curve(u_wrapped, t)
                return SVector(Float32(point[1]), Float32(point[2]))
            catch e
                println("NURBS evaluation failed at u=$u_wrapped: $e")
                # Fallback to linear interpolation
                if u_wrapped == 0.0 || u_wrapped == 1.0
                    return SVector(closed_x[1], closed_y[1])
                else
                    idx = min(Int(floor(u_wrapped * (n_closed-1))) + 1, n_closed-1)
                    t_local = (u_wrapped * (n_closed-1)) - (idx-1)
                    x = closed_x[idx] + t_local * (closed_x[idx+1] - closed_x[idx])
                    y = closed_y[idx] + t_local * (closed_y[idx+1] - closed_y[idx])
                    return SVector(x, y)
                end
            end
        end

        # Define locate function using NURBS
        function locate(x, t)
            px, py = x[1], x[2]
            min_dist = Float32(Inf)
            best_param = Float32(0)

            # Sample the NURBS curve to find closest point
            n_samples = 200
            for i in 0:n_samples
                u = Float32(i) / n_samples
                try
                    curve_point = curve(u, t)
                    dist = sqrt((px - curve_point[1])^2 + (py - curve_point[2])^2)
                    if dist < min_dist
                        min_dist = dist
                        best_param = u
                    end
                catch
                    continue
                end
            end

            return best_param
        end

        # Create ParametricBody with NURBS curve
        body = ParametricBody(curve, locate)

        # Test the body function
        centroid_x = sum(points_x) / n_points
        centroid_y = sum(points_y) / n_points
        test_point = SVector(centroid_x, centroid_y)

        println("Testing interpNurbs ParametricBody at centroid ($centroid_x, $centroid_y): $(WaterLily.sdf(body, test_point, 0.0))")

        # Test closure by checking curve at parameters 0 and 1
        p0 = curve(0.0, 0.0)
        p1 = curve(1.0, 0.0)
        closure_error = sqrt((p0[1] - p1[1])^2 + (p0[2] - p1[2])^2)
        println("interpNurbs Curve at u=0.0: $p0")
        println("interpNurbs Curve at u=1.0: $p1")
        println("interpNurbs closure error: $closure_error")

        return body, curve

    catch e
        println("Failed to create interpNurbs curve: $e")
        println("Falling back to linear interpolation")

        # Fallback to simple linear interpolation
        function curve_fallback(u, t)
            u_wrapped = mod(u, 1.0)
            if u_wrapped == 1.0
                return SVector(closed_x[1], closed_y[1])  # Perfect closure
            end

            idx = min(Int(floor(u_wrapped * (n_closed-1))) + 1, n_closed-1)
            t_local = (u_wrapped * (n_closed-1)) - (idx-1)
            x = closed_x[idx] + t_local * (closed_x[idx+1] - closed_x[idx])
            y = closed_y[idx] + t_local * (closed_y[idx+1] - closed_y[idx])
            return SVector(x, y)
        end

        function locate_fallback(x, t)
            px, py = x[1], x[2]
            min_dist = Float32(Inf)
            best_param = Float32(0)

            for i in 0:200
                u = Float32(i) / 200
                curve_point = curve_fallback(u, t)
                dist = sqrt((px - curve_point[1])^2 + (py - curve_point[2])^2)
                if dist < min_dist
                    min_dist = dist
                    best_param = u
                end
            end
            return best_param
        end

        body = ParametricBody(curve_fallback, locate_fallback)

        # Test closure
        p0 = curve_fallback(0.0, 0.0)
        p1 = curve_fallback(1.0, 0.0)
        closure_error = sqrt((p0[1] - p1[1])^2 + (p0[2] - p1[2])^2)
        println("Fallback curve closure error: $closure_error")

        return body, curve_fallback
    end
end

# Function to plot parametric curve
function plot_parametric_curve!(curve_func, t=0.0; n_points=200, color=:red, linewidth=2)
    # Generate points along the parametric curve
    u_values = range(0, 1, length=n_points)
    curve_points = [curve_func(u, t) for u in u_values]

    # Extract x and y coordinates
    x_coords = [p[1] for p in curve_points]
    y_coords = [p[2] for p in curve_points]

    # Plot the curve
    plot!(x_coords, y_coords, color=color, linewidth=linewidth, label="Body outline")
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
        body, curve_func = create_polyline_body(curve_data["points"], boundary_data)
    else
        # Default to circle if no curve provided
        println("Using default circle within boundary")
        radius = Float32(min(width, height) / 8)
        body = AutoBody((x, t) -> begin
            # Distance from real center
            dist = sqrt((x[1] - Float32(center_x))^2 + (x[2] - Float32(center_y))^2)
            return Float32(dist - radius)
        end)
        # Default circle curve function for plotting
        curve_func = (u, t) -> begin
            angle = u * 2π
            x = Float32(center_x) + radius * cos(angle)
            y = Float32(center_y) + radius * sin(angle)
            return SVector(x, y)
        end
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

    # Return simulation with domain info and curve function for plotting
    return sim, (x_min, y_min, domain_width, domain_height, char_length), curve_func
end

# Example usage with JSON files
function run_simulation_from_json(boundary_json_path, curve_json_path=nothing; kwargs...)
    # Initialize the simulation
    sim, domain_info, curve_func = circle_flow_with_boundary(boundary_json_path, curve_json_path; kwargs...)
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

    # Improved animation creation method
    try
        # Create animation using WaterLily's sim_gif! function
        println("Creating animation with sim_gif!...")
        gif_result = sim_gif!(sim, duration=duration, clims=(-5,5), plotbody=true)

        # Check if sim_gif! returned an animation object
        if isa(gif_result, Plots.Animation)
            # Save the animation manually
            gif(gif_result, gif_filename, fps=15)
            println("Animation created and saved successfully!")
        else
            # sim_gif! may have saved directly, check if file exists
            if isfile(gif_filename)
                println("Animation saved directly by sim_gif!")
            else
                println("Animation creation failed - no output file found")
            end
        end
    catch e
        println("Animation creation failed with error: $e")
        println("Continuing without animation...")
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

    # Add body outline using parametric curve
    try
        plot_parametric_curve!(curve_func, 0.0; color=:black, linewidth=3)
        println("Added parametric body outline to plot")
    catch e
        println("Could not add body outline to plot: $e")
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
        try
            plot_logger(log_file)
            residual_filename = "$(cID)_residuals.png"
            savefig(residual_filename)
            println("Saved residual plot as: $residual_filename")
            display(current())
        catch e
            println("Could not plot residuals: $e")
        end
    end

    println("Simulation complete! Check the generated files:")
    println("  - Animation: $gif_filename")
    println("  - Final state: $png_filename")
    if isfile(log_file)
        println("  - Residuals: $(cID)_residuals.png")
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