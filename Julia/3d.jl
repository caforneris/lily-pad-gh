using WaterLily,StaticArrays,GLMakie,JSON,LinearAlgebra

# Hardcoded sphere mesh data
function get_sphere_mesh()
    # Simple icosphere vertices (normalized to unit sphere)
    vertices = [
        [0.0f0, 0.0f0, 1.0f0],           # top
        [0.894f0, 0.0f0, 0.447f0],       # upper ring
        [0.276f0, 0.851f0, 0.447f0],
        [-0.724f0, 0.526f0, 0.447f0],
        [-0.724f0, -0.526f0, 0.447f0],
        [0.276f0, -0.851f0, 0.447f0],
        [0.724f0, 0.526f0, -0.447f0],    # lower ring
        [-0.276f0, 0.851f0, -0.447f0],
        [-0.894f0, 0.0f0, -0.447f0],
        [-0.276f0, -0.851f0, -0.447f0],
        [0.724f0, -0.526f0, -0.447f0],
        [0.0f0, 0.0f0, -1.0f0]           # bottom
    ]

    # Triangular faces (indices into vertices array, 1-based)
    faces = [
        [1, 2, 3], [1, 3, 4], [1, 4, 5], [1, 5, 6], [1, 6, 2],  # top cap
        [2, 7, 3], [3, 7, 8], [3, 8, 4], [4, 8, 9], [4, 9, 5],  # upper belt
        [5, 9, 10], [5, 10, 6], [6, 10, 11], [6, 11, 2], [2, 11, 7], # lower belt
        [7, 12, 8], [8, 12, 9], [9, 12, 10], [10, 12, 11], [11, 12, 7] # bottom cap
    ]

    return Dict("vertices" => vertices, "faces" => faces)
end

# Simple point-in-mesh test using ray casting
function point_in_mesh(point, vertices, faces)
    x, y, z = point
    ray_dir = SA[1.0f0, 0.0f0, 0.0f0]  # Cast ray in +x direction
    intersections = 0

    for face in faces
        v1, v2, v3 = [SA[vertices[i]...] for i in face]

        # Ray-triangle intersection test
        if ray_triangle_intersect(SA[x, y, z], ray_dir, v1, v2, v3)
            intersections += 1
        end
    end

    return intersections % 2 == 1  # Odd number of intersections = inside
end

# Basic ray-triangle intersection
function ray_triangle_intersect(ray_origin, ray_dir, v1, v2, v3)
    edge1 = v2 - v1
    edge2 = v3 - v1
    h = cross(ray_dir, edge2)
    a = dot(edge1, h)

    if abs(a) < 1f-10
        return false  # Ray parallel to triangle
    end

    f = 1.0f0 / a
    s = ray_origin - v1
    u = f * dot(s, h)

    if u < 0.0f0 || u > 1.0f0
        return false
    end

    q = cross(s, edge1)
    v = f * dot(ray_dir, q)

    if v < 0.0f0 || u + v > 1.0f0
        return false
    end

    t = f * dot(edge2, q)
    return t > 1f-10  # Intersection ahead of ray origin
end

# Approximate signed distance function from mesh
function mesh_sdf(point, vertices, faces, radius=1.0f0)
    # Scale vertices by radius
    scaled_vertices = [[v[1]*radius, v[2]*radius, v[3]*radius] for v in vertices]

    # Check if point is inside mesh
    inside = point_in_mesh(point, scaled_vertices, faces)

    # Find distance to closest triangle (simplified)
    min_dist = eltype(point)(Inf)
    for face in faces
        v1, v2, v3 = [SA[scaled_vertices[i]...] for i in face]
        dist = distance_to_triangle(point, v1, v2, v3)
        min_dist = min(min_dist, dist)
    end

    return inside ? -min_dist : min_dist
end

# Distance from point to triangle
function distance_to_triangle(p, a, b, c)
    # Simplified distance calculation - distance to triangle plane
    normal = cross(b - a, c - a)
    normal = normal / norm(normal)
    return abs(dot(p - a, normal))
end

function donut(L;Re=1e3,mem=Array,U=1)
    # Define simulation size, geometry dimensions, viscosity
    center,R,r = SA[L/2,L/2,L/2], L/4, L/16
    ν = U*R/Re

    # Apply signed distance function for a torus
    norm2(x) = √sum(abs2,x)
    body = AutoBody() do xyz,t
        x,y,z = xyz - center
        norm2(SA[x,norm2(SA[y,z])-R])-r
    end

    # Initialize simulation
    Simulation((2L,L,L),(U,0,0),R;ν,body,mem)
end

function sphere_from_mesh(L;Re=1e3,mem=Array,U=1)
    # Define simulation parameters
    center = SA[Float32(L/2),Float32(L/2),Float32(L/2)]
    R = Float32(L/4)  # sphere radius
    ν = Float32(U*R/Re)

    # Get mesh data
    mesh_data = get_sphere_mesh()
    vertices = mesh_data["vertices"]
    faces = mesh_data["faces"]

    # Create AutoBody from mesh
    body = AutoBody() do xyz,t
        # Translate to center and compute SDF
        translated_point = xyz - center
        mesh_sdf(translated_point, vertices, faces, R)
    end

    # Initialize simulation
    Simulation((2L,L,L),(U,0,0),R;ν,body,mem)
end

function ω_θ!(arr, sim)
    dt,a = sim.L/sim.U, sim.flow.σ
    center = SA{eltype(sim.flow.σ)}[2sim.L,2sim.L,2sim.L]
    @inside a[I] = WaterLily.ω_θ(I,(1,0,0),center,sim.flow.u)*dt
    copyto!(arr, a[inside(a)])
end

# make sim and run
# using CUDA
# sim = donut(2^5)#;mem=CUDA.CuArray);  # Original donut
sim = sphere_from_mesh(2^5)#;mem=CUDA.CuArray);  # New sphere from mesh
t₀ = sim_time(sim)
duration = 10.0
step = 0.25

viz!(sim;f=ω_θ!,duration,step,video="sphere.mp4",algorithm=:iso,isovalue=0.5) # remove video="sphere.mp4" for co-visualization during runtime