# julietta.jl
# Read a GH-exported JSON (mask, domain, time, fluid), build a WaterLily sim,
# and visualize vorticity magnitude as a volume (MIP + :algae), jellyfish-style.

using JSON3, Base64
using WaterLily
using GLMakie
using Interpolations
using Statistics

# --- JSON helpers ---
function load_config(path::AbstractString)
    JSON3.read(read(path, String))
end

# x-fast flattening -> reshape to (nx,ny,nz)
function decode_mask(cfg)
    nx, ny, nz = Tuple(cfg["obstacles"]["shape"])
    bytes = base64decode(String(cfg["obstacles"]["data"]))
    @assert length(bytes) == nx*ny*nz "Mask length mismatch"
    mask = Array{Bool}(undef, nx, ny, nz)
    idx = 1
    @inbounds for k in 1:nz, j in 1:ny, i in 1:nx
        mask[i,j,k] = (bytes[idx] != 0x00)
        idx += 1
    end
    return mask
end

# --- Signed field φ from mask (IDENTITY mapping: sim coords == index coords) ---
function soft_phi(mask::Array{Bool,3})
    nx, ny, nz = size(mask)
    itp  = interpolate(Float32.(mask), BSpline(Linear()), OnGrid())
    eitp = Interpolations.extrapolate(itp, Interpolations.Flat())     # safe at edges
    sitp = Interpolations.scale(eitp, 1:nx, 1:ny, 1:nz)               # index space
    return (x, t) -> 0.5 - sitp(x[1], x[2], x[3])   # φ < 0 ⇒ solid
end

# vorticity magnitude for volume viz
function ω!(arr, sim)
    a = sim.flow.σ
    WaterLily.@inside a[I] = WaterLily.ω_mag(I, sim.flow.u)
    copyto!(arr, a[inside(a)])
end

function main(cfgpath::AbstractString)
    cfg = load_config(cfgpath)

    # --- domain (in GRID units, as written by exporter) ---
    nx, ny, nz = Tuple(cfg["domain"]["resolution"])
    # min/max are [0,0,0]..[nx,ny,nz]; dx=1 in grid space
    x0, y0, z0 = Tuple(cfg["domain"]["min"])   # unused now
    x1, y1, z1 = Tuple(cfg["domain"]["max"])   # unused now

    # --- time / fluid ---
    dt_viz    = Float64(cfg["time"]["dt"])           # viz cadence
    duration  = Float32(cfg["time"]["duration"])
    ν_req     = Float32(cfg["fluid"]["nu"])
    Ux, Uy, Uz = Tuple(cfg["fluid"]["forcing"]["velocity"])

    # --- obstacles ---
    mask = decode_mask(cfg)

    # sanity
    solid_frac = mean(Float32.(mask))
    fluid_frac = 1f0 - solid_frac
    @info "fractions" solid_frac fluid_frac
    if solid_frac < 1e-6 || fluid_frac < 1e-6
        error("Mask degenerate: solid_frac=$(solid_frac), fluid_frac=$(fluid_frac). Export both fluid and solid.")
    end

    # if mostly solid, flip sign
    flip_sign = fluid_frac < 0.05f0
    if flip_sign
        @warn "Mask looks inverted (fluid < 5%). Will flip φ sign."
    end

    # φ in identity (grid) space
    φraw = soft_phi(mask)
    φ    = flip_sign ? ((x,t)-> -φraw(x,t)) : φraw
    body = AutoBody(φ, (x,t)->x)

    # --- stable solver settings (CFL-ish in grid units; dx=1) ---
    ν    = max(ν_req, 0.05f0)
    Umag = sqrt(Ux*Ux + Uy*Uy + Uz*Uz)
    dx   = 1.0                                    # grid units
    dt_adv = Umag > 1e-6 ? 0.4 * dx / Umag : Inf
    dt_vis = 0.2 * dx*dx / max(ν, 1e-6)
    dt_sim = Float64(min(min(dt_adv, dt_vis), 0.05))
    @info "time steps (solver Δt, viz step, ν, |U|)" dt_sim dt_viz ν Umag

    # --- simulation: use gentle advective BCs via uBC tuple (like jellyfish) ---
    Ubc = (Float32(Ux), Float32(Uy), Float32(Uz))     # e.g., (0.3,0,0)
    R   = Float32(max(nx,ny,nz)) / 6f0
    sim = Simulation((nx,ny,nz), Ubc, R;
                     ν=ν, body=body, T=Float32, Δt=dt_sim)

    viz!(sim;
        f         = ω!,
        duration  = duration,
        step      = dt_viz,
        algorithm = :mip,
        colormap  = :algae,
        # video   = "julietta_demo.mp4",
    )
end

main(raw"C:\Users\bhowes\OneDrive - Thornton Tomasetti, Inc\Desktop\Julietta\test02.json")