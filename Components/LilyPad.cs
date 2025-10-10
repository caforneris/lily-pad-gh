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
// - 2025-09-22: Bundled Julia package for distribution.
//   - Removed Julia Path input parameter (eliminated user path dependency).
//   - Updated path detection to prioritise bundled Julia in package directory.
//   - Simplified path detection logic for team GitHub workflow.
//   - Julia now deploys to %APPDATA%\McNeel\Rhinoceros\packages\8.0\LilyPadGH\0.0.1\Julia.
// - 2025-09-22: Updated for simplified UI with ImageView and UITimer.
//   - Integrated with new dialog structure using temp file approach.
//   - Simplified event handling to match new dialog events.
//   - Removed complex real-time display component in favour of simple approach.
// - 2025-10-10: Added GIF display integration.
//   - Updated to pass UI GIF path to Julia for in-dialog display.
//   - GIF now displays in simulation view instead of external viewer.
//   - Enhanced UI integration for better user experience.
// ========================================

using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using Rhino.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
// Resolve namespace conflicts - Grasshopper uses System.Drawing
using GH_Bitmap = System.Drawing.Bitmap;

namespace LilyPadGH.Components
{
    public class LilyPadCfdComponent : GH_Component
    {
        // =======================
        // Part 1 — Internal State
        // =======================
        #region Internal State

        // Internal state exposed to attributes and dialogs
        internal bool _isRunning = false;
        internal bool _isServerRunning = false;
        internal string _status = "Ready to configure";
        internal LilyPadCfdSettings _settings = new LilyPadCfdSettings();
        internal string _latestAnimationPath = ""; // Path to most recent GIF animation

        // Task management fields for handling the simulation process
        private LilyPadCfdDialog _activeDialog;
        private Process _juliaServerProcess = null;

        // Store the current JSON data for pushing to server
        private string _currentCurvesJson = "{}";

        #endregion

        // ==========================
        // Part 2 — Constructor
        // ==========================
        #region Constructor

        public LilyPadCfdComponent() : base(
            "LilyPad CFD Analysis",
            "LilyPad",
            "Simulates 2D flow and generates WaterLily-compatible parameters with real-time display.",
            "LilyPadGH",
            "Flow Simulation")
        {
        }

        #endregion

        // ================================
        // Part 3 — Input/Output Registration
        // ================================
        #region Input/Output Registration

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // Core geometric inputs. All simulation parameters are handled via the Eto dialog.
            // Julia is now bundled - no path input required.
            pManager.AddRectangleParameter("Boundary Plane", "B", "Boundary plane as rectangle (x,y dimensions)", GH_ParamAccess.item);
            pManager.AddCurveParameter("Custom Curves", "Crvs", "Optional custom curves to be discretised (multiple closed polylines)", GH_ParamAccess.list);
            pManager[1].Optional = true; // Mark Custom Curves as optional
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "S", "Current analysis status", GH_ParamAccess.item);
            pManager.AddTextParameter("Parameters", "P", "Simulation parameters as text", GH_ParamAccess.item);
            pManager.AddRectangleParameter("Boundary Rectangle", "BR", "Boundary plane as rectangle", GH_ParamAccess.item);
            pManager.AddTextParameter("Boundary JSON", "BJ", "Boundary plane in JSON format", GH_ParamAccess.item);
            pManager.AddTextParameter("Curves JSON", "CJ", "Custom curves points in JSON format (multiple polylines)", GH_ParamAccess.item);
            pManager.AddPointParameter("Curve Points", "Pts", "Discretised curve points from all curves", GH_ParamAccess.list);
            pManager.AddTextParameter("Julia Output", "JO", "Output from the executed Julia script", GH_ParamAccess.item);
            pManager.AddTextParameter("Animation Path", "AP", "File path to the generated simulation GIF animation", GH_ParamAccess.item);
        }

        #endregion

        // ================================
        // Part 4 — Custom Attributes
        // ================================
        #region Custom Attributes

        public override void CreateAttributes()
        {
            m_attributes = new LilyPadCfdAttributes(this);
        }

        #endregion

        // ================================
        // Part 5 — Main Solution Logic
        // ================================
        #region Main Solution Logic

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- Input Gathering with stability checks ---
            Rectangle3d boundary = Rectangle3d.Unset;
            var customCurves = new List<Curve>();

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
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Input processing warning: {ex.Message}");
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

                        // Try to get polyline from curve for straight segment optimisation
                        Polyline polyline;
                        bool isPolyline = curve.TryGetPolyline(out polyline);

                        if (isPolyline && polyline != null)
                        {
                            // It's a polyline - just use the vertices (already optimised for straight lines)
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
                        polylinesList.Add(new
                        {
                            type = "closed_polyline",
                            is_closed = isClosed,
                            divisions = _settings.CurveDivisions,
                            points = pointListJson
                        });
                    }
                }

                if (polylinesList.Count > 0)
                {
                    var curvesJson = new
                    {
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
            string juliaOutput = _isServerRunning ?
                (_isRunning ? "Simulation running with real-time display..." : "Server ready - Real-time display active") :
                "Julia server not started. Use dialog to start server.";

            // --- Output Assignment ---
            DA.SetData(0, _status);
            DA.SetData(1, parameters);
            DA.SetData(2, boundary);
            DA.SetData(3, boundaryJsonString);
            DA.SetData(4, curveJsonString);
            DA.SetDataList(5, curvePoints);
            DA.SetData(6, juliaOutput);
            DA.SetData(7, _latestAnimationPath); // Animation path output

            // Update component message on canvas to reflect current state
            if (_isRunning)
            {
                Message = "Simulating...";
            }
            else if (_isServerRunning)
            {
                Message = "Server Ready";
            }
            else
            {
                Message = "Not Running";
            }
        }

        #endregion

        // ================================
        // Part 6 — Dialog Management
        // ================================
        #region Dialog Management

        public void ShowConfigDialog()
        {
            if (_activeDialog != null && _activeDialog.Visible)
            {
                _activeDialog.BringToFront();
                return;
            }

            _activeDialog = new LilyPadCfdDialog(_settings);

            // Wire up events to match new dialog structure
            _activeDialog.OnStartServerClicked += HandleStartServerClicked;
            _activeDialog.OnStopServerClicked += HandleStopServerClicked;
            _activeDialog.OnApplyAndRunClicked += HandleApplyAndRunClicked;

            _activeDialog.SetServerState(_isServerRunning);

            _activeDialog.Closed += (s, e) => {
                // Clean up event handlers
                _activeDialog.OnStartServerClicked -= HandleStartServerClicked;
                _activeDialog.OnStopServerClicked -= HandleStopServerClicked;
                _activeDialog.OnApplyAndRunClicked -= HandleApplyAndRunClicked;
                _activeDialog = null;
            };

            // For Eto, set the Owner property first, then call Show() with no arguments.
            _activeDialog.Owner = RhinoEtoApp.MainWindow;
            _activeDialog.Show();
        }

        #endregion

        // ================================
        // Part 7 — Server Control Handlers
        // ================================
        #region Server Control Handlers

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

                // Wait for server to fully start before enabling UI
                Task.Run(async () =>
                {
                    await Task.Delay(3000); // Give Julia 3 seconds to start HTTP server

                    RhinoApp.InvokeOnUiThread(() =>
                    {
                        _isServerRunning = true;
                        _activeDialog?.SetServerState(_isServerRunning);
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Julia server started successfully");
                        ExpireSolution(true);
                    });
                });

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
                // Stop any running simulation first
                if (_isRunning)
                {
                    _isRunning = false;
                }

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

        private void HandleApplyAndRunClicked(LilyPadCfdSettings newSettings)
        {
            if (!_isServerRunning)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Julia server is not running");
                return;
            }

            // Update settings and start simulation with recording
            _settings = newSettings;

            // Debug output to verify paths are set
            RhinoApp.WriteLine("=== DEBUG: UI Paths ===");
            RhinoApp.WriteLine($"UI Frame Path: {_settings.UIFramePath}");
            RhinoApp.WriteLine($"UI GIF Path: {_settings.UIGifPath}");
            RhinoApp.WriteLine($"UI GIF Path Empty: {string.IsNullOrEmpty(_settings.UIGifPath)}");
            RhinoApp.WriteLine("=======================");

            ExpireSolution(true);

            // Start simulation with recording
            Task.Run(async () =>
            {
                await Task.Delay(500); // Give time for geometry to update

                try
                {
                    // Create the JSON data to send to server with UI paths
                    var serverData = new
                    {
                        simulation_parameters = new
                        {
                            // Original GH simulation parameters
                            reynolds_number = _settings.ReynoldsNumber,
                            velocity = _settings.Velocity,
                            grid_resolution_x = _settings.GridResolutionX,
                            grid_resolution_y = _settings.GridResolutionY,
                            duration = _settings.Duration,
                            curve_divisions = _settings.CurveDivisions,

                            // Julia-specific parameters
                            L = _settings.SimulationL,
                            U = _settings.SimulationU,
                            Re = _settings.ReynoldsNumber, // Use Reynolds from GH
                            animation_duration = _settings.AnimationDuration,
                            plot_body = _settings.PlotBody,
                            simplify_tolerance = _settings.SimplifyTolerance,
                            max_points_per_poly = _settings.MaxPointsPerPoly,

                            // UI integration paths
                            ui_frame_path = _settings.UIFramePath,  // Live PNG frames
                            ui_gif_path = _settings.UIGifPath       // Final GIF output
                        },
                        polylines = JsonSerializer.Deserialize<JsonElement>(_currentCurvesJson).GetProperty("polylines"),
                        enable_temp_file_output = true // Signal to write frames to temp file
                    };

                    string jsonData = JsonSerializer.Serialize(serverData, new JsonSerializerOptions { WriteIndented = true });

                    // Debug: Print the JSON being sent
                    RhinoApp.WriteLine("=== DEBUG: JSON Sent to Julia ===");
                    RhinoApp.WriteLine(jsonData.Length > 500 ? jsonData.Substring(0, 500) + "..." : jsonData);
                    RhinoApp.WriteLine("=================================");

                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(30);
                    var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync("http://localhost:8080/", content);

                    RhinoApp.InvokeOnUiThread(() =>
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            string responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Simulation started: {responseText}");

                            _isRunning = true;
                            ExpireSolution(true);

                            // Monitor for new GIF file creation
                            Task.Run(async () =>
                            {
                                string packageTempDir = Path.Combine(
                                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                    "McNeel", "Rhinoceros", "packages", "8.0", "LilyPadGH", "0.0.1", "Temp"
                                );

                                // Wait up to 60 seconds for GIF to appear
                                for (int i = 0; i < 60; i++)
                                {
                                    await Task.Delay(1000);

                                    if (Directory.Exists(packageTempDir))
                                    {
                                        var gifFiles = Directory.GetFiles(packageTempDir, "simulation_*.gif")
                                            .Select(f => new FileInfo(f))
                                            .OrderByDescending(f => f.LastWriteTime)
                                            .FirstOrDefault();

                                        if (gifFiles != null && (DateTime.Now - gifFiles.LastWriteTime).TotalSeconds < 5)
                                        {
                                            RhinoApp.InvokeOnUiThread(() =>
                                            {
                                                _latestAnimationPath = gifFiles.FullName;
                                                _isRunning = false;
                                                ExpireSolution(true);
                                            });
                                            break;
                                        }
                                    }
                                }
                            });
                        }
                        else
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Server returned error: {response.StatusCode}");
                        }
                    });
                }
                catch (Exception ex)
                {
                    RhinoApp.InvokeOnUiThread(() =>
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Failed to start simulation: {ex.Message}");
                        _isRunning = false;
                        ExpireSolution(true);
                    });
                }
            });
        }

        #endregion

        // ================================
        // Part 8 — Component State Management
        // ================================
        #region Component State Management

        /// <summary>
        /// Helper to update component state and trigger a redraw from any thread.
        /// </summary>
        private void UpdateComponentState(string status, bool isRunning)
        {
            _status = status;
            _isRunning = isRunning;

            RhinoApp.InvokeOnUiThread(() =>
            {
                ExpireSolution(true);
            });
        }

        #endregion

        // ================================
        // Part 9 — Julia Path Detection
        // ================================
        #region Julia Path Detection

        /// <summary>
        /// Bundled Julia path detection - prioritises package deployment location.
        /// Eliminates user-specific path dependencies for team GitHub workflow.
        /// </summary>
        private string GetJuliaExecutablePath()
        {
            // First priority: Check the bundled Julia in the package directory
            string packageDirectory = Environment.ExpandEnvironmentVariables(@"%appdata%\McNeel\Rhinoceros\packages\8.0\LilyPadGH\0.0.1");
            string bundledJulia = Path.Combine(packageDirectory, "Julia", "julia-1.11.7-win64", "bin", "julia.exe");

            if (File.Exists(bundledJulia))
            {
                return bundledJulia;
            }

            // Second priority: Check fallback in GHA directory (development/debugging)
            string ghaDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string devJulia = Path.Combine(ghaDirectory, "JuliaPackage", "julia-1.11.7-win64", "bin", "julia.exe");

            if (File.Exists(devJulia))
            {
                return devJulia;
            }

            // Third priority: Check user's system Julia installation (backwards compatibility)
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] possibleJuliaVersions = { "Julia-1.11.7", "Julia-1.11.6", "Julia-1.11.5", "Julia-1.11", "Julia-1.10" };

            foreach (var version in possibleJuliaVersions)
            {
                string juliaPath = Path.Combine(localAppData, "Programs", version, "bin", "julia.exe");
                if (File.Exists(juliaPath))
                {
                    return juliaPath;
                }
            }

            // If we get here, no Julia installation was found - provide helpful error
            throw new FileNotFoundException(
                $"Julia executable not found. Expected bundled Julia at:\n" +
                $"- Package location: {bundledJulia}\n" +
                $"- Development location: {devJulia}\n" +
                $"Ensure the Julia package is properly deployed with the component.",
                bundledJulia);
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

        #endregion

        // ================================
        // Part 10 — Component Metadata
        // ================================
        #region Component Metadata

        // PANEL PLACEMENT
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

        #endregion
    }
}