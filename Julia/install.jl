import Pkg
Pkg.activate(@__DIR__)
Pkg.resolve()
Pkg.instantiate()
Pkg.precompile()
println("LilyPadGH Julia env installed & precompiled.")
