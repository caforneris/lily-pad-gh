# ========================================
# FILE: debug_simulation.jl
# DESC: Debug script to identify why simulations aren't saving
# ========================================

println("🔍 Starting LilyPad simulation debug...")
println("=" ^ 50)

# PART 1: Test Basic Environment
println("📁 Testing environment setup...")
try
    using WaterLily, StaticArrays, Plots, JSON3, ParametricBodies
    println("✅ All packages loaded successfully")
catch e
    println("❌ Package loading failed: $e")
    exit(1)
end

# PART 2: Test Plotting Backend
println("\n🎨 Testing plotting backend...")
try
    ENV["GKSwstype"] = "100"  # Headless mode
    gr()
    println("✅ GR backend set to headless mode")
    
    # Test simple plot
    test_plot = plot([1,2,3], [1,4,9], title="Test")
    println("✅ Basic plotting works")
catch e
    println("❌ Plotting setup failed: $e")
    println("🔧 Trying alternative backend...")
    try
        pyplot()
        println("✅ Switched to pyplot backend")
    catch e2
        println("❌ Alternative backend also failed: $e2")
    end
end

# PART 3: Test Directory Creation
println("\n📂 Testing directory creation...")
package_dir = ENV["APPDATA"] * "\\McNeel\\Rhinoceros\\packages\\8.0\\LilyPadGH\\0.0.1"
temp_dir = joinpath(package_dir, "Temp")

try
    if !isdir(package_dir)
        println("📁 Creating package directory: $package_dir")
        mkpath(package_dir)
    end
    
    if !isdir(temp_dir)
        println("📁 Creating temp directory: $temp_dir")
        mkpath(temp_dir)
    end
    
    # Test file creation
    test_file = joinpath(temp_dir, "test.txt")
    open(test_file, "w") do f
        write(f, "Test file creation")
    end
    
    if isfile(test_file)
        println("✅ Directory creation and file writing successful")
        rm(test_file)  # Clean up
    else
        println("❌ File creation failed")
    end
catch e
    println("❌ Directory/file creation failed: $e")
end

# PART 4: Test Simple Simulation
println("\n⚙️ Testing simple simulation...")
try
    # Create minimal simulation (no custom geometry)
    function make_simple_sim(;L=2^4, U=1, Re=100, T=Float32)
        # Simple circle body instead of complex JSON parsing
        R = T(L/8)
        body = AutoBody((x,t) -> hypot(x[1]-T(4L), x[2]-T(2L)) - R)
        Simulation((8L,4L),(U,0),L;U,ν=U*L/Re,body,T)
    end
    
    sim = make_simple_sim()
    println("✅ Simple simulation created successfully")
    
    # Test very short simulation step
    sim_step!(sim, 0.1)
    println("✅ Simulation step completed")
    
catch e
    println("❌ Simple simulation failed: $e")
    println("📋 Error details:")
    showerror(stdout, e)
    println()
end

# PART 5: Test GIF Creation
println("\n🎬 Testing GIF creation...")
try
    # Test minimal animation creation
    function test_gif_creation()
        # Create simple test plot
        x = 1:10
        y = rand(10)
        
        # Try to create and save a simple plot
        p = plot(x, y, title="Test Animation Frame")
        
        # Try to save as PNG first (simpler than GIF)
        test_png_path = joinpath(temp_dir, "test_plot.png")
        savefig(p, test_png_path)
        
        if isfile(test_png_path)
            println("✅ PNG creation successful: $test_png_path")
            rm(test_png_path)  # Clean up
            return true
        else
            println("❌ PNG creation failed")
            return false
        end
    end
    
    if test_gif_creation()
        println("✅ Basic plot saving works")
    else
        println("❌ Plot saving failed")
    end
    
catch e
    println("❌ GIF creation test failed: $e")
end

# PART 6: Test sim_gif! Function Directly
println("\n🎥 Testing WaterLily's sim_gif! function...")
try
    # Load the specific function
    include("2d.jl")
    
    # Create a very simple simulation
    sim = make_sim("{\"polylines\": []}", L=2^3, U=1, Re=50)  # Tiny simulation
    println("✅ Created simulation from 2d.jl")
    
    # Try very short gif
    println("🎬 Attempting to create 1-second GIF...")
    gif_result = sim_gif!(sim, duration=1, clims=(-5,5), plotbody=false)
    
    println("✅ sim_gif! completed successfully!")
    println("📁 GIF object: $gif_result")
    
    if hasfield(typeof(gif_result), :filename)
        actual_path = gif_result.filename
        println("📁 GIF file path: $actual_path")
        
        if isfile(actual_path)
            println("✅ GIF file exists at: $actual_path")
            
            # Try to move to our temp directory
            final_path = joinpath(temp_dir, "debug_test.gif")
            try
                cp(actual_path, final_path)
                println("✅ Successfully copied GIF to: $final_path")
            catch e
                println("⚠️ Could not copy GIF: $e")
                println("📁 Original GIF remains at: $actual_path")
            end
        else
            println("❌ GIF file does not exist at reported path")
        end
    else
        println("⚠️ GIF object doesn't have filename field")
        println("🔍 GIF object type: $(typeof(gif_result))")
        println("🔍 GIF object fields: $(fieldnames(typeof(gif_result)))")
    end
    
catch e
    println("❌ sim_gif! test failed: $e")
    println("📋 Error details:")
    showerror(stdout, e)
    println()
end

println("\n" * "=" ^ 50)
println("🏁 Debug completed. Check output above for issues.")
println("📁 Expected temp directory: $temp_dir")