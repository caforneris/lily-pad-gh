using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace LilyPadGH.Components
{
    public class StartStopJulia : GH_Component
    {
        private Process _process = null;
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public StartStopJulia()
          : base("StartJulia", "StartJulia",
              "Starts or stops the Julia Server",
              "LilyPadGH", "Utilities")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Run", "Run", "If set to true, server will be on. If false, server will be off", GH_ParamAccess.item);
            pManager.AddTextParameter("Julia Install Directory", "Julia Install Directory", "Optional: Custom Julia installation directory (e.g., C:\\Users\\YourName\\AppData\\Local\\Programs\\Julia-1.11.7). Leave empty to use default.", GH_ParamAccess.item);
            pManager[1].Optional = true; // Make Julia directory optional
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_StringParam("console", "console", "console");
        }

        /// <summary>
        /// 
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var run = false;
            var installDir = "";
            DA.GetData(0, ref run);
            DA.GetData(1, ref installDir);

            var threadCount = Environment.ProcessorCount;

            // Get RunServer.jl from the deployed package folder, not the Julia installation
            string packageDirectory = Environment.ExpandEnvironmentVariables(@"%appdata%\McNeel\Rhinoceros\packages\8.0\LilyPadGH\0.0.1");
            var runServerPath = Path.Combine(packageDirectory, "Julia", "RunServer.jl");

            if (!File.Exists(runServerPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"RunServer.jl not found at {runServerPath}. Ensure the Julia scripts are deployed.");
                return;
            }

            if (run && (_process == null || _process.HasExited))
            {
                // Get the Julia executable path
                string juliaExePath;
                if (!string.IsNullOrEmpty(installDir) && Directory.Exists(installDir))
                {
                    // Use custom Julia installation
                    juliaExePath = Path.Combine(installDir, "bin", "julia.exe");
                }
                else
                {
                    // Use default Julia installation
                    string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    juliaExePath = Path.Combine(localAppData, "Programs", "Julia-1.11.7", "bin", "julia.exe");
                }

                if (!File.Exists(juliaExePath))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Julia executable not found at {juliaExePath}. Please check your Julia installation.");
                    return;
                }

                _process = new Process()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = juliaExePath,
                        Arguments = $"--threads {threadCount} \"{runServerPath}\"",
                        UseShellExecute = true,                       // must be true to open a new window
                        CreateNoWindow = false,                       // show the window
                        WindowStyle = ProcessWindowStyle.Normal       // optional: Normal, Minimized, etc.
                    }
                };
                _process.Start();
            }
            else if (!run && _process != null && !_process.HasExited)
            {
                try
                {
                    // Send shutdown signal to Julia server
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(5); // Set timeout
                    var response = client.GetAsync("http://localhost:8080/shutdown").GetAwaiter().GetResult();
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Server shutdown signal sent. Response: {response.StatusCode}");

                    // Wait a bit for graceful shutdown
                    System.Threading.Thread.Sleep(1000);

                    // Force kill if still running
                    if (!_process.HasExited)
                    {
                        _process.Kill();
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Julia process terminated.");
                    }
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Could not gracefully shutdown server: {ex.Message}. Killing process.");
                    if (!_process.HasExited)
                    {
                        _process.Kill();
                    }
                }
                finally
                {
                    _process = null;
                }
            }

        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("F131ECE4-BE07-4DB8-B4BA-A30EE76DE0E8"); }
        }
    }
}