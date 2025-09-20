using System;
using System.Collections.Generic;
using System.Text.Json;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace LilyPadGH.Components
{
    public class Circle2DFlowComponent : GH_Component
    {
        public Circle2DFlowComponent()
          : base("2D Circle Flow", "Circle2D",
              "Simulates 2D flow around a circular obstacle using WaterLily parameters",
              "LilyPadGH", "Flow Simulation")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Reynolds Number", "Re", "Reynolds number for the flow simulation", GH_ParamAccess.item, 250.0);
            pManager.AddNumberParameter("Velocity", "U", "Flow velocity", GH_ParamAccess.item, 1.0);
            pManager.AddIntegerParameter("Grid Resolution X", "n", "Number of grid points in X direction", GH_ParamAccess.item, 192);
            pManager.AddIntegerParameter("Grid Resolution Y", "m", "Number of grid points in Y direction", GH_ParamAccess.item, 128);
            pManager.AddNumberParameter("Duration", "t", "Simulation duration", GH_ParamAccess.item, 10.0);
            pManager.AddRectangleParameter("Boundary Plane", "B", "Boundary plane as rectangle (x,y dimensions)", GH_ParamAccess.item);
            pManager.AddCurveParameter("Custom Curve", "Crv", "Custom curve to be discretized", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Curve Divisions", "Div", "Number of divisions for custom curve", GH_ParamAccess.item, 50);

            pManager[0].Optional = true;
            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = false;
            pManager[6].Optional = true;
            pManager[7].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Parameters", "P", "Simulation parameters as text", GH_ParamAccess.item);
            pManager.AddRectangleParameter("Boundary Rectangle", "BR", "Boundary plane as rectangle", GH_ParamAccess.item);
            pManager.AddTextParameter("Boundary JSON", "BJ", "Boundary plane in JSON format", GH_ParamAccess.item);
            pManager.AddTextParameter("Curve JSON", "CJ", "Custom curve points in JSON format", GH_ParamAccess.item);
            pManager.AddPointParameter("Curve Points", "Pts", "Discretized curve points", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double reynoldsNumber = 250.0;
            double velocity = 1.0;
            int gridX = 192;
            int gridY = 128;
            double duration = 10.0;
            Rectangle3d boundary = Rectangle3d.Unset;
            Curve customCurve = null;
            int curveDivisions = 50;

            DA.GetData(0, ref reynoldsNumber);
            DA.GetData(1, ref velocity);
            DA.GetData(2, ref gridX);
            DA.GetData(3, ref gridY);
            DA.GetData(4, ref duration);

            if (!DA.GetData(5, ref boundary))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Boundary plane input is required");
                return;
            }

            if (!boundary.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid boundary rectangle");
                return;
            }

            DA.GetData(6, ref customCurve);
            DA.GetData(7, ref curveDivisions);

            string parameters = $"Reynolds Number: {reynoldsNumber}\n" +
                              $"Velocity: {velocity}\n" +
                              $"Grid Resolution: {gridX}x{gridY}\n" +
                              $"Boundary Width: {boundary.Width:F3}\n" +
                              $"Boundary Height: {boundary.Height:F3}\n" +
                              $"Duration: {duration} seconds";

            var boundaryJson = new
            {
                type = "rectangle",
                center = new { x = boundary.Center.X, y = boundary.Center.Y, z = boundary.Center.Z },
                width = boundary.Width,
                height = boundary.Height,
                corner_points = new[]
                {
                    new { x = boundary.Corner(0).X, y = boundary.Corner(0).Y },
                    new { x = boundary.Corner(1).X, y = boundary.Corner(1).Y },
                    new { x = boundary.Corner(2).X, y = boundary.Corner(2).Y },
                    new { x = boundary.Corner(3).X, y = boundary.Corner(3).Y }
                }
            };

            string boundaryJsonString = JsonSerializer.Serialize(boundaryJson, new JsonSerializerOptions { WriteIndented = true });

            string curveJsonString = "";
            List<Point3d> curvePoints = new List<Point3d>();

            if (customCurve != null && customCurve.IsValid)
            {
                customCurve.DivideByCount(curveDivisions, true, out Point3d[] points);
                if (points != null && points.Length > 0)
                {
                    curvePoints.AddRange(points);

                    var curveJson = new
                    {
                        type = "custom_curve",
                        divisions = curveDivisions,
                        points = new List<object>()
                    };

                    foreach (var pt in points)
                    {
                        curveJson.points.Add(new { x = pt.X, y = pt.Y, z = pt.Z });
                    }

                    curveJsonString = JsonSerializer.Serialize(curveJson, new JsonSerializerOptions { WriteIndented = true });
                }
            }

            DA.SetData(0, parameters);
            DA.SetData(1, boundary);
            DA.SetData(2, boundaryJsonString);
            DA.SetData(3, curveJsonString);
            DA.SetDataList(4, curvePoints);
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
            get { return new Guid("b4f7c3a2-8d1e-4f93-9c7b-e3d9a6f2b8e1"); }
        }
    }
}