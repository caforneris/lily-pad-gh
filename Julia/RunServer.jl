include("2d.jl")
using HTTP

# Global flag to control server shutdown
const SERVER_RUNNING = Ref(true)

# Define a basic request handler
function handle_request(req::HTTP.Request)
    println("📨 Received $(req.method) request to $(req.target)")

    # Handle POST requests (support both / and /process endpoints)
    if req.method == "POST" && (req.target == "/" || req.target == "/process")
        try
            msg = String(req.body)
            println("🔍 Raw body: ", msg)

            # Run the simulation
            gif_path = run_sim(msg)

            return HTTP.Response(200, "Simulation completed. GIF saved at: $gif_path")

        catch e
            println("❌ Error processing request: $e")
            return HTTP.Response(400, "Error processing simulation: $(e)")
        end

    # Handle status requests
    elseif req.method == "GET" && req.target == "/status"
        return HTTP.Response(200, "Server is running on port 8080")

    # Handle shutdown requests
    elseif req.method == "GET" && req.target == "/shutdown"
        println("🛑 Shutdown requested")
        SERVER_RUNNING[] = false
        return HTTP.Response(200, "Server shutting down...")

    # Unknown endpoint
    else
        println("❓ Unknown endpoint: $(req.method) $(req.target)")
        return HTTP.Response(404, "Unknown endpoint. Use POST / or GET /status or GET /shutdown")
    end
end

# Start the server on localhost:8080 with proper shutdown handling
println("🚀 Starting Julia HTTP server on localhost:8080")
println("📍 Available endpoints:")
println("  POST /        - Process simulation data")
println("  POST /process - Process simulation data (alternative)")
println("  GET /status   - Check server status")
println("  GET /shutdown - Shutdown server")

try
    server = HTTP.serve!(handle_request, "127.0.0.1", 8080; verbose=false)

    # Keep server running until shutdown is requested
    while SERVER_RUNNING[]
        sleep(0.1)
    end

    println("🔄 Stopping server...")
    close(server)
    println("✅ Server stopped")

catch e
    println("❌ Server error: $e")
    exit(1)
end
