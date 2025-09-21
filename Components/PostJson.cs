using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text;

namespace LilyPad.Components
{
    public class PostJson : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the PostJson class.
        /// </summary>
        public PostJson()
            : base("PostJson", "PostJson",
                "Sends Json to Julia",
                "LilyPad", "Utilities")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Json Path", "Json Path", "Json Path", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {

        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string path = "";

            DA.GetData(0, ref path);

            if (!Path.Exists(path)) return;

            string fileContent = "";

            try
            {
                fileContent = File.ReadAllText(path);
                Console.WriteLine(fileContent);

            }
            catch (Exception e)
            {
                Console.WriteLine($"Error reading file: {e.Message}");
            }

            var response = SendJsonToJulia(fileContent);

            if (!string.IsNullOrEmpty(response))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, response);
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
            get { return new Guid("C173F614-708A-4493-92D7-037D2545949D"); }
        }

        public static string SendJsonToJulia(string json)
        {
            using var client = new HttpClient();

            var content = new StringContent(json, Encoding.UTF8, "text/plain");

            // Send the POST request synchronously
            var response = client.PostAsync("http://localhost:8080/process", content).GetAwaiter().GetResult();

            // Optional: check status code
            if (!response.IsSuccessStatusCode)
            {
                return "Julia server returned: " + response.StatusCode;
            }

            return "";
        }
    }
}