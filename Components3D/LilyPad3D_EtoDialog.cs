// ========================================
// FILE: LilyPad3D_EtoDialog.cs
// PART 3: ETO CONFIGURATION DIALOG
// DESC: Creates and manages the Eto.Forms settings window for the LilyPad3D component.
// --- REVISIONS ---
// - 2025-09-21 @ 09:05: Redesigned UI for server control architecture.
//   - Added Start/Stop Server and Apply Parameters buttons.
// ========================================

using Eto.Drawing;
using Eto.Forms;
using Rhino.Geometry;
using System;

namespace LilyPadGH.Components3D
{
    public class LilyPad3DEtoDialog : Form
    {
        private readonly LilyPad3DSettings _settings;
        public event Action OnStartServerClicked;
        public event Action OnStopServerClicked;
        public event Action<LilyPad3DSettings> OnApplyParametersClicked;

        private NumericStepper _resX, _resY, _resZ, _dt, _duration, _nu, _velX, _velY, _velZ;
        private TextBox _filePathBox;
        private Button _startServerButton, _stopServerButton, _applyParametersButton;

        public LilyPad3DEtoDialog(LilyPad3DSettings currentSettings)
        {
            _settings = currentSettings.Clone();
            BuildUI();
            LoadSettingsIntoUI();
        }

        public void SetServerState(bool isServerRunning)
        {
            _startServerButton.Enabled = !isServerRunning;
            _stopServerButton.Enabled = isServerRunning;
            _applyParametersButton.Enabled = isServerRunning;
            _startServerButton.Text = isServerRunning ? "Server is Running" : "Start 3D Server";
            _applyParametersButton.Text = isServerRunning ? "Apply & Run Simulation" : "Start Server First";
        }

        private void BuildUI()
        {
            Title = "LilyPad3D Server Control";
            MinimumSize = new Size(450, 450);

            _resX = new NumericStepper { MinValue = 2, Increment = 8 }; _resY = new NumericStepper { MinValue = 2, Increment = 8 }; _resZ = new NumericStepper { MinValue = 2, Increment = 8 };
            _dt = new NumericStepper { DecimalPlaces = 4, Increment = 0.01, MinValue = 0.0001 };
            _duration = new NumericStepper { DecimalPlaces = 2, Increment = 1.0, MinValue = 0.1 };
            _nu = new NumericStepper { DecimalPlaces = 5, Increment = 0.0001, MinValue = 0 };
            _velX = new NumericStepper { DecimalPlaces = 3, Increment = 0.1 }; _velY = new NumericStepper { DecimalPlaces = 3, Increment = 0.1 }; _velZ = new NumericStepper { DecimalPlaces = 3, Increment = 0.1 };
            _filePathBox = new TextBox();

            var browseButton = new Button { Text = "Browse..." };
            browseButton.Click += OnBrowseButtonClick;

            _startServerButton = new Button(); _startServerButton.Click += (s, e) => OnStartServerClicked?.Invoke();
            _stopServerButton = new Button { Text = "Stop 3D Server" }; _stopServerButton.Click += (s, e) => OnStopServerClicked?.Invoke();
            _applyParametersButton = new Button(); _applyParametersButton.Click += OnApplyParametersButtonClick;
            var closeButton = new Button { Text = "Close" }; closeButton.Click += (s, e) => Close();

            var mainLayout = new DynamicLayout { Padding = new Padding(10), Spacing = new Size(5, 5) };

            mainLayout.Add(new GroupBox { Text = "Domain Resolution", Content = new DynamicLayout(new DynamicRow(new Label { Text = "X:" }, _resX, new Label { Text = "Y:" }, _resY, new Label { Text = "Z:" }, _resZ)) });
            mainLayout.Add(new GroupBox { Text = "Time", Content = new DynamicLayout(new DynamicRow(new Label { Text = "Viz Timestep:" }, _dt), new DynamicRow(new Label { Text = "Duration:" }, _duration)) });
            mainLayout.Add(new GroupBox { Text = "Fluid Properties", Content = new DynamicLayout(new DynamicRow(new Label { Text = "Viscosity:" }, _nu), new DynamicRow(new Label { Text = "Vel X:" }, _velX, new Label { Text = "Y:" }, _velY, new Label { Text = "Z:" }, _velZ)) });
            mainLayout.Add(new GroupBox { Text = "Output", Content = new DynamicLayout(new DynamicRow(new Label { Text = "Config File:" }, _filePathBox, browseButton)) });

            mainLayout.Add(null, yscale: true);
            mainLayout.Add(new GroupBox { Text = "Server Control", Content = new DynamicLayout(new DynamicRow(_startServerButton, _stopServerButton), new DynamicRow(_applyParametersButton)) });
            mainLayout.AddRow(null, closeButton);
            Content = mainLayout;
        }

        private void LoadSettingsIntoUI()
        {
            _resX.Value = _settings.Resolution.X; _resY.Value = _settings.Resolution.Y; _resZ.Value = _settings.Resolution.Z;
            _dt.Value = _settings.Timestep;
            _duration.Value = _settings.Duration;
            _nu.Value = _settings.Viscosity;
            _velX.Value = _settings.Velocity.X; _velY.Value = _settings.Velocity.Y; _velZ.Value = _settings.Velocity.Z;
            _filePathBox.Text = _settings.FilePath;
        }

        private void UpdateSettingsFromUI()
        {
            _settings.Resolution = new Vector3d(_resX.Value, _resY.Value, _resZ.Value);
            _settings.Timestep = _dt.Value;
            _settings.Duration = _duration.Value;
            _settings.Viscosity = _nu.Value;
            _settings.Velocity = new Vector3d(_velX.Value, _velY.Value, _velZ.Value);
            _settings.FilePath = _filePathBox.Text;
        }

        private void OnApplyParametersButtonClick(object sender, EventArgs e)
        {
            UpdateSettingsFromUI();
            OnApplyParametersClicked?.Invoke(_settings);
        }

        private void OnBrowseButtonClick(object sender, EventArgs e)
        {
            var dialog = new SaveFileDialog { Filters = { new FileFilter("JSON Files", ".json") } };
            if (dialog.ShowDialog(this) == DialogResult.Ok) _filePathBox.Text = dialog.FileName;
        }
    }
}