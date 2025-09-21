include("2d.jl")
using HTTP

# Define a basic request handler
function handle_request(req::HTTP.Request)
    if req.method == "POST" && req.target == "/process"
        try
            
            msg = String(req.body)
            println("🔍 Raw body: ", msg)
            run_sim(msg)

            return HTTP.Response(200, "Simulation launched")

        catch e
            return HTTP.Response(400, "Invalid JSON: $(e)")
        end
    elseif req.target == "/status"
            return HTTP.Response(200, "Server is running")
    else
        return HTTP.Response(404, "Unknown endpoint")
    end
        #turn HTTP.Response(200, "Hello from Julia on localhost!")
end

# Start the server on localhost:8080
HTTP.serve(handle_request, "127.0.0.1", 8080)
