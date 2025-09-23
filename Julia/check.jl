using LilyPadGH  # now precompiles/loads your package
using StaticArrays, WaterLily, Plots, JSON3, ParametricBodies, HTTP,
      GLMakie, CairoMakie, Interpolations, GeometryBasics
println("All packages loaded successfully! | ", LilyPadGH.greet())
