using System;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace LilyPadGH.Components
{
    public class SampleComponent : GH_Component
    {
        public SampleComponent()
          : base("Sample Component", "Sample",
              "A sample component for LilyPadGH",
              "LilyPadGH", "Basic")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Text", "T", "Input text", GH_ParamAccess.item, "Hello World");
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Output", "O", "Output text", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string inputText = "";
            if (!DA.GetData(0, ref inputText)) return;

            DA.SetData(0, $"LilyPadGH says: {inputText}");
        }

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return null;
            }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("87654321-4321-4321-4321-210987654321"); }
        }
    }
}