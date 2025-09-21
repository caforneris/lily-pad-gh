using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.Register_StringParam("console", "console", "console");
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var run = false;
            DA.GetData(0, ref run);

            if (run && (_process == null || _process.HasExited))
            { _process = new Process()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "C:\\Users\\grued\\AppData\\Local\\Programs\\Julia-1.11.7\\bin\\julia.exe",
                        Arguments = "C:\\Repos\\lily-pad-gh\\Julia\\RunServer.jl",
                        UseShellExecute = true,                       // must be true to open a new window
                        CreateNoWindow = false,                       // show the window
                        WindowStyle = ProcessWindowStyle.Normal       // optional: Normal, Minimized, etc.

                    }
                };
                _process.Start();
            }
            //else if (!run && _process != null)
            //{
            //    using var client = new HttpClient();
            //    var response = client.GetAsync("http://localhost:8080/shutdown").GetAwaiter().GetResult();

            //}

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