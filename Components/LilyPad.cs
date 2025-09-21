// ========================================
// FILE: LilyPad.cs
// PART 1: MAIN COMPONENT
// DESC: Core Grasshopper component. Manages inputs/outputs, orchestrates the UI,
//       and integrates logic for both immediate data processing and a simulated
//       background analysis task.
// --- REVISIONS ---
// - 2025-09-20 @ 18:24: Added Julia integration.
//   - Added a new output parameter for Julia results.
//   - Updated SolveInstance with integrated Julia server control.
//   - Added Julia scripts and HTTP server communication.
// - 2025-09-21: Integrated server control into main component.
//   - Removed dependency on separate JuliaRunner class.
//   - Added server start/stop and data push buttons to ETO dialog.
// ========================================

using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using Rhino.UI;
using System;
using System.Collections.Generic;
using System.IO; // NOTE: Added for Path.Combine
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

// Resolve namespace conflicts - Grasshopper uses System.Drawing
using GH_Bitmap = System.Drawing.Bitmap;

namespace LilyPadGH.Components
{
    public class LilyPadCfdComponent : GH_Component
    {
        // Internal state exposed to attributes and dialogs
        internal bool _isRunning = false;
        internal bool _isServerRunning = false;
        internal string _status = "Ready to configure";
        internal LilyPadCfdSettings _settings = new LilyPadCfdSettings();

        // NOTE: Task management fields for handling the simulation process.
        private CancellationTokenSource _cancellationTokenSource;
        private LilyPadCfdDialog _activeDialog;
        private Process _juliaServerProcess = null;

        // Store the current JSON data for pushing to server
        private string _currentCurvesJson = "{}";
        private string _customJuliaPath = null;

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
            // --- Input Gathering with stability checks ---
            Rectangle3d boundary = Rectangle3d.Unset;
            var customCurves = new List<Curve>();
            string customJuliaPath = string.Empty;

            // Clear any previous error messages
            ClearRuntimeMessages();

            if (!DA.GetData(0, ref boundary))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Boundary input is required");
                return;
            }

            // Safely get optional inputs
            try
            {
                DA.GetDataList(1, customCurves); // Optional input - multiple curves
                DA.GetData(2, ref customJuliaPath); // Optional input - custom Julia path
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Input processing warning: {ex.Message}");
            }

            // Set custom Julia path if provided
            if (!string.IsNullOrEmpty(customJuliaPath))
            {
                _customJuliaPath = customJuliaPath;
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

            // Store the current curves JSON for server pushing
            _currentCurvesJson = curveJsonString;

            // --- JULIA INTEGRATION (Manual via server buttons) ---
            string juliaOutput = _isServerRunning ? "Server is running. Use 'Push Data to Server' button." : "Julia server not started. Use dialog to start server.";

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
            _activeDialog.OnStartServerClicked += HandleStartServerClicked;
            _activeDialog.OnStopServerClicked += HandleStopServerClicked;
            _activeDialog.OnPushDataClicked += HandlePushDataClicked;

            _activeDialog.SetRunningState(_isRunning);
            _activeDialog.SetServerState(_isServerRunning);

            _activeDialog.Closed += (s, e) => {
                _activeDialog.OnRunClicked -= HandleRunClicked;
                _activeDialog.OnStopClicked -= HandleStopClicked;
                _activeDialog.OnStartServerClicked -= HandleStartServerClicked;
                _activeDialog.OnStopServerClicked -= HandleStopServerClicked;
                _activeDialog.OnPushDataClicked -= HandlePushDataClicked;
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

        private void HandleStartServerClicked()
        {
            if (_isServerRunning) return;

            try
            {
                string juliaExePath = GetJuliaExecutablePath();
                string serverScriptPath = GetServerScriptPath();

                if (!File.Exists(juliaExePath))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Julia executable not found at {juliaExePath}");
                    return;
                }

                if (!File.Exists(serverScriptPath))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Server script not found at {serverScriptPath}");
                    return;
                }

                var threadCount = Environment.ProcessorCount;
                _juliaServerProcess = new Process()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = juliaExePath,
                        Arguments = $"--threads {threadCount} \"{serverScriptPath}\"",
                        UseShellExecute = true,
                        CreateNoWindow = false,
                        WindowStyle = ProcessWindowStyle.Normal
                    }
                };

                _juliaServerProcess.Start();
                _isServerRunning = true;
                _activeDialog?.SetServerState(_isServerRunning);

                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Julia server started successfully");
                ExpireSolution(true);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Failed to start Julia server: {ex.Message}");
            }
        }

        private void HandleStopServerClicked()
        {
            if (!_isServerRunning || _juliaServerProcess == null) return;

            try
            {
                // Send shutdown signal to Julia server
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                var response = client.GetAsync("http://localhost:8080/shutdown").GetAwaiter().GetResult();

                // Wait a bit for graceful shutdown
                System.Threading.Thread.Sleep(1000);

                // Force kill if still running
                if (!_juliaServerProcess.HasExited)
                {
                    _juliaServerProcess.Kill();
                }

                _isServerRunning = false;
                _juliaServerProcess = null;
                _activeDialog?.SetServerState(_isServerRunning);

                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Julia server stopped successfully");
                ExpireSolution(true);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Error stopping server: {ex.Message}");

                // Force kill as fallback
                if (_juliaServerProcess != null && !_juliaServerProcess.HasExited)
                {
                    _juliaServerProcess.Kill();
                }
                _isServerRunning = false;
                _juliaServerProcess = null;
                _activeDialog?.SetServerState(_isServerRunning);
                ExpireSolution(true);
            }
        }

        private void HandlePushDataClicked()
        {
            if (!_isServerRunning)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Julia server is not running");
                return;
            }

            try
            {
                // Create the JSON data to send to server
                var serverData = new
                {
                    simulation_parameters = new
                    {
                        reynolds_number = _settings.ReynoldsNumber,
                        velocity = _settings.Velocity,
                        grid_resolution_x = _settings.GridResolutionX,
                        grid_resolution_y = _settings.GridResolutionY,
                        duration = _settings.Duration
                    },
                    polylines = JsonSerializer.Deserialize<JsonElement>(_currentCurvesJson).GetProperty("polylines")
                };

                string jsonData = JsonSerializer.Serialize(serverData, new JsonSerializerOptions { WriteIndented = true });

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
                var response = client.PostAsync("http://localhost:8080/", content).GetAwaiter().GetResult();

                if (response.IsSuccessStatusCode)
                {
                    string responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Data pushed successfully: {responseText}");
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Server returned error: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Failed to push data to server: {ex.Message}");
            }
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
        // JULIA PATH DETECTION METHODS
        // ========================================

        private string GetJuliaExecutablePath()
        {
            // First, check if a custom path has been set
            if (!string.IsNullOrEmpty(_customJuliaPath))
            {
                string juliaPath = Path.Combine(_customJuliaPath, "bin", "julia.exe");
                if (File.Exists(juliaPath))
                {
                    return juliaPath;
                }
            }

            // Second, check the user's AppData\Local\Programs for Julia installation
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] possibleJuliaVersions = { "Julia-1.11.7", "Julia-1.11.6", "Julia-1.11.5", "Julia-1.11", "Julia-1.10" };

            foreach (var version in possibleJuliaVersions)
            {
                // Check the exact structure: AppData\Local\Programs\Julia-1.11.7\bin\julia.exe
                string juliaPath = Path.Combine(localAppData, "Programs", version, "bin", "julia.exe");
                if (File.Exists(juliaPath))
                {
                    return juliaPath;
                }
            }

            // Third, check the bundled Julia in the plugin directory
            string ghaDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string bundledJulia = Path.Combine(ghaDirectory, "JuliaPackage", "julia-1.11.7-win64", "bin", "julia.exe");

            if (File.Exists(bundledJulia))
            {
                return bundledJulia;
            }

            // If we get here, no Julia installation was found
            string expectedPath = Path.Combine(localAppData, "Programs", "Julia-1.11.7", "bin", "julia.exe");
            throw new FileNotFoundException(
                $"julia.exe not found. Please ensure Julia is installed at:\n" +
                $"- {expectedPath}\n" +
                $"Or provide a custom path via the Julia Path input.",
                expectedPath);
        }

        private string GetServerScriptPath()
        {
            // Check in the deployed package folder first
            string packageDirectory = Environment.ExpandEnvironmentVariables(@"%appdata%\McNeel\Rhinoceros\packages\8.0\LilyPadGH\0.0.1");
            string scriptPath = Path.Combine(packageDirectory, "Julia", "RunServer.jl");

            if (File.Exists(scriptPath))
            {
                return scriptPath;
            }

            // Fallback to the gha directory (for development/debugging)
            string ghaDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            scriptPath = Path.Combine(ghaDirectory, "Julia", "RunServer.jl");

            if (!File.Exists(scriptPath))
            {
                string packagePath = Path.Combine(packageDirectory, "Julia", "RunServer.jl");
                throw new FileNotFoundException(
                    $"RunServer.jl not found. Expected at:\n" +
                    $"- Package folder: {packagePath}\n" +
                    $"- Fallback folder: {scriptPath}\n" +
                    $"Ensure the Julia scripts are deployed to the package folder.",
                    scriptPath);
            }

            return scriptPath;
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