// ========================================
// FILE: LilyPad.cs
// PART 1: MAIN COMPONENT
// DESC: Core Grasshopper component. Manages inputs/outputs, orchestrates the UI,
//       and integrates logic for both immediate data processing and a simulated
//       background analysis task.
// ========================================

using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using Rhino.UI; // NOTE: Added for Eto window owner assignment
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

// Resolve namespace conflicts - Grasshopper uses System.Drawing
using GH_Bitmap = System.Drawing.Bitmap;

namespace LilyPadGH.Components
{
    public class LilyPadCfdComponent : GH_Component
    {
        // Internal state exposed to attributes and dialogs
        internal bool _isRunning = false;
        internal string _status = "Ready to configure";
        internal LilyPadCfdSettings _settings = new LilyPadCfdSettings();

        // NOTE: Task management fields for handling the simulation process.
        private CancellationTokenSource _cancellationTokenSource;
        private LilyPadCfdDialog _activeDialog;

        public LilyPadCfdComponent() : base(
            "LilyPad CFD Analysis",
            "LilyPad",
            "Simulates 2D flow and generates WaterLily-compatible parameters.",
            "LilyPadGH",
            "Flow Simulation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // NOTE: Core geometric inputs. All simulation parameters are handled via the Eto dialog.
            pManager.AddRectangleParameter("Boundary Plane", "B", "Boundary plane as rectangle (x,y dimensions)", GH_ParamAccess.item);
            pManager.AddCurveParameter("Custom Curve", "Crv", "Optional custom curve to be discretized", GH_ParamAccess.item);
            pManager[1].Optional = true; // Mark Custom Curve as optional
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "S", "Current analysis status", GH_ParamAccess.item);
            pManager.AddTextParameter("Parameters", "P", "Simulation parameters as text", GH_ParamAccess.item);
            pManager.AddRectangleParameter("Boundary Rectangle", "BR", "Boundary plane as rectangle", GH_ParamAccess.item);
            pManager.AddTextParameter("Boundary JSON", "BJ", "Boundary plane in JSON format", GH_ParamAccess.item);
            pManager.AddTextParameter("Curve JSON", "CJ", "Custom curve points in JSON format", GH_ParamAccess.item);
            pManager.AddPointParameter("Curve Points", "Pts", "Discretized curve points", GH_ParamAccess.list);
        }

        public override void CreateAttributes()
        {
            m_attributes = new LilyPadCfdAttributes(this);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- Input Gathering ---
            Rectangle3d boundary = Rectangle3d.Unset;
            Curve customCurve = null;

            if (!DA.GetData(0, ref boundary)) return;
            DA.GetData(1, ref customCurve); // Optional input

            if (!boundary.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid boundary rectangle");
                return;
            }

            // --- Data Processing (from reference component) ---

            // Generate a summary string of all parameters
            string parameters = $"Reynolds Number: {_settings.ReynoldsNumber}\n" +
                              $"Velocity: {_settings.Velocity}\n" +
                              $"Grid Resolution: {_settings.GridResolutionX}x{_settings.GridResolutionY}\n" +
                              $"Boundary Width: {boundary.Width:F3}\n" +
                              $"Boundary Height: {boundary.Height:F3}\n" +
                              $"Duration: {_settings.Duration} seconds";

            // Create an anonymous object for boundary JSON serialization
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

            // Process the optional curve input
            string curveJsonString = "{}"; // Default to empty JSON object
            var curvePoints = new List<Point3d>();
            if (customCurve != null && customCurve.IsValid)
            {
                customCurve.DivideByCount(_settings.CurveDivisions, true, out Point3d[] points);
                if (points != null && points.Length > 0)
                {
                    curvePoints.AddRange(points);
                    var pointListJson = new List<object>();
                    foreach (var pt in points)
                    {
                        pointListJson.Add(new { x = pt.X, y = pt.Y, z = pt.Z });
                    }
                    var curveJson = new { type = "custom_curve", divisions = _settings.CurveDivisions, points = pointListJson };
                    curveJsonString = JsonSerializer.Serialize(curveJson, new JsonSerializerOptions { WriteIndented = true });
                }
            }

            // --- Output Assignment ---
            DA.SetData(0, _status);
            DA.SetData(1, parameters);
            DA.SetData(2, boundary);
            DA.SetData(3, boundaryJsonString);
            DA.SetData(4, curveJsonString);
            DA.SetDataList(5, curvePoints);

            // Update component message on canvas
            Message = _isRunning ? "Running..." : "Configured";
        }


        // ========================================
        // ASYNC & UI HANDLING
        // ========================================

        public void ShowConfigDialog()
        {
            if (_activeDialog != null && _activeDialog.Visible)
            {
                _activeDialog.BringToFront();
                return;
            }

            _activeDialog = new LilyPadCfdDialog(_settings);
            _activeDialog.OnRunClicked += HandleRunClicked;
            _activeDialog.OnStopClicked += HandleStopClicked;

            _activeDialog.SetRunningState(_isRunning);

            _activeDialog.Closed += (s, e) => {
                _activeDialog.OnRunClicked -= HandleRunClicked;
                _activeDialog.OnStopClicked -= HandleStopClicked;
                _activeDialog = null;
            };

            // NOTE: For Eto, set the Owner property first, then call Show() with no arguments.
            // This correctly parents the dialog to the main Rhino/GH window.
            _activeDialog.Owner = RhinoEtoApp.MainWindow;
            _activeDialog.Show();
        }

        private void HandleRunClicked(LilyPadCfdSettings newSettings)
        {
            if (_isRunning) return;

            _settings = newSettings;
            _cancellationTokenSource = new CancellationTokenSource();

            // NOTE: Fire-and-forget async task. UI remains responsive.
            Task.Run(() => RunSimulationAsync(_cancellationTokenSource.Token));

            // Immediately re-calculate the component's outputs with the new settings
            ExpireSolution(true);
        }

        private void HandleStopClicked()
        {
            _cancellationTokenSource?.Cancel();
        }

        private async Task RunSimulationAsync(CancellationToken token)
        {
            try
            {
                _isRunning = true;
                UpdateComponentState("Simulation starting...", true);

                // Simulate work based on duration and a fixed delay per step
                int steps = (int)(_settings.Duration / 0.1); // Assuming 10 steps per second of duration
                for (int i = 0; i <= steps; i++)
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Delay(100, token); // 100ms delay simulates one step
                    double progress = (double)i / steps;
                    UpdateComponentState($"Running... {progress:P0}", true);
                }

                UpdateComponentState("Simulation completed.", false);
            }
            catch (OperationCanceledException)
            {
                UpdateComponentState("Simulation stopped by user.", false);
            }
            catch (Exception ex)
            {
                UpdateComponentState($"Error: {ex.Message}", false);
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                _isRunning = false;
                UpdateComponentState("Ready to configure", false); // Final UI state refresh
            }
        }

        // NOTE: Helper to update component state and trigger a redraw from any thread.
        private void UpdateComponentState(string status, bool isRunning)
        {
            _status = status;
            _isRunning = isRunning;

            RhinoApp.InvokeOnUiThread(() =>
            {
                _activeDialog?.SetRunningState(isRunning);
                ExpireSolution(true);
            });
        }

        // ========================================
        // COMPONENT METADATA
        // ========================================

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override GH_Bitmap Icon
        {
            get
            {
                try
                {
                    var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LilyPadGH.Icons.CFDAnalysisIcon.png");
                    return stream != null ? new GH_Bitmap(stream) : null;
                }
                catch
                {
                    return null;
                }
            }
        }

        public override Guid ComponentGuid => new Guid("a4b1c2d3-e5f6-4a9b-8c7d-ef1234567890");
    }
}