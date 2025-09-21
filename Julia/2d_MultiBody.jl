using WaterLily,StaticArrays,Plots
using JSON, ParametricBodies

function make_sim(;L=2^5,U=1,Re=250,mem=Array)

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

                # Find overall bounds across all polylines
                all_px = Float64[]
                all_py = Float64[]
                for polyline in polylines
                    if haskey(polyline, "points") && !isempty(polyline["points"])
                        px = [Float64(p["x"]) for p in polyline["points"]]
                        py = [Float64(p["y"]) for p in polyline["points"]]
                        append!(all_px, px)
                        append!(all_py, py)
                    end
                end

                if isempty(all_px)
                    throw("No points found in any polyline")
                end

                x_min, x_max = extrema(all_px)
                y_min, y_max = extrema(all_py)

                # Scale to simulation domain (8L x 4L) and center
                scale = min(6L/(x_max-x_min), 3L/(y_max-y_min))  # Leave some margin
                cx = 4L  # center x
                cy = 2L  # center y

                # Create SDF function for multiple polylines (union)
                function multi_poly_sdf(x, t)
                    px_sim = x[1]
                    py_sim = x[2]

                    # Transform back to original coordinates
                    px_orig = (px_sim - cx) / scale + (x_min + x_max) / 2
                    py_orig = (py_sim - cy) / scale + (y_min + y_max) / 2

                    min_dist_to_any = Inf

                    # Process each polyline
                    for polyline in polylines
                        if haskey(polyline, "points") && !isempty(polyline["points"])
                            px = [Float64(p["x"]) for p in polyline["points"]]
                            py = [Float64(p["y"]) for p in polyline["points"]]

                            # Distance to this polygon boundary
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
                                t_seg = max(0, min(1, (wx*vx + wy*vy) / (vx*vx + vy*vy + 1e-10)))
                                closest_x = ax + t_seg * vx
                                closest_y = ay + t_seg * vy

                                dist = sqrt((px_orig - closest_x)^2 + (py_orig - closest_y)^2)
                                min_dist = min(min_dist, dist)
                            end

                            # Use ray casting for inside/outside test for this polygon
                            inside = false
                            j = n
                            for i in 1:n
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
                    end

                    return min_dist_to_any
                end

                AutoBody(multi_poly_sdf)

            # Fallback to old single polyline schema
            elseif haskey(curve_data, "points") && !isempty(curve_data["points"])
                println("Loading custom JSON body with $(length(curve_data["points"])) points (legacy format)")

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
                throw("No curve data found in JSON")
            end
        else
            throw("JSON file not found")
        end
    catch e
        println("Warning: Failed to load custom body ($(e)), using circle fallback")
        R = L/8
        AutoBody((x,t) -> hypot(x[1]-4L, x[2]-2L) - R)
    end

    # make a simulation
    Simulation((8L,4L),(U,0),L;U,ν=U*L/Re,body,T=Float64,mem)
end
# using CUDA
# intialize and run
sim = make_sim()#;mem=CuArray)
sim_gif!(sim,duration=1,clims=(-10,10),plotbody=true)