# ========================================
# FILE: RunServer3D.jl
# DESC: Persistent HTTP server that listens for 3D voxel data from Grasshopper,
#       runs a headless WaterLily.jl simulation, and saves the output.
# ========================================

using HTTP
using JSON3
using WaterLily

# --- Server State ---
const SERVER = Ref{HTTP.Server}() # Holds the server instance for graceful shutdown.

# --- Main Simulation Logic ---
# NOTE: This function will contain the core simulation logic, adapted from julietta.jl,
# but modified to run without a visualizer and save results to a file.
function run_simulation_from_json(json_data)
    # TODO: Add logic to decode the Base64 mask from json_data["obstacles"]["data"].
    # TODO: Set up the WaterLily.Simulation using parameters from the JSON.
    # TODO: Run the simulation headlessly.
    # TODO: Save the output (e.g., an MP4 animation or raw data) to a temporary file.

    println("Received data. Simulation processing would happen here.")
    return "Simulation task started on server."
end


# --- HTTP Handlers ---
function handle_request(req::HTTP.Request)
    if HTTP.method(req) == "POST" && HTTP.target(req) == "/"
        try
            json_data = JSON3.read(IOBuffer(HTTP.body(req)))
            result = run_simulation_from_json(json_data)
            return HTTP.Response(200, result)
        catch e
            @error "Failed to process request: $e"
            return HTTP.Response(500, "Internal Server Error: $e")
        end
    end
    return HTTP.Response(404)
end

function handle_shutdown(req::HTTP.Request)
    @info "Shutdown request received. Closing server."
    # Asynchronously close the server to allow the HTTP response to be sent.
    @async close(SERVER[])
    return HTTP.Response(200, "Server is shutting down.")
end


# --- Server Startup ---
function main()
    router = HTTP.Router()
    HTTP.register!(router, "POST", "/", handle_request)
    HTTP.register!(router, "GET", "/shutdown", handle_shutdown)

    host = "127.0.0.1"
    port = 8080
    
    println("🚀 LilyPad3D Julia Server starting at http://$host:$port")
    println("Waiting for data from Grasshopper...")
    
    # Start the server and store the instance.
    SERVER[] = HTTP.serve!(router, host, port)
end

# Run the server when the script is executed.
main()