using WaterLily,StaticArrays,Plots
using JSON3, ParametricBodies

function run_sim(raw_string::AbstractString; L=2^5,U=1,Re=250,mem=Array)
    # using CUDA
    # intialize and run
    sim = make_sim(raw_string; L=L, U=U, Re=Re, mem=mem)#;mem=CuArray)
    sim_gif!(sim,duration=1,clims=(-10,10),plotbody=true)
end

function make_sim(raw_string::AbstractString; L=2^5,U=1,Re=250,mem=Array)
   
    println("running")
    # Try to load custom JSON body, fallback to circle
    body = try
        script_dir = @__DIR__
        curve_data = JSON3.read(raw_string)
        # curve_path = joinpath(script_dir, "JSON", "curve_json.json")

        # curve_data = JSON.parsefile(curve_path)

        @show typeof(curve_data)
        @show propertynames(curve_data)



        if haskey(curve_data, "points") && !isempty(curve_data["points"])
            println("Loading custom JSON body with $(length(curve_data["points"])) points")

            # Get curve points and normalize to simulation domain
            points = curve_data["points"]
            px = [Float64(p["x"]) for p in points]
            py = [Float64(p["y"]) for p in points]

            # Find bounds
            x_min, x_max = extrema(px)
            y_min, y_max = extrema(py)

            # Scale to simulation domain (8L x 4L) and center
            scale = min(6L/(x_max-x_min), 3L/(y_max-y_min))  # Leave some margin
            cx = 4L  # center x
            cy = 2L  # center y

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
            throw("No curve points found in JSON")
        end
    catch e
        println("Warning: Failed to load custom body ($(e)), using circle fallback")
        R = L/8
        AutoBody((x,t) -> hypot(x[1]-4L, x[2]-2L) - R)
    end

    # make a simulation
    Simulation((8L,4L),(U,0),L;U,ν=U*L/Re,body,T=Float64,mem)
end
