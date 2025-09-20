// ========================================
// PART 4: MAIN COMPONENT
// ========================================
// Core Grasshopper component class

using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using GH_Bitmap = System.Drawing.Bitmap;

namespace LilyPadGH.Components
{
    public class LilyPadCfdComponent : GH_Component
    {
        internal bool _isRunning = false;
        internal string _status = "Ready to configure";
        internal LilyPadCfdSettings _settings = new LilyPadCfdSettings();

        // NOTE: Task management fields for handling the simulation process.
        private CancellationTokenSource _cancellationTokenSource;
        private LilyPadCfdDialog _activeDialog;

        public LilyPadCfdComponent() : base(
            "LilyPad CFD Analysis",
            "CFD",
            "Computational Fluid Dynamics analysis with external solver",
            "LilyPadGH",
            "Analysis")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "G", "Building/obstacle geometry for CFD analysis", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "S", "Current analysis status", GH_ParamAccess.item);
            // pManager.AddPointParameter("Sample Points", "Pts", "Velocity sample point locations", GH_ParamAccess.list);
            // pManager.AddVectorParameter("Velocity Vectors", "V", "Velocity vectors at sample points", GH_ParamAccess.list);
            // pManager.AddNumberParameter("Pressure", "P", "Pressure values at sample points", GH_ParamAccess.list);
            // pManager.AddNumberParameter("Temperature", "T", "Temperature values if thermal analysis enabled", GH_ParamAccess.list);
            // pManager.AddMeshParameter("Result Meshes", "M", "Colour-mapped result visualisation meshes", GH_ParamAccess.list);
        }

        public override void CreateAttributes()
        {
            m_attributes = new LilyPadCfdAttributes(this);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var geometry = new List<GeometryBase>();
            if (!DA.GetDataList(0, geometry)) return;

            DA.SetData(0, _status);

            Message = _isRunning ? "Running..." : _status;
        }

        // Public method called by the custom attributes to show the Eto dialog
        public void ShowConfigDialog()
        {
            // If dialog is already open, bring to ront
            if (_activeDialog != null && _activeDialog.Visible)
            {
                _activeDialog.BringToFront();
                return;
            }

            _activeDialog = new LilyPadCfdDialog(_settings);
            _activeDialog.OnRunClicked += HandleRunClicked;
            _activeDialog.OnStopClicked += HandleStopClicked;

            // NOTE: Ensure the component's running state is reflected when the dialog is first shown.
            _activeDialog.SetRunningState(_isRunning);

            // Unsubscribe from events when the dialog closes to prevent memory leaks
            _activeDialog.Closed += (s, e) => {
                _activeDialog.OnRunClicked -= HandleRunClicked;
                _activeDialog.OnStopClicked -= HandleStopClicked;
                _activeDialog = null; // Clear the reference
            };

            // NOTE: Use .Show() for a non-modal window that can remain open.
            _activeDialog.Show();
        }

        private void HandleRunClicked(LilyPadCfdSettings newSettings)
        {
            if (_isRunning) return;

            _settings = newSettings; // Update settings from the dialog
            _cancellationTokenSource = new CancellationTokenSource();

            // NOTE: Fire-and-forget async task. UI to remains responsive while the simulation runs.
            Task.Run(() => RunSimulationAsync(_cancellationTokenSource.Token));
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

                // Simulate work
                int steps = (int)(_settings.Duration / _settings.Timestep);
                for (int i = 0; i <= steps; i++)
                {
                    // Check for cancellation at the start of each step
                    token.ThrowIfCancellationRequested();

                    // Simulate a calculation step
                    await Task.Delay(100, token); // 100ms delay per step

                    double progress = (double)i / steps;
                    UpdateComponentState($"Running... {progress:P0}", true);
                }

                UpdateComponentState("Simulation completed.", false);
            }
            catch (OperationCanceledException)
            {
                // Gracefully handle cancellation
                UpdateComponentState("Simulation stopped by user.", false);
            }
            catch (Exception ex)
            {
                // Handle other potential errors
                UpdateComponentState($"Error: {ex.Message}", false);
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                _isRunning = false; // Ensure running flag is always reset
                UpdateComponentState(_status, false); // Final UI state refresh
            }
        }

        // Helper to update component state and trigger a redraw from any thread
        private void UpdateComponentState(string status, bool isRunning)
        {
            _status = status;
            _isRunning = isRunning;

            // NOTE: UI updates must be marshalled back to Rhino's main thread.
            RhinoApp.InvokeOnUiThread(() =>
            {
                _activeDialog?.SetRunningState(isRunning);
                OnDisplayExpired(true); // Redraw the component on the canvas
            });
        }

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

