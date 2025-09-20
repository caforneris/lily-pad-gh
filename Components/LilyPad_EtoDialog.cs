// ========================================
// PART 3: ETO CONFIGURATION DIALOG
// ========================================
// Creates and manages the pop-up settings window using the Eto.Forms framework.
// It is only responsible for presenting and collecting data, not for analysis.

using Eto.Drawing;
using Eto.Forms;
using System;

namespace LilyPadGH.Components
{
    // NOTE: Inherits from Form instead of Dialog to make the window non-modal,
    // allowing it to stay open during the simulation.
    public sealed class LilyPadCfdDialog : Form
    {
        private readonly LilyPadCfdSettings _settings;
        private NumericStepper _durationStepper;
        private NumericStepper _timestepStepper;
        private Button _runStopButton;

        // Decoupled events for starting and stopping the analysis
        public event Action<LilyPadCfdSettings> OnRunClicked;
        public event Action OnStopClicked;

        public LilyPadCfdDialog(LilyPadCfdSettings currentSettings)
        {
            _settings = currentSettings.Clone();
            BuildUI();
            LoadSettingsIntoUI();
        }

        // Public method to allow the main component to update the button's state
        public void SetRunningState(bool isRunning)
        {
            if (isRunning)
            {
                _runStopButton.Text = "Stop Analysis";
                _durationStepper.Enabled = false;
                _timestepStepper.Enabled = false;
            }
            else
            {
                _runStopButton.Text = "Apply & Run";
                _durationStepper.Enabled = true;
                _timestepStepper.Enabled = true;
            }
        }

        private void BuildUI()
        {
            Title = "CFD Simulation Control";
            MinimumSize = new Size(350, 220);
            Resizable = true;

            _durationStepper = new NumericStepper { MinValue = 0.1, DecimalPlaces = 2, Increment = 1.0 };
            _timestepStepper = new NumericStepper { MinValue = 0.001, DecimalPlaces = 3, Increment = 0.01 };

            _runStopButton = new Button();
            var closeButton = new Button { Text = "Close" };
            closeButton.Click += (s, e) => Close();

            _runStopButton.Click += OnRunStopButtonClick;

            var layout = new DynamicLayout { DefaultSpacing = new Size(5, 5), Padding = new Padding(10) };

            layout.AddRow(new Label { Text = "Simulation Duration (s):" }, null, _durationStepper);
            layout.AddRow(new Label { Text = "Timestep - Δt (s):" }, null, _timestepStepper);
            layout.Add(null, yscale: true); // Spacer

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
            _durationStepper.Value = _settings.Duration;
            _timestepStepper.Value = _settings.Timestep;
            SetRunningState(false); // Initial state is always not running
        }

        private void OnRunStopButtonClick(object sender, EventArgs e)
        {
            // The button's text is used to determine which event to fire.
            if (_runStopButton.Text == "Apply & Run")
            {
                // Update the settings object from the UI before firing the event
                _settings.Duration = _durationStepper.Value;
                _settings.Timestep = _timestepStepper.Value;
                OnRunClicked?.Invoke(_settings);
            }
            else
            {
                OnStopClicked?.Invoke();
            }
        }
    }
}

