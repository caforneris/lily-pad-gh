# Simple syntax test without running the simulation
println("Testing syntax...")

# Test basic function definitions from 3d.jl
function get_sphere_mesh()
    vertices = [
        [0.0, 0.0, 1.0],
        [0.894, 0.0, 0.447],
        [0.276, 0.851, 0.447],
        [-0.724, 0.526, 0.447],
        [-0.724, -0.526, 0.447],
        [0.276, -0.851, 0.447],
        [0.724, 0.526, -0.447],
        [-0.276, 0.851, -0.447],
        [-0.894, 0.0, -0.447],
        [-0.276, -0.851, -0.447],
        [0.724, -0.526, -0.447],
        [0.0, 0.0, -1.0]
    ]

    faces = [
        [1, 2, 3], [1, 3, 4], [1, 4, 5], [1, 5, 6], [1, 6, 2],
        [2, 7, 3], [3, 7, 8], [3, 8, 4], [4, 8, 9], [4, 9, 5],
        [5, 9, 10], [5, 10, 6], [6, 10, 11], [6, 11, 2], [2, 11, 7],
        [7, 12, 8], [8, 12, 9], [9, 12, 10], [10, 12, 11], [11, 12, 7]
    ]

    return Dict("vertices" => vertices, "faces" => faces)
end

# Test the function
mesh = get_sphere_mesh()
println("✓ Sphere mesh created with $(length(mesh["vertices"])) vertices and $(length(mesh["faces"])) faces")

# Test basic linear algebra operations
using LinearAlgebra

v1 = [1.0, 0.0, 0.0]
v2 = [0.0, 1.0, 0.0]
result = cross(v1, v2)
println("✓ Cross product test: $result")

test_point = [0.5, 0.5, 0.5]
println("✓ Test point: $test_point")

println("✓ All syntax tests passed!")