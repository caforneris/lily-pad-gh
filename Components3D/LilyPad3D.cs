// ========================================
// FILE: LilyPad3D.cs
// PART 1: MAIN COMPONENT
// DESC: Main Grasshopper component for LilyPad3D.
// --- REVISIONS ---
// - 2025-09-21 @ 10:22: Fixed JSON serialization bug causing KeyError(:obstacles).
//   - Reverted to DataContractJsonSerializer to match the JSON data model.
// - 2025-09-21 @ 10:06: Updated Julia server launch to use a local project environment.
// ========================================

using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using Rhino.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization.Json; // NOTE: Changed for the correct serializer
using System.Text;
// using System.Text.Json; // NOTE: Removed incorrect serializer
using System.Threading.Tasks;
using GH_Bitmap = System.Drawing.Bitmap;

namespace LilyPadGH.Components3D
{
    public class LilyPad3DComponent : GH_Component
    {
        internal LilyPad3DSettings _settings = new LilyPad3DSettings();
        private LilyPad3DEtoDialog _activeDialog;

        // Server and state management
        internal bool _isServerRunning = false;
        private Process _juliaServerProcess = null;
        private string _customJuliaPath = null;
        private string _currentJsonData = "{}";

        public LilyPad3DComponent()
          : base("LilyPad 3D", "LilyPad3D",
              "Generates a 3D voxel mask and sends it to a Julia CFD server.",
              "LilyPadGH", "Flow Simulation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Boundary Curve", "BC", "A closed, planar curve defining the simulation boundary.", GH_ParamAccess.item);
            pManager.AddBoxParameter("Geometries", "G", "A list of boxes representing obstacles within the boundary.", GH_ParamAccess.list);
            pManager.AddTextParameter("Julia Path", "JP", "Optional custom Julia installation path.", GH_ParamAccess.item);
            pManager[1].Optional = true;
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "S", "Provides feedback on the component's state.", GH_ParamAccess.item);
            pManager.AddTextParameter("File Path", "P", "The path to the generated JSON file.", GH_ParamAccess.item);
            pManager.AddTextParameter("JSON", "J", "The generated JSON content for preview.", GH_ParamAccess.item);
            pManager.AddTextParameter("Animation Path", "A", "The path to the generated MP4 animation file.", GH_ParamAccess.item);
        }

        public override void CreateAttributes()
        {
            m_attributes = new LilyPad3DAttributes(this);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Curve boundaryCurve = null;
            var geometryBoxes = new List<Box>();
            string customJuliaPath = string.Empty;

            if (!DA.GetData(0, ref boundaryCurve)) return;
            DA.GetDataList(1, geometryBoxes);
            DA.GetData(2, ref customJuliaPath);

            if (!string.IsNullOrEmpty(customJuliaPath)) _customJuliaPath = customJuliaPath;
            if (boundaryCurve == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Boundary Curve input is required."); return; }
            if (!boundaryCurve.IsClosed || !boundaryCurve.IsPlanar()) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Boundary Curve should be a closed, planar curve.");

            var bb = boundaryCurve.GetBoundingBox(true);
            int nx = (int)_settings.Resolution.X, ny = (int)_settings.Resolution.Y, nz = (int)_settings.Resolution.Z;
            var voxelSize = new Vector3d(bb.Diagonal.X / nx, bb.Diagonal.Y / ny, bb.Diagonal.Z / nz);
            var mask = new bool[nx, ny, nz];

            Parallel.For(0, nx, i => {
                for (int j = 0; j < ny; j++) for (int k = 0; k < nz; k++)
                    {
                        var pt = bb.Min + new Vector3d((i + 0.5) * voxelSize.X, (j + 0.5) * voxelSize.Y, (k + 0.5) * voxelSize.Z);
                        foreach (var obstacle in geometryBoxes) if (obstacle.Contains(pt)) { mask[i, j, k] = true; break; }
                    }
            });

            var flatBytes = new byte[nx * ny * nz];
            int idx = 0;
            for (int k = 0; k < nz; k++) for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++) flatBytes[idx++] = mask[i, j, k] ? (byte)0x01 : (byte)0x00;
            string base64Mask = Convert.ToBase64String(flatBytes);

            var domain = new DomainSpec3D { min = new[] { bb.Min.X, bb.Min.Y, bb.Min.Z }, max = new[] { bb.Max.X, bb.Max.Y, bb.Max.Z }, resolution = new[] { nx, ny, nz }, units = "world", axis_order = "xyz" };
            var time = new TimeSpec3D { dt = _settings.Timestep, duration = _settings.Duration, warmup_steps = 20 };
            var fluid = new FluidSpec3D { nu = _settings.Viscosity, rho = 1.0, forcing = new ForcingSpec3D { type = "uniform_wind", velocity = new[] { _settings.Velocity.X, _settings.Velocity.Y, _settings.Velocity.Z } } };
            var obstacles = new ObstaclesSpec3D { type = "voxel_mask", shape = new[] { nx, ny, nz }, data = base64Mask };
            var root = new RootSpec3D { version = "1.1", domain = domain, time = time, fluid = fluid, obstacles = obstacles, notes = "Generated by LilyPad3D" };

            string animationPath = "Animation not generated.";
            if (_settings.GenerateAnimation)
            {
                string configDir = Path.GetDirectoryName(_settings.FilePath);
                animationPath = Path.Combine(configDir, _settings.AnimationFileName);
                root.animation = new AnimationSpec { generate_video = true, output_path = animationPath.Replace("\\", "/") };
            }

            // NOTE: Reverted to the original, correct ToJson method.
            _currentJsonData = ToJson(root);

            string status = _isServerRunning ? "Server Running. Apply parameters to simulate." : "Server stopped. Use dialog to start.";
            Message = _isServerRunning ? "Server Ready" : "Configured";

            DA.SetData(0, status);
            DA.SetData(1, _settings.FilePath);
            DA.SetData(2, _currentJsonData);
            DA.SetData(3, animationPath);
        }

        #region UI and Server Methods
        public void ShowConfigDialog()
        {
            if (_activeDialog != null && _activeDialog.Visible) { _activeDialog.BringToFront(); return; }
            _activeDialog = new LilyPad3DEtoDialog(_settings);
            _activeDialog.OnStartServerClicked += HandleStartServerClicked;
            _activeDialog.OnStopServerClicked += HandleStopServerClicked;
            _activeDialog.OnApplyParametersClicked += HandleApplyParametersClicked;
            _activeDialog.SetServerState(_isServerRunning);
            _activeDialog.Closed += (s, e) => { _activeDialog = null; };
            _activeDialog.Owner = RhinoEtoApp.MainWindow;
            _activeDialog.Show();
        }

        private void HandleStartServerClicked()
        {
            if (_isServerRunning) return;
            try
            {
                string juliaExePath = GetJuliaExecutablePath();
                string serverScriptPath = GetServerScriptPath();
                string serverScriptDirectory = Path.GetDirectoryName(serverScriptPath);
                var threadCount = Environment.ProcessorCount;
                _juliaServerProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = juliaExePath,
                        Arguments = $"--project=\"{serverScriptDirectory}\" --threads {threadCount} \"{serverScriptPath}\"",
                        UseShellExecute = true,
                        CreateNoWindow = false,
                        WindowStyle = ProcessWindowStyle.Normal
                    }
                };
                _juliaServerProcess.Start();
                _isServerRunning = true;
                _activeDialog?.SetServerState(true);
                ExpireSolution(true);
            }
            catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Failed to start 3D server: {ex.Message}"); }
        }

        private void HandleStopServerClicked()
        {
            if (!_isServerRunning || _juliaServerProcess == null) return;
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    var response = client.GetAsync("http://localhost:8080/shutdown").GetAwaiter().GetResult();
                }
                System.Threading.Thread.Sleep(500);
            }
            catch { /* Ignore errors */ }
            finally
            {
                if (!_juliaServerProcess.HasExited) { _juliaServerProcess.Kill(); }
                _isServerRunning = false;
                _juliaServerProcess = null;
                _activeDialog?.SetServerState(false);
                ExpireSolution(true);
            }
        }

        private void HandleApplyParametersClicked(LilyPad3DSettings newSettings)
        {
            if (!_isServerRunning) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Julia server is not running."); return; }
            _settings = newSettings;
            ExpireSolution(true);
            Task.Run(async () => {
                await Task.Delay(1000);
                try
                {
                    File.WriteAllText(_settings.FilePath, _currentJsonData);
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(300);
                    var content = new StringContent(_currentJsonData, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync("http://localhost:8080/", content);
                    RhinoApp.InvokeOnUiThread(() => {
                        if (response.IsSuccessStatusCode) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Parameters applied. 3D simulation running on server.");
                        else AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Server returned error: {response.StatusCode}");
                        ExpireSolution(true);
                    });
                }
                catch (Exception ex)
                {
                    RhinoApp.InvokeOnUiThread(() => AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Failed to apply parameters: {ex.Message}"));
                }
            });
        }
        #endregion

        #region Path and JSON Helpers
        // NOTE: Restored the original, correct JSON serializer helper method.
        private static string ToJson<T>(T obj)
        {
            var ser = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream())
            {
                ser.WriteObject(ms, obj);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private string GetJuliaExecutablePath()
        {
            if (!string.IsNullOrEmpty(_customJuliaPath))
            {
                string path = Path.Combine(_customJuliaPath, "bin", "julia.exe");
                if (File.Exists(path)) return path;
            }
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] versions = { "Julia-1.11.7", "Julia-1.11", "Julia-1.10" };
            foreach (var version in versions)
            {
                string path = Path.Combine(localAppData, "Programs", version, "bin", "julia.exe");
                if (File.Exists(path)) return path;
            }
            string ghaDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string bundledPath = Path.Combine(ghaDir, "JuliaPackage", "julia-1.11.7-win64", "bin", "julia.exe");
            if (File.Exists(bundledPath)) return bundledPath;
            throw new FileNotFoundException("julia.exe not found. Please install Julia or provide a custom path.");
        }

        private string GetServerScriptPath()
        {
            string ghaDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string scriptPath = Path.Combine(ghaDir, "Julia", "RunServer3D.jl");
            if (!File.Exists(scriptPath)) throw new FileNotFoundException("RunServer3D.jl not found in Julia folder.", scriptPath);
            return scriptPath;
        }
        #endregion

        public override Guid ComponentGuid => new Guid("7f8c9d0e-1a2b-3c4d-5e6f-7a8b9c0d1e2f");
        protected override GH_Bitmap Icon => null;
        public override GH_Exposure Exposure => GH_Exposure.primary;
    }
}