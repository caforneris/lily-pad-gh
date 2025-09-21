using WaterLily,StaticArrays,LinearAlgebra

# Hardcoded sphere mesh data
function get_sphere_mesh()
    vertices = [
        [0.0, 0.0, 1.0],           # top
        [0.894, 0.0, 0.447],       # upper ring
        [0.276, 0.851, 0.447],
        [-0.724, 0.526, 0.447],
        [-0.724, -0.526, 0.447],
        [0.276, -0.851, 0.447],
        [0.724, 0.526, -0.447],    # lower ring
        [-0.276, 0.851, -0.447],
        [-0.894, 0.0, -0.447],
        [-0.276, -0.851, -0.447],
        [0.724, -0.526, -0.447],
        [0.0, 0.0, -1.0]           # bottom
    ]

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
    ray_dir = SA[1.0, 0.0, 0.0]  # Cast ray in +x direction
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

    if abs(a) < 1e-10
        return false  # Ray parallel to triangle
    end

    f = 1.0 / a
    s = ray_origin - v1
    u = f * dot(s, h)

    if u < 0.0 || u > 1.0
        return false
    end

    q = cross(s, edge1)
    v = f * dot(ray_dir, q)

    if v < 0.0 || u + v > 1.0
        return false
    end

    t = f * dot(edge2, q)
    return t > 1e-10  # Intersection ahead of ray origin
end

# Distance from point to triangle
function distance_to_triangle(p, a, b, c)
    # Simplified distance calculation - distance to triangle plane
    normal = cross(b - a, c - a)
    normal = normal / norm(normal)
    return abs(dot(p - a, normal))
end

# Approximate signed distance function from mesh
function mesh_sdf(point, vertices, faces, radius=1.0)
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

# Test the sphere simulation
println("Creating sphere simulation...")
sim = sphere_from_mesh(2^4)  # Smaller grid for faster testing
println("✓ Sphere simulation created successfully!")
println("Domain size: $(size(sim.flow.σ))")
println("Sphere center: $(SA[Float32(2^4/2),Float32(2^4/2),Float32(2^4/2)])")
println("Sphere radius: $(Float32(2^4/4))")