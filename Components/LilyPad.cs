// ========================================
// FILE: LilyPad.cs
// PART 1: MAIN COMPONENT
// DESC: Core Grasshopper component. Manages inputs/outputs, orchestrates the UI,
//       and integrates logic for both immediate data processing and a simulated
//       background analysis task.
// --- REVISIONS ---
// - 2025-09-20 @ 18:24: Added Julia integration.
//   - Added a new output parameter for Julia results.
//   - Updated SolveInstance to call the new JuliaRunner class.
//   - Added a "JuliaScripts" folder to the project structure.
// ========================================

using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using Rhino.UI;
using System;
using System.Collections.Generic;
using System.IO; // NOTE: Added for Path.Combine
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
            pManager.AddCurveParameter("Custom Curves", "Crvs", "Optional custom curves to be discretized (multiple closed polylines)", GH_ParamAccess.list);
            pManager.AddTextParameter("Julia Path", "JP", "Optional custom Julia installation path (e.g., C:\\Users\\YourName\\AppData\\Local\\Programs\\Julia-1.11.7)", GH_ParamAccess.item);
            pManager[1].Optional = true; // Mark Custom Curves as optional
            pManager[2].Optional = true; // Mark Julia Path as optional
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "S", "Current analysis status", GH_ParamAccess.item);
            pManager.AddTextParameter("Parameters", "P", "Simulation parameters as text", GH_ParamAccess.item);
            pManager.AddRectangleParameter("Boundary Rectangle", "BR", "Boundary plane as rectangle", GH_ParamAccess.item);
            pManager.AddTextParameter("Boundary JSON", "BJ", "Boundary plane in JSON format", GH_ParamAccess.item);
            pManager.AddTextParameter("Curves JSON", "CJ", "Custom curves points in JSON format (multiple polylines)", GH_ParamAccess.item);
            pManager.AddPointParameter("Curve Points", "Pts", "Discretized curve points from all curves", GH_ParamAccess.list);

            // NOTE: New output for Julia script results.
            pManager.AddTextParameter("Julia Output", "JO", "Output from the executed Julia script", GH_ParamAccess.item);
        }

        public override void CreateAttributes()
        {
            m_attributes = new LilyPadCfdAttributes(this);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- Input Gathering ---
            Rectangle3d boundary = Rectangle3d.Unset;
            var customCurves = new List<Curve>();
            string customJuliaPath = string.Empty;

            if (!DA.GetData(0, ref boundary)) return;
            DA.GetDataList(1, customCurves); // Optional input - multiple curves
            DA.GetData(2, ref customJuliaPath); // Optional input - custom Julia path

            // Set custom Julia path if provided
            if (!string.IsNullOrEmpty(customJuliaPath))
            {
                JuliaRunner.SetCustomJuliaPath(customJuliaPath);
            }

            if (!boundary.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid boundary rectangle");
                return;
            }

            // --- Data Processing (from reference component) ---
            string parameters = $"Reynolds Number: {_settings.ReynoldsNumber}\n" +
                              $"Velocity: {_settings.Velocity}\n" +
                              $"Grid Resolution: {_settings.GridResolutionX}x{_settings.GridResolutionY}\n" +
                              $"Boundary Width: {boundary.Width:F3}\n" +
                              $"Boundary Height: {boundary.Height:F3}\n" +
                              $"Duration: {_settings.Duration} seconds";

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

            string curveJsonString = "{}";
            var curvePoints = new List<Point3d>();
            if (customCurves != null && customCurves.Count > 0)
            {
                var polylinesList = new List<object>();

                foreach (var curve in customCurves)
                {
                    if (curve != null && curve.IsValid)
                    {
                        var pointListJson = new List<object>();
                        var collectedPoints = new List<Point3d>();

                        // Try to get polyline from curve for straight segment optimization
                        Polyline polyline;
                        bool isPolyline = curve.TryGetPolyline(out polyline);

                        if (isPolyline && polyline != null)
                        {
                            // It's a polyline - just use the vertices (already optimized for straight lines)
                            collectedPoints.AddRange(polyline);
                        }
                        else
                        {
                            // Try to check if it's a simple line
                            if (curve.IsLinear())
                            {
                                // It's a straight line - just use start and end points
                                collectedPoints.Add(curve.PointAtStart);
                                collectedPoints.Add(curve.PointAtEnd);
                            }
                            else
                            {
                                // It's a curve or complex shape - try to get segments
                                Curve[] segments = curve.DuplicateSegments();

                                if (segments != null && segments.Length > 0)
                                {
                                    for (int i = 0; i < segments.Length; i++)
                                    {
                                        var segment = segments[i];
                                        if (segment != null && segment.IsValid)
                                        {
                                            // Check if segment is a straight line
                                            if (segment.IsLinear())
                                            {
                                                // For straight lines, just use start point
                                                collectedPoints.Add(segment.PointAtStart);

                                                // Add end point only for last segment if not closed
                                                if (i == segments.Length - 1 && !curve.IsClosed)
                                                {
                                                    collectedPoints.Add(segment.PointAtEnd);
                                                }
                                            }
                                            else
                                            {
                                                // For curved segments, subdivide
                                                segment.DivideByCount(_settings.CurveDivisions, true, out Point3d[] points);
                                                if (points != null && points.Length > 0)
                                                {
                                                    // Skip last point if not the last segment (to avoid duplicates)
                                                    int pointsToAdd = (i < segments.Length - 1) ? points.Length - 1 : points.Length;
                                                    for (int j = 0; j < pointsToAdd; j++)
                                                    {
                                                        collectedPoints.Add(points[j]);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    // Fallback: treat as single curve and subdivide
                                    curve.DivideByCount(_settings.CurveDivisions, true, out Point3d[] points);
                                    if (points != null && points.Length > 0)
                                    {
                                        collectedPoints.AddRange(points);
                                    }
                                }
                            }
                        }

                        // Add collected points to both the output list and JSON
                        curvePoints.AddRange(collectedPoints);
                        foreach (var pt in collectedPoints)
                        {
                            pointListJson.Add(new { x = pt.X, y = pt.Y, z = pt.Z });
                        }

                        // Check if curve is closed
                        bool isClosed = curve.IsClosed;

                        // Use original simple structure
                        polylinesList.Add(new {
                            type = "closed_polyline",
                            is_closed = isClosed,
                            divisions = _settings.CurveDivisions,
                            points = pointListJson
                        });
                    }
                }

                if (polylinesList.Count > 0)
                {
                    var curvesJson = new {
                        type = "multiple_closed_polylines",
                        count = polylinesList.Count,
                        polylines = polylinesList
                    };
                    curveJsonString = JsonSerializer.Serialize(curvesJson, new JsonSerializerOptions { WriteIndented = true });
                }
            }

            // --- JULIA INTEGRATION ---
            string juliaOutput = "Julia script not executed.";
            try
            {
                // Get the path to RunServer.jl in the deployed package
                string serverScriptPath = JuliaRunner.GetServerScriptPath();

                if (File.Exists(serverScriptPath))
                {
                    // Create a JSON file with the boundary and curves data for Julia to read
                    string packageDirectory = Environment.ExpandEnvironmentVariables(@"%appdata%\McNeel\Rhinoceros\packages\8.0\LilyPadGH\0.0.1");
                    string juliaDataPath = Path.Combine(packageDirectory, "Julia", "input_data.json");

                    // Create combined data for Julia including simulation parameters
                    var juliaInputData = new
                    {
                        simulation_parameters = new
                        {
                            reynolds_number = _settings.ReynoldsNumber,
                            velocity = _settings.Velocity,
                            grid_resolution_x = _settings.GridResolutionX,
                            grid_resolution_y = _settings.GridResolutionY,
                            duration = _settings.Duration
                        },
                        boundary = new
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
                        },
                        curves = new
                        {
                            type = "multiple_closed_polylines",
                            count = customCurves?.Count ?? 0,
                            curves_json = curveJsonString
                        }
                    };

                    string juliaInputJson = JsonSerializer.Serialize(juliaInputData, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(juliaDataPath, juliaInputJson);

                    // Run Julia server with the data file path
                    string args = $"\"{juliaDataPath}\"";
                    juliaOutput = JuliaRunner.RunScript(serverScriptPath, args);
                }
                else
                {
                    juliaOutput = $"Error: RunServer.jl not found at {serverScriptPath}";
                }
            }
            catch (Exception ex)
            {
                // Display Julia errors on the component itself.
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Julia Error: {ex.Message}");
                juliaOutput = $"Execution failed: {ex.Message}";
            }

            // --- Output Assignment ---
            DA.SetData(0, _status);
            DA.SetData(1, parameters);
            DA.SetData(2, boundary);
            DA.SetData(3, boundaryJsonString);
            DA.SetData(4, curveJsonString);
            DA.SetDataList(5, curvePoints);
            DA.SetData(6, juliaOutput); // Set the new Julia output

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