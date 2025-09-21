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
        private Button _runStopButton;
        private Button _startServerButton;
        private Button _stopServerButton;
        private Button _pushDataButton;

        // Events for starting/stopping the analysis
        public event Action<LilyPadCfdSettings> OnRunClicked;
        public event Action OnStopClicked;

        // Events for server control and data pushing
        public event Action OnStartServerClicked;
        public event Action OnStopServerClicked;
        public event Action OnPushDataClicked;

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

            _runStopButton.Text = isRunning ? "Stop Analysis" : "Apply & Run";
        }

        public void SetServerState(bool isServerRunning)
        {
            _startServerButton.Enabled = !isServerRunning;
            _stopServerButton.Enabled = isServerRunning;
            _pushDataButton.Enabled = isServerRunning;

            _startServerButton.Text = isServerRunning ? "Server Running" : "Start Julia Server";
            _stopServerButton.Text = "Stop Julia Server";
            _pushDataButton.Text = isServerRunning ? "Push Data to Server" : "Server Not Running";
        }

        private void BuildUI()
        {
            Title = "CFD Simulation Control";
            MinimumSize = new Size(400, 300);
            Resizable = true;

            // --- Control Initialisation ---
            _reynoldsStepper = new NumericStepper { MinValue = 1, DecimalPlaces = 1 };
            _velocityStepper = new NumericStepper { MinValue = 0, DecimalPlaces = 2 };
            _gridXStepper = new NumericStepper { MinValue = 10, Increment = 8 };
            _gridYStepper = new NumericStepper { MinValue = 10, Increment = 8 };
            _durationStepper = new NumericStepper { MinValue = 0.1, DecimalPlaces = 2, Increment = 1.0 };
            _curveDivisionsStepper = new NumericStepper { MinValue = 2, Increment = 1 };

            _runStopButton = new Button();
            _runStopButton.Click += OnRunStopButtonClick;

            // Server control buttons
            _startServerButton = new Button { Text = "Start Julia Server" };
            _startServerButton.Click += (s, e) => OnStartServerClicked?.Invoke();

            _stopServerButton = new Button { Text = "Stop Julia Server", Enabled = false };
            _stopServerButton.Click += (s, e) => OnStopServerClicked?.Invoke();

            _pushDataButton = new Button { Text = "Server Not Running", Enabled = false };
            _pushDataButton.Click += (s, e) => OnPushDataClicked?.Invoke();

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
            layout.Add(null, yscale: true); // Flexible spacer

            // Server control section
            layout.AddRow(new Label { Text = "Julia Server Control:", Font = new Font(SystemFont.Bold) });
            var serverButtonLayout = new TableLayout
            {
                Spacing = new Size(5, 5),
                Rows = { new TableRow(_startServerButton, _stopServerButton) }
            };
            layout.Add(serverButtonLayout);

            // Data push section
            layout.AddRow(_pushDataButton);

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
    }
}