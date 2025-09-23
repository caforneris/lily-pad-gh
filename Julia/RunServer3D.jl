# ========================================
# FILE: RunServer3D.jl
# DESC: A persistent HTTP server that runs a headless WaterLily.jl simulation
#       and optionally generates an MP4 animation of the results.
# --- REVISIONS ---
# - 2025-09-21 @ 11:23: Fixed UndefVarError(:isosurface, Makie).
#   - The isosurface function comes from GeometryBasics, not Makie.
#     Removed incorrect "Makie." prefix from the function call.
# - 2025-09-21 @ 11:12: Switched to mesh! for plotting solid obstacles.
# ========================================

using HTTP
using JSON3
using WaterLily
using CairoMakie
using Base64
using Statistics
using Interpolations
using GeometryBasics

# --- Global Server State ---
const SERVER = Ref{HTTP.Server}()
const SERVER_RUNNING = Ref(true)

# --- Helper to convert boolean mask to a Signed Distance Function (SDF) ---
function soft_phi(mask::Array{Bool,3})
    nx, ny, nz = size(mask)
    itp  = interpolate(Float32.(mask), BSpline(Linear()), OnGrid())
    eitp = Interpolations.extrapolate(itp, Interpolations.Flat())
    sitp = Interpolations.scale(eitp, 1:nx, 1:ny, 1:nz)
    return (x, t) -> 0.5 - sitp(x[1], x[2], x[3])
end

# --- Simulation Logic ---
function run_simulation_from_json(json_data)
    try
        cfg = json_data
        
        nx, ny, nz = Tuple(cfg["obstacles"]["shape"])
        bytes = base64decode(String(cfg["obstacles"]["data"]))
        mask = Array{Bool}(undef, nx, ny, nz)
        idx = 1
        for k in 1:nz, j in 1:ny, i in 1:nx
            mask[i,j,k] = (bytes[idx] != 0x00)
            idx += 1
        end

        φ = soft_phi(mask)
        body = AutoBody(φ)

        Ux, Uy, Uz = Tuple(cfg["fluid"]["forcing"]["velocity"])
        Ubc = (Float32(Ux), Float32(Uy), Float32(Uz))
        ν = Float32(cfg["fluid"]["nu"])
        sim = Simulation((nx,ny,nz), Ubc, 0.0; ν=ν, body=body, T=Float32)
        
        if haskey(cfg, "animation") && cfg["animation"]["generate_video"]
            anim_path = cfg["animation"]["output_path"]
            duration = Float32(cfg["time"]["duration"])
            framerate = round(Int, 1 / Float64(cfg["time"]["dt"]))
            frame_duration = 1 / framerate
            
            println("🎬 Starting animation recording to: ", anim_path)
            
            # --- Set up the Makie Scene ---
            fig = Figure(size=(800, 800), backgroundcolor = :black)
            ax = LScene(fig[1,1], show_axis=false)
            
            solid_mask_numeric = Float32.(mask)
            # NOTE: Corrected. The isosurface function comes from GeometryBasics.
            vertices, faces = isosurface(solid_mask_numeric, 0.5)
            mesh!(ax, vertices, faces; color=(:white, 0.3), shading=false)

            vorticity_data = sim.flow.σ
            vorticity_obs = Observable(vorticity_data[inside(vorticity_data)])

            sim_step!(sim, 0.5)
            @inside vorticity_data[I] = WaterLily.ω_mag(I, sim.flow.u)
            max_vort = maximum(vorticity_data)
            vorticity_obs[] = vorticity_data[inside(vorticity_data)]
            
            volume!(ax, vorticity_obs, algorithm=:mip, colormap=:viridis, colorrange=(0, max_vort * 0.75))
            
            cam_pos = Vec3f(nx*1.5, ny*1.5, nz*1.5)
            look_at = Vec3f(nx/2, ny/2, nz/2)
            update_cam!(ax.scene, cam_pos, look_at)

            # --- Record the Animation ---
            record(fig, anim_path, 0:frame_duration:duration; framerate=framerate) do t
                sim_step!(sim, frame_duration)
                @inside vorticity_data[I] = WaterLily.ω_mag(I, sim.flow.u)
                vorticity_obs[] = vorticity_data[inside(vorticity_data)]
            end

            println("✅ Animation saved.")
            return "Simulation complete. Animation saved to: $anim_path"
        else
            println("💨 Running headless simulation...")
            sim_step!(sim, Float32(cfg["time"]["duration"]))
            println("✅ Headless simulation complete.")
            return "Headless simulation complete."
        end
    catch e
        @error "Simulation failed: $e"
        rethrow(e)
    end
end

# --- HTTP Handlers and Server Startup (No Changes) ---
function handle_request(req::HTTP.Request)
    if req.method == "POST" && req.target == "/"
        try
            json_data = JSON3.read(IOBuffer(HTTP.body(req)))
            result = run_simulation_from_json(json_data)
            return HTTP.Response(200, result)
        catch e
            @error "Failed to process request: $e"
            return HTTP.Response(500, "Internal Server Error: $e")
        end
    elseif req.method == "GET" && req.target == "/shutdown"
        @info "Shutdown request received. Closing server."
        SERVER_RUNNING[] = false
        return HTTP.Response(200, "Server is shutting down.")
    end
    return HTTP.Response(404, "Not Found")
end

function main()
    host = "127.0.0.1"
    port = 8080
    
    println("🚀 LilyPad3D Julia Server starting at http://$host:$port")
    
    server = nothing
    try
        server = HTTP.serve!(handle_request, host, port; verbose=false)
        SERVER[] = server
        while SERVER_RUNNING[]
            sleep(0.1)
        end
    catch e
        @error "Server error: $e"
    finally
        if !isnothing(server)
            close(server)
            println("✅ Server stopped")
        end
    end
end

main()