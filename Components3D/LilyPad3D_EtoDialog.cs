// ========================================
// FILE: LilyPad3D_EtoDialog.cs
// PART 3: ETO CONFIGURATION DIALOG
// DESC: Creates and manages the Eto.Forms settings window for the LilyPad3D component.
// --- REVISIONS ---
// - 2025-09-21 @ 10:27: Fixed UI layout alignment issues using TableLayout.
// - 2025-09-21 @ 10:19: Overhauled UI layout to match 2D component's robust design.
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
        private TextBox _filePathBox, _animationFileNameBox;
        private CheckBox _generateAnimationCheck;
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
            _applyParametersButton.BackgroundColor = isServerRunning ? Colors.SteelBlue : Colors.Gray;
        }

        private void BuildUI()
        {
            Title = "LilyPad3D Server Control";
            MinimumSize = new Size(450, 550);
            Resizable = true;

            _resX = new NumericStepper { MinValue = 2, Increment = 8 }; _resY = new NumericStepper { MinValue = 2, Increment = 8 }; _resZ = new NumericStepper { MinValue = 2, Increment = 8 };
            _dt = new NumericStepper { DecimalPlaces = 4, Increment = 0.01, MinValue = 0.0001 };
            _duration = new NumericStepper { DecimalPlaces = 2, Increment = 1.0, MinValue = 0.1 };
            _nu = new NumericStepper { DecimalPlaces = 5, Increment = 0.0001, MinValue = 0 };
            _velX = new NumericStepper { DecimalPlaces = 3, Increment = 0.1 }; _velY = new NumericStepper { DecimalPlaces = 3, Increment = 0.1 }; _velZ = new NumericStepper { DecimalPlaces = 3, Increment = 0.1 };
            _filePathBox = new TextBox();
            _generateAnimationCheck = new CheckBox { Text = "Generate MP4 Animation" };
            _animationFileNameBox = new TextBox();
            _generateAnimationCheck.CheckedChanged += (s, e) => { _animationFileNameBox.Enabled = _generateAnimationCheck.Checked ?? false; };

            var browseButton = new Button { Text = "Browse..." }; browseButton.Click += OnBrowseButtonClick;
            _startServerButton = new Button(); _startServerButton.Click += (s, e) => OnStartServerClicked?.Invoke();
            _stopServerButton = new Button { Text = "Stop 3D Server" }; _stopServerButton.Click += (s, e) => OnStopServerClicked?.Invoke();
            _applyParametersButton = new Button(); _applyParametersButton.Click += OnApplyParametersButtonClick;
            var closeButton = new Button { Text = "Close" }; closeButton.Click += (s, e) => Close();

            var boldFont = SystemFonts.Bold(SystemFonts.Default().Size);

            // --- Use TableLayouts for precise alignment ---
            var resolutionTable = new TableLayout { Spacing = new Size(5, 5) };
            resolutionTable.Rows.Add(new TableRow(
                new Label { Text = "Resolution X:" }, _resX,
                new Label { Text = "Y:" }, _resY,
                new Label { Text = "Z:" }, _resZ
            ));

            var propertiesTable = new TableLayout { Spacing = new Size(5, 5) };
            propertiesTable.Rows.Add(new TableRow(new Label { Text = "Viz Timestep:", VerticalAlignment = VerticalAlignment.Center }, new TableCell(_dt, true)));
            propertiesTable.Rows.Add(new TableRow(new Label { Text = "Duration:", VerticalAlignment = VerticalAlignment.Center }, new TableCell(_duration, true)));
            propertiesTable.Rows.Add(new TableRow(new Label { Text = "Viscosity (nu):", VerticalAlignment = VerticalAlignment.Center }, new TableCell(_nu, true)));
            propertiesTable.Rows.Add(new TableRow(
                new Label { Text = "Velocity X:" }, _velX,
                new Label { Text = "Y:" }, _velY,
                new Label { Text = "Z:" }, _velZ
            ));

            var outputTable = new TableLayout { Spacing = new Size(5, 5) };
            outputTable.Rows.Add(new TableRow(new Label { Text = "Config File:" }, new TableCell(_filePathBox, true), browseButton));
            outputTable.Rows.Add(new TableRow(_generateAnimationCheck, null, null));
            outputTable.Rows.Add(new TableRow(new Label { Text = "Animation Filename:" }, new TableCell(_animationFileNameBox, true), null));

            // --- Main Layout ---
            var layout = new DynamicLayout { DefaultSpacing = new Size(5, 5), Padding = new Padding(10) };
            layout.AddRow(new Label { Text = "Domain & Resolution", Font = boldFont });
            layout.Add(resolutionTable);
            layout.Add(null);

            layout.AddRow(new Label { Text = "Time & Fluid Properties", Font = boldFont });
            layout.Add(propertiesTable);
            layout.Add(null);

            layout.AddRow(new Label { Text = "Output Configuration", Font = boldFont });
            layout.Add(outputTable);
            layout.Add(null, yscale: true);

            layout.AddRow(new Label { Text = "Julia Server Control", Font = boldFont });
            layout.Add(new TableLayout { Spacing = new Size(5, 5), Rows = { new TableRow(_startServerButton, _stopServerButton) } });
            layout.AddRow(_applyParametersButton);
            layout.Add(null);

            layout.AddRow(null, closeButton);
            Content = layout;
        }

        private void LoadSettingsIntoUI()
        {
            _resX.Value = _settings.Resolution.X; _resY.Value = _settings.Resolution.Y; _resZ.Value = _settings.Resolution.Z;
            _dt.Value = _settings.Timestep;
            _duration.Value = _settings.Duration;
            _nu.Value = _settings.Viscosity;
            _velX.Value = _settings.Velocity.X; _velY.Value = _settings.Velocity.Y; _velZ.Value = _settings.Velocity.Z;
            _filePathBox.Text = _settings.FilePath;
            _generateAnimationCheck.Checked = _settings.GenerateAnimation;
            _animationFileNameBox.Text = _settings.AnimationFileName;
            _animationFileNameBox.Enabled = _settings.GenerateAnimation;
        }

        private void UpdateSettingsFromUI()
        {
            _settings.Resolution = new Vector3d(_resX.Value, _resY.Value, _resZ.Value);
            _settings.Timestep = _dt.Value;
            _settings.Duration = _duration.Value;
            _settings.Viscosity = _nu.Value;
            _settings.Velocity = new Vector3d(_velX.Value, _velY.Value, _velZ.Value);
            _settings.FilePath = _filePathBox.Text;
            _settings.GenerateAnimation = _generateAnimationCheck.Checked ?? false;
            _settings.AnimationFileName = _animationFileNameBox.Text;
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