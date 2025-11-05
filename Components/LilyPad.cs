// ========================================
// FILE: LilyPad.cs (COMPLETE CORRECTED VERSION)
// DESC: Core Grasshopper component for WaterLily CFD simulation.
//       Manages inputs/outputs, orchestrates the UI dialogue, and handles Julia server communication.
// --- REVISIONS ---
// - 2025-10-11: Fixed simulation duration issue.
//   - Removed AnimationDuration parameter confusion.
//   - TotalSimulationTime now correctly controls simulation length.
//   - Improved parameter extraction logging in Julia.
//   - Added regions for better code organisation.
// - 2025-10-11 (FIX): Corrected fallback GIF monitor logic.
//   - The background file monitor now checks for the specific GIF path (`_settings.UIGifPath`).
//   - This aligns it with the dialogue's corrected logic and improves reliability.
// ========================================

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Rhino;
using Rhino.Geometry;
using Rhino.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GH_Bitmap = System.Drawing.Bitmap;

namespace LilyPadGH.Components
{
    public class LilyPadCfdComponent : GH_Component
    {
        // =======================
        // Region: Internal State
        // =======================
        #region Internal State

        // Note: Component state flags for tracking server and simulation status.
        // These drive the UI feedback and control flow.
        internal bool _isRunning = false;
        internal bool _isServerRunning = false;
        internal string _status = "Ready to configure";
        internal LilyPadCfdSettings _settings = new LilyPadCfdSettings();
        internal string _latestAnimationPath = "";

        // Note: References to active UI elements and processes.
        // Dialog is kept alive until user closes it or component is disposed.
        private LilyPadCfdDialog _activeDialog;
        private Process _juliaServerProcess = null;
        private string _currentCurvesJson = "{}";
        private bool _disposed = false;

        #endregion

        // ==========================
        // Region: Constructor
        // ==========================
        #region Constructor

        public LilyPadCfdComponent() : base(
            "LilyPad CFD Analysis",
            "LilyPad",
            "Simulates 2D flow and generates WaterLily-compatible parameters with real-time display.",
            "LilyPadGH",
            "Flow Simulation")
        {
            // Note: Hook into document close event for cleanup.
            // Ensures Julia server is stopped when Grasshopper document closes.
            Instances.DocumentServer.DocumentRemoved += OnDocumentRemoved;
        }

        #endregion

        // ================================
        // Region: Input/Output Registration
        // ================================
        #region Input/Output Registration

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddRectangleParameter("Boundary Plane", "B", "Boundary plane as rectangle (x,y dimensions)", GH_ParamAccess.item);
            pManager.AddCurveParameter("Custom Curves", "Crvs", "Optional custom curves to be discretised (multiple closed polylines)", GH_ParamAccess.list);
            pManager[1].Optional = true;
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
        // Region: Custom Attributes
        // ================================
        #region Custom Attributes

        // Note: Replaces default component attributes with custom button UI.
        // Allows users to open configuration dialogue directly from canvas.
        public override void CreateAttributes()
        {
            m_attributes = new LilyPadCfdAttributes(this);
        }

        #endregion

        // ================================
        // Region: Main Solution Logic
        // ================================
        #region Main Solution Logic

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Rectangle3d boundary = Rectangle3d.Unset;
            var customCurves = new List<Curve>();

            ClearRuntimeMessages();

            if (!DA.GetData(0, ref boundary))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Boundary input is required");
                return;
            }

            try
            {
                DA.GetDataList(1, customCurves);
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

            // Note: Build parameter summary string for display output.
            // Shows current CFD configuration at a glance.
            string parameters = $"Reynolds Number: {_settings.ReynoldsNumber}\n" +
                              $"Inlet Velocity: {_settings.InletVelocity} m/s\n" +
                              $"Kinematic Viscosity: {_settings.KinematicViscosity:E3} m²/s\n" +
                              $"Grid Resolution: {_settings.GridResolutionX}×{_settings.GridResolutionY} cells\n" +
                              $"Domain Size: {_settings.DomainWidth}×{_settings.DomainHeight} m\n" +
                              $"Time Step: {_settings.TimeStep} s\n" +
                              $"Total Simulation Time: {_settings.TotalSimulationTime} s";

            // Note: Serialize boundary geometry to JSON for Julia consumption.
            // Includes centre point, dimensions, and corner coordinates.
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

            // Note: Process custom curves into discretised polylines.
            // Handles linear segments efficiently, divides curved segments according to user settings.
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

                        Polyline polyline;
                        bool isPolyline = curve.TryGetPolyline(out polyline);

                        if (isPolyline && polyline != null)
                        {
                            // Note: Already a polyline, use vertices directly.
                            collectedPoints.AddRange(polyline);
                        }
                        else
                        {
                            if (curve.IsLinear())
                            {
                                // Note: Linear curve, just use endpoints.
                                collectedPoints.Add(curve.PointAtStart);
                                collectedPoints.Add(curve.PointAtEnd);
                            }
                            else
                            {
                                Curve[] segments = curve.DuplicateSegments();

                                if (segments != null && segments.Length > 0)
                                {
                                    // Note: Process each segment individually.
                                    // Avoids duplicating points at segment boundaries.
                                    for (int i = 0; i < segments.Length; i++)
                                    {
                                        var segment = segments[i];
                                        if (segment != null && segment.IsValid)
                                        {
                                            if (segment.IsLinear())
                                            {
                                                collectedPoints.Add(segment.PointAtStart);

                                                // Note: Only add endpoint for last segment if curve is open.
                                                if (i == segments.Length - 1 && !curve.IsClosed)
                                                {
                                                    collectedPoints.Add(segment.PointAtEnd);
                                                }
                                            }
                                            else
                                            {
                                                segment.DivideByCount(_settings.CurveDivisions, true, out Point3d[] points);
                                                if (points != null && points.Length > 0)
                                                {
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
                                    // Note: No segments detected, divide entire curve.
                                    curve.DivideByCount(_settings.CurveDivisions, true, out Point3d[] points);
                                    if (points != null && points.Length > 0)
                                    {
                                        collectedPoints.AddRange(points);
                                    }
                                }
                            }
                        }

                        curvePoints.AddRange(collectedPoints);
                        foreach (var pt in collectedPoints)
                        {
                            pointListJson.Add(new { x = pt.X, y = pt.Y, z = pt.Z });
                        }

                        bool isClosed = curve.IsClosed;

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

            // Note: Store curves JSON for later use when simulation is triggered.
            // Avoids re-processing geometry on every solution.
            _currentCurvesJson = curveJsonString;

            string juliaOutput = _isServerRunning ?
                (_isRunning ? "Simulation running with real-time display..." : "Server ready - Real-time display active") :
                "Julia server not started. Use dialogue to start server.";

            DA.SetData(0, _status);
            DA.SetData(1, parameters);
            DA.SetData(2, boundary);
            DA.SetData(3, boundaryJsonString);
            DA.SetData(4, curveJsonString);
            DA.SetDataList(5, curvePoints);
            DA.SetData(6, juliaOutput);
            DA.SetData(7, _latestAnimationPath);

            // Note: Update component display message based on current state.
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
        // Region: Dialogue Management
        // ================================
        #region Dialogue Management

        // Note: Shows configuration dialogue, reusing existing instance if already open.
        // Dialogue persists until user closes it, allowing parameter tweaking without reopening.
        public void ShowConfigDialog()
        {
            if (_activeDialog != null && _activeDialog.Visible)
            {
                _activeDialog.BringToFront();
                return;
            }

            _activeDialog = new LilyPadCfdDialog(_settings);

            _activeDialog.OnStartServerClicked += HandleStartServerClicked;
            _activeDialog.OnStopServerClicked += HandleStopServerClicked;
            _activeDialog.OnApplyAndRunClicked += HandleApplyAndRunClicked;

            _activeDialog.SetServerState(_isServerRunning);

            _activeDialog.Closed += (s, e) => {
                _activeDialog.OnStartServerClicked -= HandleStartServerClicked;
                _activeDialog.OnStopServerClicked -= HandleStopServerClicked;
                _activeDialog.OnApplyAndRunClicked -= HandleApplyAndRunClicked;
                _activeDialog = null;
            };

            _activeDialog.Owner = RhinoEtoApp.MainWindow;
            _activeDialog.Show();
        }

        #endregion

        // ================================
        // Region: Server Control Handlers
        // ================================
        #region Server Control Handlers

        // Note: Starts Julia HTTP server on port 8080.
        // Checks for existing server first to avoid port conflicts.
        private void HandleStartServerClicked()
        {
            if (_isServerRunning) return;

            // Note: Check if port 8080 is already in use.
            // Another Julia instance might be running from a previous session.
            if (IsPortInUse(8080))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Julia server already running on port 8080. Attempting to connect...");

                // Note: Test connectivity to existing server.
                // If responsive, we can reuse it instead of starting a new one.
                Task.Run(async () =>
                {
                    await Task.Delay(500);
                    try
                    {
                        using var client = new HttpClient();
                        client.Timeout = TimeSpan.FromSeconds(2);
                        var response = await client.GetAsync("http://localhost:8080/status");

                        if (response.IsSuccessStatusCode)
                        {
                            RhinoApp.InvokeOnUiThread(() =>
                            {
                                _isServerRunning = true;
                                _activeDialog?.SetServerState(_isServerRunning);
                                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                                    "Connected to existing Julia server");
                                ExpireSolution(true);
                            });
                        }
                    }
                    catch
                    {
                        RhinoApp.InvokeOnUiThread(() =>
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                                "Port 8080 is in use but server is not responding. Please close other applications using port 8080.");
                        });
                    }
                });

                return;
            }

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

                // Note: Use all available CPU threads for maximum performance.
                // WaterLily benefits significantly from multi-threading.
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

                // Note: Wait 3 seconds for Julia to initialise packages and start HTTP server.
                // Package precompilation may occur on first run, taking longer.
                Task.Run(async () =>
                {
                    await Task.Delay(3000);

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
            if (!_isServerRunning) return;

            StopJuliaServer();
        }

        // Note: Applies user settings and sends simulation request to Julia server.
        // Handles JSON serialisation, HTTP communication, and result monitoring.
        private void HandleApplyAndRunClicked(LilyPadCfdSettings newSettings)
        {
            if (!_isServerRunning)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Julia server is not running");
                return;
            }

            _settings = newSettings;

            RhinoApp.WriteLine("=== DEBUG: UI Paths ===");
            RhinoApp.WriteLine($"UI Frame Path: {_settings.UIFramePath}");
            RhinoApp.WriteLine($"UI GIF Path: {_settings.UIGifPath}");
            RhinoApp.WriteLine($"UI GIF Path Empty: {string.IsNullOrEmpty(_settings.UIGifPath)}");
            RhinoApp.WriteLine($"Total Simulation Time: {_settings.TotalSimulationTime}s");
            RhinoApp.WriteLine("=======================");

            ExpireSolution(true);

            Task.Run(async () =>
            {
                await Task.Delay(500);

                try
                {
                    // CRITICAL: Configure JsonSerializer to preserve exact property names (not convert to camelCase).
                    // Julia expects snake_case keys like "total_simulation_time", not "totalSimulationTime".
                    var jsonOptions = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = null // Don't convert property names
                    };

                    var serverData = new
                    {
                        simulation_parameters = new
                        {
                            // Fluid properties
                            fluid_density = _settings.FluidDensity,
                            kinematic_viscosity = _settings.KinematicViscosity,
                            inlet_velocity = _settings.InletVelocity,
                            reynolds_number = _settings.ReynoldsNumber,

                            // Domain configuration
                            domain_width = _settings.DomainWidth,
                            domain_height = _settings.DomainHeight,
                            grid_resolution_x = _settings.GridResolutionX,
                            grid_resolution_y = _settings.GridResolutionY,
                            characteristic_length = _settings.CharacteristicLength,

                            // Time stepping
                            time_step = _settings.TimeStep,

                            // CRITICAL FIX: Only send total_simulation_time, not animation_duration.
                            // Julia's run_sim function uses this value for both simulation length and animation duration.
                            total_simulation_time = _settings.TotalSimulationTime,

                            cfl_number = _settings.CFLNumber,

                            // Solver settings
                            max_iterations = _settings.MaxIterations,
                            convergence_tolerance = _settings.ConvergenceTolerance,
                            relaxation_factor = _settings.RelaxationFactor,

                            // Geometry
                            curve_divisions = _settings.CurveDivisions,
                            simplify_tolerance = _settings.SimplifyTolerance,
                            max_points_per_poly = _settings.MaxPointsPerPoly,
                            object_scale_factor = _settings.ObjectScaleFactor,

                            // Visualization
                            color_scale_min = _settings.ColorScaleMin,
                            color_scale_max = _settings.ColorScaleMax,
                            animation_fps = _settings.AnimationFPS,
                            show_body = _settings.ShowBody,
                            show_velocity_vectors = _settings.ShowVelocityVectors,
                            show_pressure = _settings.ShowPressure,
                            show_vorticity = _settings.ShowVorticity,

                            // Advanced WaterLily settings
                            waterlily_l = _settings.WaterLilyL,
                            use_gpu = _settings.UseGPU,
                            precision_type = _settings.PrecisionType,

                            // UI integration paths
                            // Note: These paths tell Julia where to write real-time frames and final GIF.
                            // When ui_gif_path is provided, Julia won't open external viewer.
                            ui_frame_path = _settings.UIFramePath,
                            ui_gif_path = _settings.UIGifPath
                        },
                        polylines = JsonSerializer.Deserialize<JsonElement>(_currentCurvesJson).GetProperty("polylines"),
                        enable_temp_file_output = true
                    };

                    string jsonData = JsonSerializer.Serialize(serverData, jsonOptions);

                    // Note: Calculate estimated simulation time based on grid complexity.
                    // Provides realistic user expectations for computation time.
                    int totalCells = _settings.GridResolutionX * _settings.GridResolutionY;
                    string estimate = totalCells < 10000 ? "< 1 min" :
                                     totalCells < 50000 ? "1-5 min" :
                                     totalCells < 100000 ? "5-15 min" : "> 15 min";

                    RhinoApp.WriteLine("=== CORRECTED SIMULATION PARAMETERS ===");
                    RhinoApp.WriteLine($"Grid: {_settings.GridResolutionX}x{_settings.GridResolutionY} = {totalCells} cells");
                    RhinoApp.WriteLine($"Total Simulation Time: {_settings.TotalSimulationTime}s (USED FOR BOTH SIM & ANIMATION)");
                    RhinoApp.WriteLine($"Estimated time: {estimate}");
                    RhinoApp.WriteLine("=======================================");

                    using var client = new HttpClient();

                    // CRITICAL: Extended timeout for long-running simulations.
                    // High-resolution simulations with long durations can take significant time.
                    // 60 minutes should be sufficient for most reasonable use cases.
                    client.Timeout = TimeSpan.FromMinutes(60);

                    var content = new StringContent(jsonData, Encoding.UTF8, "application/json");

                    RhinoApp.WriteLine($"🚀 Starting simulation (estimated: {estimate})...");

                    var response = await client.PostAsync("http://localhost:8080/", content);

                    RhinoApp.InvokeOnUiThread(() =>
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            string responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Simulation completed successfully");
                            RhinoApp.WriteLine($"Julia response: {responseText}");

                            _isRunning = true;
                            ExpireSolution(true);

                            // Note: Monitor for new GIF file creation in package temp directory.
                            // Provides feedback when animation is ready even if HTTP already timed out.
                            Task.Run(async () =>
                            {
                                // Wait up to 120 seconds for GIF to appear
                                for (int i = 0; i < 120; i++)
                                {
                                    await Task.Delay(1000);

                                    // CRITICAL FIX: Check for the specific GIF path from settings.
                                    // This ensures the component monitor and UI dialogue monitor are consistent.
                                    // This logic acts as a fallback if the UI dialogue is closed prematurely.
                                    if (!string.IsNullOrEmpty(_settings.UIGifPath) && File.Exists(_settings.UIGifPath))
                                    {
                                        var gifInfo = new FileInfo(_settings.UIGifPath);

                                        if (gifInfo != null && (DateTime.Now - gifInfo.LastWriteTime).TotalSeconds < 5)
                                        {
                                            RhinoApp.InvokeOnUiThread(() =>
                                            {
                                                _latestAnimationPath = gifInfo.FullName;
                                                _isRunning = false;
                                                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Animation saved: {gifInfo.Name}");
                                                ExpireSolution(true);
                                            });
                                            break;
                                        }
                                    }
                                }

                                // If we exit the loop without finding GIF
                                if (_isRunning)
                                {
                                    RhinoApp.InvokeOnUiThread(() =>
                                    {
                                        _isRunning = false;
                                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Simulation completed but GIF not found in temp folder");
                                        ExpireSolution(true);
                                    });
                                }
                            });
                        }
                        else
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Server returned error: {response.StatusCode}");
                            _isRunning = false;
                            ExpireSolution(true);
                        }
                    });
                }
                catch (TaskCanceledException)
                {
                    RhinoApp.InvokeOnUiThread(() =>
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                            "Simulation is still running but took longer than expected. " +
                            "Check Julia console for progress. The GIF will appear when complete.");
                        _isRunning = false;
                        ExpireSolution(true);
                    });
                }
                catch (HttpRequestException ex)
                {
                    RhinoApp.InvokeOnUiThread(() =>
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"HTTP request failed: {ex.Message}");
                        _isRunning = false;
                        ExpireSolution(true);
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
        // Region: Component State Management
        // ================================
        #region Component State Management

        // Note: Updates component status and triggers re-solution.
        // Called from async callbacks to update UI thread safely.
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
        // Region: Julia Path Detection
        // ================================
        #region Julia Path Detection

        // Note: Searches for Julia executable in multiple locations.
        // Priority: bundled package > development folder > system installation.
        private string GetJuliaExecutablePath()
        {
            string packageDirectory = Environment.ExpandEnvironmentVariables(@"%appdata%\McNeel\Rhinoceros\packages\8.0\LilyPadGH\0.0.1");
            string bundledJulia = Path.Combine(packageDirectory, "Julia", "julia-1.11.7-win64", "bin", "julia.exe");

            if (File.Exists(bundledJulia))
            {
                return bundledJulia;
            }

            string ghaDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string devJulia = Path.Combine(ghaDirectory, "JuliaPackage", "julia-1.11.7-win64", "bin", "julia.exe");

            if (File.Exists(devJulia))
            {
                return devJulia;
            }

            // Note: Check for system-wide Julia installation as fallback.
            // Tries multiple recent versions in descending order.
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

            throw new FileNotFoundException(
                $"Julia executable not found. Expected bundled Julia at:\n" +
                $"- Package location: {bundledJulia}\n" +
                $"- Development location: {devJulia}\n" +
                $"Ensure the Julia package is properly deployed with the component.",
                bundledJulia);
        }

        // Note: Locates RunServer.jl script in package or development folders.
        // This script starts the HTTP server and loads simulation code.
        private string GetServerScriptPath()
        {
            string packageDirectory = Environment.ExpandEnvironmentVariables(@"%appdata%\McNeel\Rhinoceros\packages\8.0\LilyPadGH\0.0.1");
            string scriptPath = Path.Combine(packageDirectory, "Julia", "RunServer.jl");

            if (File.Exists(scriptPath))
            {
                return scriptPath;
            }

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
        // Region: Server Detection & Cleanup
        // ================================
        #region Server Detection & Cleanup

        /// <summary>
        /// Checks if a port is currently in use.
        /// Used to detect existing Julia servers before starting a new one.
        /// </summary>
        private bool IsPortInUse(int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var result = client.BeginConnect("127.0.0.1", port, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(100));

                    if (success)
                    {
                        try
                        {
                            client.EndConnect(result);
                            return true;
                        }
                        catch
                        {
                            return false;
                        }
                    }
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Stops the Julia server gracefully, with fallback to force kill.
        /// Ensures all Julia processes are properly terminated.
        /// </summary>
        private void StopJuliaServer()
        {
            try
            {
                if (_isRunning)
                {
                    _isRunning = false;
                }

                // Note: Try graceful shutdown via HTTP endpoint first.
                // Gives Julia time to clean up resources properly.
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                try
                {
                    var response = client.GetAsync("http://localhost:8080/shutdown").GetAwaiter().GetResult();
                }
                catch { }

                Thread.Sleep(1000);

                // Force kill if still running after graceful shutdown attempt
                if (_juliaServerProcess != null && !_juliaServerProcess.HasExited)
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

                // Final fallback: force kill regardless of errors
                if (_juliaServerProcess != null && !_juliaServerProcess.HasExited)
                {
                    try
                    {
                        _juliaServerProcess.Kill();
                    }
                    catch { }
                }

                _isServerRunning = false;
                _juliaServerProcess = null;
                _activeDialog?.SetServerState(_isServerRunning);
                ExpireSolution(true);
            }
        }

        /// <summary>
        /// Called when a Grasshopper document is removed.
        /// Ensures Julia server cleanup when document closes.
        /// </summary>
        private void OnDocumentRemoved(GH_DocumentServer sender, GH_Document doc)
        {
            // Check if this component was in the removed document
            if (doc != null && doc.Objects.Contains(this))
            {
                CleanupResources();

                // Unhook the event to prevent memory leaks
                Instances.DocumentServer.DocumentRemoved -= OnDocumentRemoved;
            }
        }

        /// <summary>
        /// Override called when component is removed from canvas.
        /// Ensures Julia server cleanup when component is deleted.
        /// </summary>
        public override void RemovedFromDocument(GH_Document document)
        {
            CleanupResources();
            base.RemovedFromDocument(document);
        }

        /// <summary>
        /// Centralised cleanup method for all disposal scenarios.
        /// Stops Julia server and closes any open dialogues.
        /// </summary>
        private void CleanupResources()
        {
            if (_disposed)
                return;

            try
            {
                // Stop Julia server if running
                if (_isServerRunning)
                {
                    StopJuliaServer();
                }

                // Close dialogue if open
                if (_activeDialog != null && _activeDialog.Visible)
                {
                    _activeDialog.Close();
                }

                _disposed = true;
            }
            catch (Exception ex)
            {
                // Log but don't throw during cleanup
                System.Diagnostics.Debug.WriteLine($"Cleanup error: {ex.Message}");
            }
        }

        #endregion

        // ================================
        // Region: Component Metadata
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
                    var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LilyPadGH.Icons.Flow_Simulation.CFDAnalysisIcon.png");
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