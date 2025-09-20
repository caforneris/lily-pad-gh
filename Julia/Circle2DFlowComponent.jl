############### Fluid + Makie end-to-end ###############
using WaterLily
using JSON, StaticArrays, Statistics
using ParametricBodies                # <-- added
using GLMakie
GLMakie.activate!()

# -------- Component / file info ----------
const cID = "Circle2DFlowComponent"

# ---------- IO ----------
function read_geometry_data(boundary_json_path, curve_json_path=nothing)
    boundary_data = JSON.parsefile(boundary_json_path)
    curve_data = (curve_json_path !== nothing && isfile(curve_json_path)) ?
        JSON.parsefile(curve_json_path) : nothing
    return boundary_data, curve_data
end

# ---------- Body from (closed) polyline ----------
function create_polyline_body(curve_points)
    px = Float32[p["x"] for p in curve_points]
    py = Float32[p["y"] for p in curve_points]

    # remove duplicate closing point if present
    if !isempty(px) &&
       isapprox(px[1], px[end]; atol=1e-6) &&
       isapprox(py[1], py[end]; atol=1e-6)
        pop!(px); pop!(py)
    end

    # ensure closed by appending first point
    cx = vcat(px, px[1])
    cy = vcat(py, py[1])
    n  = length(cx)

    # piecewise linear parametric curve u∈[0,1]
    curve(u, t) = begin
        uw = mod(u, 1.0f0)
        if uw == 1f0
            return SVector(cx[1], cy[1])
        end
        i  = min(Int(floor(uw*(n-1))) + 1, n-1)
        τ  = (uw*(n-1)) - (i-1)
        x  = cx[i] + τ*(cx[i+1]-cx[i])
        y  = cy[i] + τ*(cy[i+1]-cy[i])
        SVector(x,y)
    end

    # coarse closest-point search; good enough for sdf locate
    function locate(x, t)
        px, py = x[1], x[2]
        best_u, best_d = 0f0, typemax(Float32)
        for k in 0:200
            u  = Float32(k)/200
            q  = curve(u, t)
            d2 = (px-q[1])^2 + (py-q[2])^2
            if d2 < best_d
                best_d = d2; best_u = u
            end
        end
        best_u
    end

    body = ParametricBodies.ParametricBody(curve, locate)
    return body, curve
end

# ---------- Outline plotting in sim space ----------
function sim_outline(curve_func; npts=300, x_min=0f0, y_min=0f0)
    uvals = range(0f0, 1f0; length=npts)
    pts   = [curve_func(u, 0f0) for u in uvals]
    sx    = [p[1] - x_min for p in pts]
    sy    = [p[2] - y_min for p in pts]
    sx, sy
end

# ---------- Simulation setup ----------
function circle_flow_with_boundary(boundary_json_path, curve_json_path=nothing;
                                   Re=250f0, U=1f0, n=192, m=128, ν=nothing)

    boundary, curve = read_geometry_data(boundary_json_path, curve_json_path)

    width   = Float32(boundary["width"])
    height  = Float32(boundary["height"])
    cx_real = Float32(boundary["center"]["x"])
    cy_real = Float32(boundary["center"]["y"])

    # extents (real coordinates)
    cp = boundary["corner_points"]
    x_min = Float32(minimum(p["x"] for p in cp)); x_max = Float32(maximum(p["x"] for p in cp))
    y_min = Float32(minimum(p["y"] for p in cp)); y_max = Float32(maximum(p["y"] for p in cp))

    # domain size in sim space equals rectangle size in real space
    Lx, Ly = width, height

    # body in REAL space
    if curve !== nothing && haskey(curve, "points") && !isempty(curve["points"])
        body_real, curve_func = create_polyline_body(curve["points"])
        println("Using custom polyline body with $(length(curve["points"])) vertices")
    else
        # default circle in REAL space
        R = Float32(min(width, height)/8)
        body_real = AutoBody((x, t) -> hypot(x[1]-cx_real, x[2]-cy_real) - R)
        curve_func = (u, t) -> begin
            θ = Float32(2π)*Float32(u)
            SVector(cx_real + R*cos(θ), cy_real + R*sin(θ))
        end
        println("Using default circle body (R=$(R))")
    end

    # Translate sim-coords (x_sim∈[0,L]) → REAL coords
    body_sim = AutoBody((x, t) -> begin
        x_real = SVector(x[1] + x_min, x[2] + y_min)
        WaterLily.sdf(body_real, x_real, t)
    end)

    # viscosity from Re if not provided
    if ν === nothing
        Lchar = max(Lx, Ly)
        ν = U * Lchar / Re
    end

    # build simulation on rectangular domain L=(Lx,Ly)
    sim = Simulation((n, m), (U, 0f0), (Lx, Ly); ν=Float32(ν), body=body_sim)
    return sim, (x_min, y_min, Lx, Ly), curve_func
end

# ---------- Vorticity utilities ----------
percentile_clim(vort, p=0.98) = begin
    a = abs.(vec(vort))
    c = quantile(a, p)
    c > 0 ? c : maximum(a)
end

# ---------- GLMakie: live viz + recording ----------
function visualize_makie!(sim, domain_info, curve_func; warmup_steps=200, duration=4.0, fps=24)
    x_min, y_min, Lx, Ly = domain_info

    # warm up flow
    for _ in 1:warmup_steps
        WaterLily.mom_step!(sim.flow, sim.pois)
    end

    vort = WaterLily.curl(sim.flow.u)[:, :, 1]
    nx, ny = size(vort)
    xs = range(0f0, Lx; length=nx)
    ys = range(0f0, Ly; length=ny)

    fig  = Figure(resolution=(1100, 760))
    ax   = Axis(fig[1,1], title="Vorticity Field — $(cID)", xlabel="X", ylabel="Y",
                aspect=DataAspect())

    cl = percentile_clim(vort, 0.985)
    hm = heatmap!(ax, xs, ys, permutedims(vort); colormap=:RdBu, colorrange=(-cl, cl))
    Colorbar(fig[1,2], hm, label="ω")

    sx, sy = sim_outline(curve_func; x_min=x_min, y_min=y_min)
    lines!(ax, sx, sy; color=:black, linewidth=3)

    tightlimits!(ax)
    display(fig)

    total_frames = max(1, Int(round(duration*fps)))
    record(fig, "$(cID)_makie_animation.mp4", 1:total_frames; framerate=fps) do k
        for _ in 1:3
            WaterLily.mom_step!(sim.flow, sim.pois)
        end
        vort .= WaterLily.curl(sim.flow.u)[:, :, 1]
        cl = percentile_clim(vort, 0.985)
        hm.colorrange[] = (-cl, cl)
        hm[3] = permutedims(vort)
        fig
    end

    save("$(cID)_makie_final.png", fig)
    @info "Saved" png="$(cID)_makie_final.png" mp4="$(cID)_makie_animation.mp4"
    return fig
end

# ---------- High-level runner ----------
function run_simulation_from_json(boundary_json_path, curve_json_path=nothing;
                                  Re=250f0, U=1f0, n=192, m=128,
                                  warmup_steps=300, duration=5.0, fps=24)

    sim, domain_info, curve_func =
        circle_flow_with_boundary(boundary_json_path, curve_json_path;
                                  Re=Re, U=U, n=n, m=m)

    WaterLily.logger(cID)  # optional residual logging
    fig = visualize_makie!(sim, domain_info, curve_func;
                           warmup_steps=warmup_steps, duration=duration, fps=fps)
    return sim, fig
end

# ---------- CLI main ----------
if abspath(PROGRAM_FILE) == @__FILE__
    json_dir    = joinpath(@__DIR__, "JSON")
    boundarypth = joinpath(json_dir, "boundary_json.json")
    curvepth    = joinpath(json_dir, "curve_json.json")

    if !isfile(boundarypth)
        @error "boundary_json.json not found" path=boundarypth
        exit(1)
    end

    curvepth = isfile(curvepth) ? curvepth : nothing
    sim, fig = run_simulation_from_json(boundarypth, curvepth;
                                        Re=350f0, U=1f0,
                                        n=256, m=192,
                                        warmup_steps=500,
                                        duration=6.0, fps=24)
end
