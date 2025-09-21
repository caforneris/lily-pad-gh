// ========================================
// FILE: LilyPad_EtoDialog.cs
// PART 3: ETO CONFIGURATION DIALOG
// DESC: Creates and manages the pop-up settings window using Eto.Forms.
//       Updated to include all simulation parameters. It is only responsible
//       for presenting and collecting data, not for analysis.
// ========================================

using Eto.Drawing;
using Eto.Forms;
using System;

namespace LilyPadGH.Components
{
    // NOTE: Inherits from Form instead of Dialog to make the window non-modal.
    public sealed class LilyPadCfdDialog : Form
    {
        private readonly LilyPadCfdSettings _settings;

        // UI control references
        private NumericStepper _reynoldsStepper;
        private NumericStepper _velocityStepper;
        private NumericStepper _gridXStepper;
        private NumericStepper _gridYStepper;
        private NumericStepper _durationStepper;
        private NumericStepper _curveDivisionsStepper;

        // Julia-specific controls
        private NumericStepper _simulationLStepper;
        private NumericStepper _simulationUStepper;
        private NumericStepper _animationDurationStepper;
        private CheckBox _plotBodyCheckBox;
        private NumericStepper _simplifyToleranceStepper;
        private NumericStepper _maxPointsPerPolyStepper;

        private Button _runStopButton;
        private Button _startServerButton;
        private Button _stopServerButton;
        private Button _applyParametersButton;

        // Events for starting/stopping the analysis
        public event Action<LilyPadCfdSettings> OnRunClicked;
        public event Action OnStopClicked;

        // Events for server control and parameter application
        public event Action OnStartServerClicked;
        public event Action OnStopServerClicked;
        public event Action<LilyPadCfdSettings> OnApplyParametersClicked;

        public LilyPadCfdDialog(LilyPadCfdSettings currentSettings)
        {
            // NOTE: Clone settings to prevent direct mutation of component state.
            _settings = currentSettings.Clone();
            BuildUI();
            LoadSettingsIntoUI();
        }

        public void SetRunningState(bool isRunning)
        {
            bool controlsEnabled = !isRunning;
            _reynoldsStepper.Enabled = controlsEnabled;
            _velocityStepper.Enabled = controlsEnabled;
            _gridXStepper.Enabled = controlsEnabled;
            _gridYStepper.Enabled = controlsEnabled;
            _durationStepper.Enabled = controlsEnabled;
            _curveDivisionsStepper.Enabled = controlsEnabled;

            // Julia-specific controls
            _simulationLStepper.Enabled = controlsEnabled;
            _simulationUStepper.Enabled = controlsEnabled;
            _animationDurationStepper.Enabled = controlsEnabled;
            _plotBodyCheckBox.Enabled = controlsEnabled;
            _simplifyToleranceStepper.Enabled = controlsEnabled;
            _maxPointsPerPolyStepper.Enabled = controlsEnabled;

            _runStopButton.Text = isRunning ? "Stop Analysis" : "Apply & Run";
        }

        public void SetServerState(bool isServerRunning)
        {
            _startServerButton.Enabled = !isServerRunning;
            _stopServerButton.Enabled = isServerRunning;
            _applyParametersButton.Enabled = isServerRunning;

            _startServerButton.Text = isServerRunning ? "Server Running" : "Start Julia Server";
            _stopServerButton.Text = "Stop Julia Server";
            _applyParametersButton.Text = isServerRunning ? "Apply Parameters & Run Simulation" : "Start Server First";
            _applyParametersButton.BackgroundColor = isServerRunning ? Color.FromArgb(70, 130, 180) : Color.FromArgb(128, 128, 128);
        }

        private void BuildUI()
        {
            Title = "CFD Simulation Control";
            MinimumSize = new Size(450, 600);
            Resizable = true;

            // --- Control Initialisation ---
            _reynoldsStepper = new NumericStepper { MinValue = 1, DecimalPlaces = 1 };
            _velocityStepper = new NumericStepper { MinValue = 0, DecimalPlaces = 2 };
            _gridXStepper = new NumericStepper { MinValue = 10, Increment = 8 };
            _gridYStepper = new NumericStepper { MinValue = 10, Increment = 8 };
            _durationStepper = new NumericStepper { MinValue = 0.1, DecimalPlaces = 2, Increment = 1.0 };
            _curveDivisionsStepper = new NumericStepper { MinValue = 2, Increment = 1 };

            // Julia-specific controls
            _simulationLStepper = new NumericStepper { MinValue = 16, MaxValue = 128, Increment = 8, Value = 32 };
            _simulationUStepper = new NumericStepper { MinValue = 0.1, DecimalPlaces = 2, Increment = 0.1, Value = 1.0 };
            _animationDurationStepper = new NumericStepper { MinValue = 0.1, DecimalPlaces = 1, Increment = 0.5, Value = 1.0 };
            _plotBodyCheckBox = new CheckBox { Text = "Show Body in Animation", Checked = true };
            _simplifyToleranceStepper = new NumericStepper { MinValue = 0.0, MaxValue = 0.5, DecimalPlaces = 3, Increment = 0.001, Value = 0.0 };
            _maxPointsPerPolyStepper = new NumericStepper { MinValue = 5, MaxValue = 1000, Increment = 10, Value = 1000 };

            _runStopButton = new Button();
            _runStopButton.Click += OnRunStopButtonClick;

            // Server control buttons
            _startServerButton = new Button { Text = "Start Julia Server" };
            _startServerButton.Click += (s, e) => OnStartServerClicked?.Invoke();

            _stopServerButton = new Button { Text = "Stop Julia Server", Enabled = false };
            _stopServerButton.Click += (s, e) => OnStopServerClicked?.Invoke();

            _applyParametersButton = new Button { Text = "Start Server First", Enabled = false, BackgroundColor = Color.FromArgb(128, 128, 128) };
            _applyParametersButton.Click += OnApplyParametersButtonClick;

            var closeButton = new Button { Text = "Close" };
            closeButton.Click += (s, e) => Close();

            // --- Layout ---
            var layout = new DynamicLayout { DefaultSpacing = new Size(5, 5), Padding = new Padding(10) };

            layout.AddRow(new Label { Text = "Reynolds Number:" }, null, _reynoldsStepper);
            layout.AddRow(new Label { Text = "Flow Velocity (m/s):" }, null, _velocityStepper);
            layout.AddRow(new Label { Text = "Grid Resolution X:" }, null, _gridXStepper);
            layout.AddRow(new Label { Text = "Grid Resolution Y:" }, null, _gridYStepper);
            layout.AddRow(new Label { Text = "Simulation Duration (s):" }, null, _durationStepper);
            layout.AddRow(new Label { Text = "Curve Divisions:" }, null, _curveDivisionsStepper);

            // Julia simulation parameters section
            layout.AddRow(new Label { Text = "Julia Simulation Parameters:", Font = new Font(SystemFont.Bold) });
            layout.AddRow(new Label { Text = "Simulation Scale (L):" }, null, _simulationLStepper);
            layout.AddRow(new Label { Text = "Flow Scale (U):" }, null, _simulationUStepper);
            layout.AddRow(new Label { Text = "Animation Duration (s):" }, null, _animationDurationStepper);
            layout.AddRow(_plotBodyCheckBox);

            // Julia point reduction section
            layout.AddRow(new Label { Text = "Point Reduction Settings:", Font = new Font(SystemFont.Bold) });
            layout.AddRow(new Label { Text = "Simplify Tolerance:" }, null, _simplifyToleranceStepper);
            layout.AddRow(new Label { Text = "Max Points Per Polyline:" }, null, _maxPointsPerPolyStepper);

            layout.Add(null, yscale: true); // Flexible spacer

            // Server control section
            layout.AddRow(new Label { Text = "Julia Server Control:", Font = new Font(SystemFont.Bold) });
            var serverButtonLayout = new TableLayout
            {
                Spacing = new Size(5, 5),
                Rows = { new TableRow(_startServerButton, _stopServerButton) }
            };
            layout.Add(serverButtonLayout);

            // Apply parameters section
            layout.AddRow(_applyParametersButton);

            // Main action buttons
            var buttonLayout = new TableLayout
            {
                Spacing = new Size(5, 5),
                Rows = { new TableRow(new TableCell(null, true), _runStopButton, closeButton) }
            };
            layout.Add(buttonLayout);

            Content = layout;
        }

        private void LoadSettingsIntoUI()
        {
            _reynoldsStepper.Value = _settings.ReynoldsNumber;
            _velocityStepper.Value = _settings.Velocity;
            _gridXStepper.Value = _settings.GridResolutionX;
            _gridYStepper.Value = _settings.GridResolutionY;
            _durationStepper.Value = _settings.Duration;
            _curveDivisionsStepper.Value = _settings.CurveDivisions;

            // Load Julia-specific settings
            _simulationLStepper.Value = _settings.SimulationL;
            _simulationUStepper.Value = _settings.SimulationU;
            _animationDurationStepper.Value = _settings.AnimationDuration;
            _plotBodyCheckBox.Checked = _settings.PlotBody;
            _simplifyToleranceStepper.Value = _settings.SimplifyTolerance;
            _maxPointsPerPolyStepper.Value = _settings.MaxPointsPerPoly;

            SetRunningState(false); // Initial state is always 'not running'
            SetServerState(false); // Initial server state is not running
        }

        private void UpdateSettingsFromUI()
        {
            _settings.ReynoldsNumber = _reynoldsStepper.Value;
            _settings.Velocity = _velocityStepper.Value;
            _settings.GridResolutionX = (int)_gridXStepper.Value;
            _settings.GridResolutionY = (int)_gridYStepper.Value;
            _settings.Duration = _durationStepper.Value;
            _settings.CurveDivisions = (int)_curveDivisionsStepper.Value;

            // Update Julia-specific settings
            _settings.SimulationL = (int)_simulationLStepper.Value;
            _settings.SimulationU = _simulationUStepper.Value;
            _settings.AnimationDuration = _animationDurationStepper.Value;
            _settings.PlotBody = _plotBodyCheckBox.Checked ?? true;
            _settings.SimplifyTolerance = _simplifyToleranceStepper.Value;
            _settings.MaxPointsPerPoly = (int)_maxPointsPerPolyStepper.Value;
        }

        private void OnRunStopButtonClick(object sender, EventArgs e)
        {
            if (_runStopButton.Text == "Apply & Run")
            {
                UpdateSettingsFromUI();
                OnRunClicked?.Invoke(_settings);
            }
            else
            {
                OnStopClicked?.Invoke();
            }
        }

        private void OnApplyParametersButtonClick(object sender, EventArgs e)
        {
            UpdateSettingsFromUI();
            OnApplyParametersClicked?.Invoke(_settings);
        }
    }
}