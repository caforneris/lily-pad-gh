// ========================================
// FILE: LilyPad_EtoDialog.cs (CORRECTED VERSION)
// DESC: Industry-standard CFD configuration dialogue with tabbed interface.
// --- REVISIONS ---
// - 2025-11-04 (DATA URI FIX):
//   - The `Value does not fall...` error was caused by the IE11 engine's
//     ~2MB limit for Base64 data URIs.
// - 2025-11-05 (HISTORY/CLEANUP FIX):
//   - Implemented user request for GIF history (max 10).
//   - `OnApplyAndRunButtonClick` now generates a unique timestamped name
//     for the GIF and HTML files for each run.
//   - A new `CleanupOldGifs` function is called before each run to delete
//     the oldest GIF(s) if the count exceeds 10, maintaining a rolling history.
//   - `OnClosed` now only cleans up session-specific temp files (.png, .html).
// - 2025-11-05 (NAMING CONVENTION):
//   - Updated `OnApplyAndRunButtonClick` to use the new naming convention:
//     `CFD-Simulation{time}s_YYMMDDTTTT.gif`
//   - Updated `CleanupOldGifs` to find and delete files based on this
//     new convention.
// ========================================

using Eto.Drawing;
using Eto.Forms;
using System;
using System.IO;
using System.Linq;

namespace LilyPadGH.Components
{
    public sealed class LilyPadCfdDialog : Form
    {
        // ========================
        // Region: Fields & State
        // ========================
        #region Fields & State

        private readonly LilyPadCfdSettings _settings;
        private readonly string _packageTempDir; // Store the main temp directory
        private readonly string _tempFramePath;

        // Note: These are no longer readonly, as they are updated with each run
        private string _tempGifPath;
        private string _tempHtmlPath;

        // Note: Fields for tracking GIF file stability to prevent race conditions.
        private long _lastGifSize = -1;
        private DateTime _lastFrameWriteTime = DateTime.MinValue;
        private bool _isGifDisplayed = false;

        // Note: Added fields for a more robust stability check.
        // We now wait for the file size to be stable for this duration.
        private const double GIF_STABILITY_SECONDS = 1.0;
        private DateTime _lastGifSizeTime = DateTime.MinValue;

        // UI control references
        private NumericStepper _fluidDensityStepper, _kinematicViscosityStepper, _inletVelocityStepper, _reynoldsNumberStepper;
        private Button _calculateReButton;
        private NumericStepper _domainWidthStepper, _domainHeightStepper, _gridXStepper, _gridYStepper, _characteristicLengthStepper;
        private NumericStepper _timeStepStepper, _totalTimeStepper, _cflNumberStepper;
        private Button _calculateMaxDtButton;
        private NumericStepper _maxIterationsStepper, _convergenceToleranceStepper, _relaxationFactorStepper;
        private NumericStepper _curveDivisionsStepper, _simplifyToleranceStepper, _maxPointsPerPolyStepper, _objectScaleFactorStepper;
        private NumericStepper _colorScaleMinStepper, _colorScaleMaxStepper, _animationFPSStepper;
        private CheckBox _showBodyCheckBox, _showVelocityCheckBox, _showPressureCheckBox, _showVorticityCheckBox;
        private NumericStepper _waterLilyLStepper;
        private CheckBox _useGPUCheckBox;
        private DropDown _precisionTypeDropDown;
        private WebView _simulationView;
        private UITimer _viewTimer;
        private Label _statusLabel;
        private Button _startServerButton, _stopServerButton, _applyAndRunButton, _closeButton;

        #endregion

        // ========================
        // Region: Events
        // ========================
        #region Events

        public event Action OnStartServerClicked;
        public event Action OnStopServerClicked;
        public event Action<LilyPadCfdSettings> OnApplyAndRunClicked;

        #endregion

        // ========================
        // Region: Constructor
        // ========================
        #region Constructor

        public LilyPadCfdDialog(LilyPadCfdSettings currentSettings)
        {
            _settings = currentSettings.Clone();

            // Note: Store the package temp dir as a field
            _packageTempDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "McNeel", "Rhinoceros", "packages", "8.0", "LilyPadGH", "0.0.1", "Temp"
            );

            if (!Directory.Exists(_packageTempDir))
            {
                Directory.CreateDirectory(_packageTempDir);
            }

            string processId = System.Diagnostics.Process.GetCurrentProcess().Id.ToString();
            _tempFramePath = Path.Combine(_packageTempDir, $"lilypad_sim_frame_{processId}.png");

            // Note: GIF and HTML paths are now set in the 'Run' button click
            _tempGifPath = string.Empty;
            _tempHtmlPath = string.Empty;

            BuildUI();
            LoadSettingsIntoUI();

            _viewTimer = new UITimer { Interval = 0.2 };
            _viewTimer.Elapsed += UpdateSimulationView;
            _viewTimer.Start();
        }

        #endregion

        // ========================
        // Region: Frame Update Logic
        // ========================
        #region Frame Update Logic

        private void UpdateSimulationView(object sender, EventArgs e)
        {
            // Note: Don't run if path isn't set yet, or if we're done.
            if (string.IsNullOrEmpty(_tempGifPath) || _isGifDisplayed)
            {
                return;
            }

            try
            {
                // STATE 2: CHECKING FOR THE FINAL GIF.
                if (File.Exists(_tempGifPath))
                {
                    var gifInfo = new FileInfo(_tempGifPath);

                    // Check 1: Has the file size changed since our last check?
                    if (gifInfo.Length != _lastGifSize)
                    {
                        // Yes, it's different. The file is actively being written (or just appeared).
                        // Update trackers and report progress.
                        _lastGifSize = gifInfo.Length;
                        _lastGifSizeTime = DateTime.Now; // Reset the stability timer
                        Application.Instance.Invoke(() =>
                        {
                            _statusLabel.Text = $"⏳ Finalising Animation ({gifInfo.Length / 1024} KB)...";
                            _statusLabel.TextColor = Colors.Orange;
                        });
                    }
                    // Check 2: Size is stable. Has it been stable for long enough?
                    else if (gifInfo.Length > 0 && (DateTime.Now - _lastGifSizeTime).TotalSeconds > GIF_STABILITY_SECONDS)
                    {
                        // Yes, size is non-zero and hasn't changed for 1.0s. It's safe to *try* to read.
                        Application.Instance.Invoke(() =>
                        {
                            try
                            {
                                // --- WEBVIEW SECURITY FIX ---
                                // 1. Get the relative file name of the GIF
                                string relativeGifName = gifInfo.Name;

                                // 2. Create the HTML content with a relative path
                                string html = $@"
                                    <html><head><style>body{{margin:0;padding:10px;background:#fff;display:flex;justify-content:center;align-items:center;height:100vh;overflow:hidden;box-sizing:border-box;}} ::-webkit-scrollbar{{display:none;}} html{{scrollbar-width:none;-ms-overflow-style:none;}} img{{width:100%;height:auto;max-height:100%;object-fit:contain;}}</style></head>
                                    <body><img src='{relativeGifName}' alt='Simulation Animation' /></body></html>";

                                // 3. Write this HTML to its own temp file (path was set in 'Run' click)
                                File.WriteAllText(_tempHtmlPath, html);

                                // 4. Point the WebView's Url to the new HTML file
                                _simulationView.Url = new Uri(_tempHtmlPath);
                                // --- END FIX ---

                                // Set final status and lock the UI state by setting the flag.
                                _statusLabel.Text = $"✅ Displaying: {gifInfo.Name} ({gifInfo.Length / 1024}KB)";
                                _statusLabel.TextColor = Colors.Green;
                                _isGifDisplayed = true;
                            }
                            catch (Exception ex)
                            {
                                // This will catch any remaining errors
                                _lastGifSizeTime = DateTime.Now;
                                _statusLabel.Text = $"❌ GIF Load Error (retrying): {ex.Message}";
                                _statusLabel.TextColor = Colors.Red;
                            }
                        });
                    }
                    // Check 3: Size is stable, but we're still inside the 1.0s waiting window.
                    else if (gifInfo.Length > 0)
                    {
                        // Do nothing. Just wait for the stability window to pass.
                    }

                    return; // Do not proceed to other states if we're dealing with the final GIF.
                }

                // STATE 3: CHECKING FOR LIVE PNG FRAMES (SIMULATION IS RUNNING).
                if (File.Exists(_tempFramePath))
                {
                    var frameInfo = new FileInfo(_tempFramePath);

                    // Use a 5-second tolerance to account for pauses in frame writes.
                    if ((DateTime.Now - frameInfo.LastWriteTime).TotalSeconds < 5)
                    {
                        Application.Instance.Invoke(() =>
                        {
                            // Update image only if it's a new frame.
                            if (frameInfo.LastWriteTime > _lastFrameWriteTime)
                            {
                                try
                                {
                                    // Use shared read for live frames, as we *expect* them
                                    // to be in use. This is fine for PNGs.
                                    byte[] pngBytes = ReadAllBytesShared(_tempFramePath);
                                    if (pngBytes != null && pngBytes.Length > 0)
                                    {
                                        string base64Png = Convert.ToBase64String(pngBytes);
                                        string html = $@"
                                            <html><head><style>body{{margin:0;padding:10px;background:#fff;display:flex;justify-content:center;align-items:center;height:100vh;overflow:hidden;box-sizing:border-box;}} ::-webkit-scrollbar{{display:none;}} html{{scrollbar-width:none;-ms-overflow-style:none;}} img{{width:100%;height:auto;max-height:100%;object-fit:contain;}}</style></head>
                                            <body><img src='data:image/png;base64,{base64Png}' alt='Live Frame' /></body></html>";
                                        _simulationView.LoadHtml(html);
                                        _lastFrameWriteTime = frameInfo.LastWriteTime;
                                    }
                                }
                                catch { }
                            }
                            // Always update the status text to show activity.
                            _statusLabel.Text = $"🔄 Simulation Running - Frame updated at {DateTime.Now:HH:mm:ss}";
                            _statusLabel.TextColor = Colors.Blue;
                        });
                    }
                    else // Frame exists but is old -> Simulation is likely finished and generating the GIF.
                    {
                        Application.Instance.Invoke(() => {
                            _statusLabel.Text = "⏳ Generating Animation...";
                            _statusLabel.TextColor = Colors.Orange;
                        });
                    }
                    return; // Don't fall through to idle state.
                }

                // STATE 4: IDLE / WAITING STATE.
                // This is the default state, only reached if no temp files are found.
                Application.Instance.Invoke(() =>
                {
                    // Reset the WebView to the placeholder
                    _simulationView.LoadHtml(@"
                        <html><head><style>body{margin:0;padding:20px;background:#fff;display:flex;justify-content:center;align-items:center;height:100vh;font-family:Arial,sans-serif;overflow:hidden;}::-webkit-scrollbar{display:none;}html{scrollbar-width:none;-ms-overflow-style:none;}.placeholder{text-align:center;color:#888;}</style></head>
                        <body><div class='placeholder'><h2>Simulation View</h2><p>Waiting for Simulation</p></div></body></html>");

                    if (DateTime.Now.Second % 2 == 0)
                    {
                        _statusLabel.Text = "⏳ Waiting for Simulation";
                        _statusLabel.TextColor = Colors.Gray;
                    }
                });
            }
            catch (IOException) { } // Safely ignore transient IO errors.
            catch (Exception ex)
            {
                Application.Instance.Invoke(() =>
                {
                    _statusLabel.Text = $"⚠️ Error: {ex.Message}";
                    _statusLabel.TextColor = Colors.Red;
                });
            }
        }

        // This function is still needed for the live PNG frames, which
        // we *want* to read permissively.
        private byte[] ReadAllBytesShared(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var ms = new MemoryStream())
                {
                    fs.CopyTo(ms);
                    return ms.ToArray();
                }
            }
            catch (IOException)
            {
                return null;
            }
        }

        #endregion

        // ========================
        // Region: Server State Management
        // ========================
        #region Server State Management

        public void SetServerState(bool isServerRunning)
        {
            Application.Instance.Invoke(() =>
            {
                _startServerButton.Enabled = !isServerRunning;
                _stopServerButton.Enabled = isServerRunning;
                _applyAndRunButton.Enabled = isServerRunning;
                _startServerButton.Text = isServerRunning ? "Server Running" : "Start Server";
                _startServerButton.BackgroundColor = isServerRunning ? Colors.LightGreen : SystemColors.Control;
                _applyAndRunButton.Text = isServerRunning ? "Apply Settings & Run" : "Start Server First";
                _applyAndRunButton.BackgroundColor = isServerRunning ? Colors.SteelBlue : Colors.Gray;
                _statusLabel.Text = isServerRunning ? "✅ Julia Server Ready" : "⚠️ Server Stopped";
                _statusLabel.TextColor = isServerRunning ? Colors.Blue : Colors.Gray;

                if (!isServerRunning)
                {
                    try { if (File.Exists(_tempFramePath)) File.Delete(_tempFramePath); } catch { }
                }
            });
        }

        #endregion

        // ========================
        // Region: UI Construction
        // ========================
        #region UI Construction

        private void BuildUI()
        {
            Title = "LilyPad CFD - Comprehensive Simulation Control";
            MinimumSize = new Size(1200, 800);
            Resizable = true;
            InitializeControls();
            _simulationView = new WebView { Size = new Size(500, 400) };
            _simulationView.LoadHtml(@"
                <html><head><style>body{margin:0;padding:20px;background:#fff;display:flex;justify-content:center;align-items:center;height:100vh;font-family:Arial,sans-serif;overflow:hidden;}::-webkit-scrollbar{display:none;}html{scrollbar-width:none;-ms-overflow-style:none;}.placeholder{text-align:center;color:#888;}</style></head>
                <body><div class='placeholder'><h2>Simulation View</h2><p>Waiting for Simulation</p></div></body></html>");
            var mainLayout = new DynamicLayout { Padding = new Padding(10) };
            var contentSplitter = new Splitter
            {
                Panel1 = CreateSettingsTabs(),
                Panel2 = CreateSimulationPanel(),
                Position = 320,
                Orientation = Orientation.Horizontal
            };
            mainLayout.Add(contentSplitter, yscale: true);
            mainLayout.Add(CreateButtonPanel(), yscale: false);
            Content = mainLayout;
        }

        private void InitializeControls()
        {
            #region Control Initialization
            _fluidDensityStepper = new NumericStepper { MinValue = 0.1, DecimalPlaces = 2, Width = 100 };
            _kinematicViscosityStepper = new NumericStepper { MinValue = 1e-9, MaxValue = 1e-3, DecimalPlaces = 9, Increment = 1e-7, Width = 100 };
            _inletVelocityStepper = new NumericStepper { MinValue = 0.01, DecimalPlaces = 3, Width = 100 };
            _reynoldsNumberStepper = new NumericStepper { MinValue = 1, DecimalPlaces = 1, Width = 100 };
            _calculateReButton = new Button { Text = "Calculate Re", Width = 100 };
            _calculateReButton.Click += (s, e) => _reynoldsNumberStepper.Value = (_inletVelocityStepper.Value * _characteristicLengthStepper.Value) / _kinematicViscosityStepper.Value;
            _domainWidthStepper = new NumericStepper { MinValue = 0.1, DecimalPlaces = 2, Width = 100 };
            _domainHeightStepper = new NumericStepper { MinValue = 0.1, DecimalPlaces = 2, Width = 100 };
            _gridXStepper = new NumericStepper { MinValue = 10, Increment = 8, Width = 100 };
            _gridYStepper = new NumericStepper { MinValue = 10, Increment = 8, Width = 100 };
            _characteristicLengthStepper = new NumericStepper { MinValue = 0.001, DecimalPlaces = 4, Width = 100 };
            _timeStepStepper = new NumericStepper { MinValue = 0.0001, DecimalPlaces = 5, Width = 100 };
            _totalTimeStepper = new NumericStepper { MinValue = 0.1, MaxValue = 1000, DecimalPlaces = 1, Width = 100 };
            _cflNumberStepper = new NumericStepper { MinValue = 0.1, MaxValue = 1.0, DecimalPlaces = 2, Width = 100 };
            _calculateMaxDtButton = new Button { Text = "Calculate Max Δt", Width = 120 };
            _calculateMaxDtButton.Click += (s, e) => _timeStepStepper.Value = Math.Round(_cflNumberStepper.Value * (_domainWidthStepper.Value / _gridXStepper.Value) / _inletVelocityStepper.Value, 5);
            _maxIterationsStepper = new NumericStepper { MinValue = 10, Increment = 10, Width = 100 };
            _convergenceToleranceStepper = new NumericStepper { MinValue = 1e-10, MaxValue = 1e-2, DecimalPlaces = 10, Increment = 1e-6, Width = 100 };
            _relaxationFactorStepper = new NumericStepper { MinValue = 0.1, MaxValue = 1.0, DecimalPlaces = 2, Width = 100 };
            _curveDivisionsStepper = new NumericStepper { MinValue = 2, Increment = 1, Width = 100 };
            _simplifyToleranceStepper = new NumericStepper { MinValue = 0.0, MaxValue = 1.0, DecimalPlaces = 3, Increment = 0.01, Width = 100 };
            _maxPointsPerPolyStepper = new NumericStepper { MinValue = 5, MaxValue = 5000, Increment = 10, Width = 100 };
            _objectScaleFactorStepper = new NumericStepper { MinValue = 0.05, MaxValue = 0.95, DecimalPlaces = 2, Increment = 0.05, Width = 100 };
            _colorScaleMinStepper = new NumericStepper { MinValue = -100, MaxValue = 0, DecimalPlaces = 1, Width = 100 };
            _colorScaleMaxStepper = new NumericStepper { MinValue = 0, MaxValue = 100, DecimalPlaces = 1, Width = 100 };
            _animationFPSStepper = new NumericStepper { MinValue = 5, MaxValue = 60, Increment = 5, Width = 100 };
            _showBodyCheckBox = new CheckBox { Text = "Show Body" };
            _showVelocityCheckBox = new CheckBox { Text = "Show Velocity Vectors" };
            _showPressureCheckBox = new CheckBox { Text = "Show Pressure" };
            _showVorticityCheckBox = new CheckBox { Text = "Show Vorticity" };
            _waterLilyLStepper = new NumericStepper { MinValue = 16, MaxValue = 128, Increment = 8, Width = 100 };
            _useGPUCheckBox = new CheckBox { Text = "Use GPU (CUDA)" };
            _precisionTypeDropDown = new DropDown { Width = 100, Items = { "Float32", "Float64" }, SelectedIndex = 0 };
            _statusLabel = new Label { Text = "Ready to start server", Font = new Font(SystemFont.Default, 10), TextColor = Colors.Gray, TextAlignment = TextAlignment.Center };
            _startServerButton = new Button { Text = "Start Server", Width = 120 };
            _stopServerButton = new Button { Text = "Stop Server", Width = 120, Enabled = false };
            _applyAndRunButton = new Button { Text = "Start Server First", Width = 150, Enabled = false, BackgroundColor = Colors.Gray };
            _closeButton = new Button { Text = "Close", Width = 80 };
            _startServerButton.Click += (s, e) => OnStartServerClicked?.Invoke();
            _stopServerButton.Click += (s, e) => OnStopServerClicked?.Invoke();
            _applyAndRunButton.Click += OnApplyAndRunButtonClick;
            _closeButton.Click += (s, e) => Close();
            #endregion
        }

        private Control CreateSettingsTabs()
        {
            var tabs = new TabControl();
            #region Tab: Flow & Domain
            var tab1 = new TabPage { Text = "Flow & Domain" };
            var layout1 = new DynamicLayout { DefaultSpacing = new Size(5, 5), Padding = new Padding(10) };
            layout1.Add(new Label { Text = "Fluid Properties", Font = new Font(SystemFont.Bold) });
            layout1.AddRow(new Label { Text = "Density (kg/m³):" }, _fluidDensityStepper);
            layout1.AddRow(new Label { Text = "Kinematic Viscosity (m²/s):" }, _kinematicViscosityStepper);
            layout1.AddRow(new Label { Text = "Inlet Velocity (m/s):" }, _inletVelocityStepper);
            layout1.AddRow(new Label { Text = "Reynolds Number:" }, _reynoldsNumberStepper);
            layout1.AddRow(null, _calculateReButton);
            layout1.AddSpace();
            layout1.Add(new Label { Text = "Domain Configuration", Font = new Font(SystemFont.Bold) });
            layout1.AddRow(new Label { Text = "Width (m):" }, _domainWidthStepper);
            layout1.AddRow(new Label { Text = "Height (m):" }, _domainHeightStepper);
            layout1.AddRow(new Label { Text = "Grid Cells X:" }, _gridXStepper);
            layout1.AddRow(new Label { Text = "Grid Cells Y:" }, _gridYStepper);
            layout1.AddRow(new Label { Text = "Characteristic Length (m):" }, _characteristicLengthStepper);
            layout1.AddSpace();
            layout1.Add(new Label { Text = "Time Stepping", Font = new Font(SystemFont.Bold) });
            layout1.AddRow(new Label { Text = "Time Step (s):" }, _timeStepStepper);
            layout1.AddRow(new Label { Text = "Total Simulation Time (s):" }, _totalTimeStepper);
            layout1.AddRow(new Label { Text = "CFL Number:" }, _cflNumberStepper);
            layout1.AddRow(null, _calculateMaxDtButton);
            layout1.AddSpace();
            layout1.Add(new Label { Text = "Solver Settings", Font = new Font(SystemFont.Bold) });
            layout1.AddRow(new Label { Text = "Max Iterations:" }, _maxIterationsStepper);
            layout1.AddRow(new Label { Text = "Convergence Tolerance:" }, _convergenceToleranceStepper);
            layout1.AddRow(new Label { Text = "Relaxation Factor:" }, _relaxationFactorStepper);
            layout1.Add(null, yscale: true);
            tab1.Content = new Scrollable { Content = layout1, Border = BorderType.None };
            tabs.Pages.Add(tab1);
            #endregion
            #region Tab: Geometry & Viz
            var tab2 = new TabPage { Text = "Geometry & Viz" };
            var layout2 = new DynamicLayout { DefaultSpacing = new Size(5, 5), Padding = new Padding(10) };
            layout2.Add(new Label { Text = "Geometry Settings", Font = new Font(SystemFont.Bold) });
            layout2.AddRow(new Label { Text = "Curve Divisions:" }, _curveDivisionsStepper);
            layout2.AddRow(new Label { Text = "Simplify Tolerance:" }, _simplifyToleranceStepper);
            layout2.AddRow(new Label { Text = "Max Points/Polyline:" }, _maxPointsPerPolyStepper);
            layout2.AddRow(new Label { Text = "Object Scale (0-1):" }, _objectScaleFactorStepper);
            layout2.AddSpace();
            layout2.Add(new Label { Text = "Visualization Settings", Font = new Font(SystemFont.Bold) });
            layout2.AddRow(new Label { Text = "Color Min:" }, _colorScaleMinStepper);
            layout2.AddRow(new Label { Text = "Color Max:" }, _colorScaleMaxStepper);
            layout2.AddRow(new Label { Text = "Animation FPS:" }, _animationFPSStepper);
            layout2.Add(_showBodyCheckBox);
            layout2.Add(_showVelocityCheckBox);
            layout2.Add(_showPressureCheckBox);
            layout2.Add(_showVorticityCheckBox);
            layout2.AddSpace();
            layout2.Add(new Label { Text = "Advanced Settings", Font = new Font(SystemFont.Bold) });
            layout2.AddRow(new Label { Text = "WaterLily L:" }, _waterLilyLStepper);
            layout2.AddRow(new Label { Text = "Precision:" }, _precisionTypeDropDown);
            layout2.Add(_useGPUCheckBox);
            layout2.Add(null, yscale: true);
            tab2.Content = new Scrollable { Content = layout2, Border = BorderType.None };
            tabs.Pages.Add(tab2);
            #endregion
            return tabs;
        }

        private Control CreateSimulationPanel()
        {
            var layout = new DynamicLayout { Padding = new Padding(10) };
            layout.Add(_simulationView, yscale: true);
            layout.Add(_statusLabel, yscale: false);
            return layout;
        }

        private Control CreateButtonPanel()
        {
            return new TableLayout { Spacing = new Size(10, 5), Rows = { new TableRow(_startServerButton, _stopServerButton, new TableCell(null, true), _applyAndRunButton, _closeButton) } };
        }

        #endregion

        // ========================
        // Region: Settings Management
        // ========================
        #region Settings Management

        private void LoadSettingsIntoUI()
        {
            _fluidDensityStepper.Value = _settings.FluidDensity;
            _kinematicViscosityStepper.Value = _settings.KinematicViscosity;
            _inletVelocityStepper.Value = _settings.InletVelocity;
            _reynoldsNumberStepper.Value = _settings.ReynoldsNumber;
            _domainWidthStepper.Value = _settings.DomainWidth;
            _domainHeightStepper.Value = _settings.DomainHeight;
            _gridXStepper.Value = _settings.GridResolutionX;
            _gridYStepper.Value = _settings.GridResolutionY;
            _characteristicLengthStepper.Value = _settings.CharacteristicLength;
            _timeStepStepper.Value = _settings.TimeStep;
            _totalTimeStepper.Value = _settings.TotalSimulationTime;
            _cflNumberStepper.Value = _settings.CFLNumber;
            _maxIterationsStepper.Value = _settings.MaxIterations;
            _convergenceToleranceStepper.Value = _settings.ConvergenceTolerance;
            _relaxationFactorStepper.Value = _settings.RelaxationFactor;
            _curveDivisionsStepper.Value = _settings.CurveDivisions;
            _simplifyToleranceStepper.Value = _settings.SimplifyTolerance;
            _maxPointsPerPolyStepper.Value = _settings.MaxPointsPerPoly;
            _objectScaleFactorStepper.Value = _settings.ObjectScaleFactor;
            _colorScaleMinStepper.Value = _settings.ColorScaleMin;
            _colorScaleMaxStepper.Value = _settings.ColorScaleMax;
            _animationFPSStepper.Value = _settings.AnimationFPS;
            _showBodyCheckBox.Checked = _settings.ShowBody;
            _showVelocityCheckBox.Checked = _settings.ShowVelocityVectors;
            _showPressureCheckBox.Checked = _settings.ShowPressure;
            _showVorticityCheckBox.Checked = _settings.ShowVorticity;
            _waterLilyLStepper.Value = _settings.WaterLilyL;
            _useGPUCheckBox.Checked = _settings.UseGPU;
            _precisionTypeDropDown.SelectedIndex = _settings.PrecisionType == "Float64" ? 1 : 0;
            SetServerState(false);
        }

        private void UpdateSettingsFromUI()
        {
            _settings.FluidDensity = _fluidDensityStepper.Value;
            _settings.KinematicViscosity = _kinematicViscosityStepper.Value;
            _settings.InletVelocity = _inletVelocityStepper.Value;
            _settings.ReynoldsNumber = _reynoldsNumberStepper.Value;
            _settings.DomainWidth = _domainWidthStepper.Value;
            _settings.DomainHeight = _domainHeightStepper.Value;
            _settings.GridResolutionX = (int)_gridXStepper.Value;
            _settings.GridResolutionY = (int)_gridYStepper.Value;
            _settings.CharacteristicLength = _characteristicLengthStepper.Value;
            _settings.TimeStep = _timeStepStepper.Value;
            _settings.TotalSimulationTime = _totalTimeStepper.Value;
            _settings.CFLNumber = _cflNumberStepper.Value;
            _settings.MaxIterations = (int)_maxIterationsStepper.Value;
            _settings.ConvergenceTolerance = _convergenceToleranceStepper.Value;
            _settings.RelaxationFactor = _relaxationFactorStepper.Value;
            _settings.CurveDivisions = (int)_curveDivisionsStepper.Value;
            _settings.SimplifyTolerance = _simplifyToleranceStepper.Value;
            _settings.MaxPointsPerPoly = (int)_maxPointsPerPolyStepper.Value;
            _settings.ObjectScaleFactor = _objectScaleFactorStepper.Value;
            _settings.ColorScaleMin = _colorScaleMinStepper.Value;
            _settings.ColorScaleMax = _colorScaleMaxStepper.Value;
            _settings.AnimationFPS = (int)_animationFPSStepper.Value;
            _settings.ShowBody = _showBodyCheckBox.Checked ?? true;
            _settings.ShowVelocityVectors = _showVelocityCheckBox.Checked ?? false;
            _settings.ShowPressure = _showPressureCheckBox.Checked ?? false;
            _settings.ShowVorticity = _showVorticityCheckBox.Checked ?? true;
            _settings.WaterLilyL = (int)_waterLilyLStepper.Value;
            _settings.UseGPU = _useGPUCheckBox.Checked ?? false;
            _settings.PrecisionType = _precisionTypeDropDown.SelectedIndex == 1 ? "Float64" : "Float32";
        }

        #endregion

        // ========================
        // Region: Event Handlers
        // ========================
        #region Event Handlers

        private void OnApplyAndRunButtonClick(object sender, EventArgs e)
        {
            // --- NEW NAMING CONVENTION LOGIC ---
            // 1. Run cleanup logic *before* creating new paths
            CleanupOldGifs();

            // 2. Update settings from UI *first* to get the simulation time
            UpdateSettingsFromUI();

            // 3. Generate new, unique paths based on user convention
            //    e.g., CFD-Simulation10s_2511051547
            string simTime = _settings.TotalSimulationTime.ToString("F0"); // "F0" = 0 decimal places
            string date = DateTime.Now.ToString("yyMMdd");
            string time = DateTime.Now.ToString("HHmm");
            string timestamp = date + time; // YYMMDDTTTT

            string baseName = $"CFD-Simulation{simTime}s_{timestamp}";
            string viewName = $"lilypad_sim_view_{timestamp}"; // Matching HTML file

            _tempGifPath = Path.Combine(_packageTempDir, $"{baseName}.gif");
            _tempHtmlPath = Path.Combine(_packageTempDir, $"{viewName}.html");
            // --- END NEW LOGIC ---

            // Note: Reset state flags and UI for the new simulation run.
            _isGifDisplayed = false;
            _lastGifSize = -1;
            _lastFrameWriteTime = DateTime.MinValue;
            _lastGifSizeTime = DateTime.MinValue;

            _simulationView.LoadHtml(@"
                <html><head><style>body{margin:0;padding:20px;background:#fff;display:flex;justify-content:center;align-items:center;height:100vh;font-family:Arial,sans-serif;overflow:hidden;}::-webkit-scrollbar{display:none;}html{scrollbar-width:none;-ms-overflow-style:none;}.placeholder{text-align:center;color:#888;}</style></head>
                <body><div class='placeholder'><h2>Simulation View</h2><p>Starting Simulation...</p></div></body></html>");

            // Pass new paths to settings object for Julia
            _settings.UIFramePath = _tempFramePath;
            _settings.UIGifPath = _tempGifPath;

            int totalCells = _settings.GridResolutionX * _settings.GridResolutionY;
            string timeEstimate = totalCells < 10000 ? "< 1 min" : totalCells < 50000 ? "1-5 min" : totalCells < 100000 ? "5-15 min" : "> 15 min";

            OnApplyAndRunClicked?.Invoke(_settings);

            _statusLabel.Text = $"🚀 Simulation Starting... Estimated time: {timeEstimate}";
            _statusLabel.TextColor = Colors.Blue;
        }

        // --- NEW FUNCTION ---
        /// <summary>
        /// Scans the temp directory and deletes the oldest GIFs if the count exceeds 10.
        /// </summary>
        private void CleanupOldGifs()
        {
            const int MAX_GIF_COUNT = 10;
            try
            {
                var dirInfo = new DirectoryInfo(_packageTempDir);

                // Find all simulation GIFs using the new convention
                var gifs = dirInfo.GetFiles("CFD-Simulation*.gif") // Use new wildcard
                                 .OrderBy(f => f.CreationTime)
                                 .ToList();

                // Check if we're at or over the limit
                if (gifs.Count >= MAX_GIF_COUNT)
                {
                    // Calculate how many to delete (e.g., if 10 exist, delete 1 to make room)
                    int deleteCount = gifs.Count - MAX_GIF_COUNT + 1;
                    var oldGifs = gifs.Take(deleteCount);

                    foreach (var oldGif in oldGifs)
                    {
                        try
                        {
                            // Delete the GIF
                            oldGif.Delete();

                            // Also delete its matching HTML file
                            // e.g., "CFD-Simulation10s_2511051547.gif"
                            // extract "2511051547"
                            string gifName = oldGif.Name;
                            int underscoreIndex = gifName.LastIndexOf('_');
                            if (underscoreIndex != -1 && gifName.Length > underscoreIndex + 1)
                            {
                                // e.g., "2511051547"
                                string timestamp = gifName.Substring(underscoreIndex + 1).Replace(".gif", "");
                                // e.g., "lilypad_sim_view_2511051547.html"
                                string htmlName = $"lilypad_sim_view_{timestamp}.html";
                                string htmlPath = Path.Combine(_packageTempDir, htmlName);

                                if (File.Exists(htmlPath))
                                {
                                    File.Delete(htmlPath);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // Log, but don't crash the app
                            System.Diagnostics.Debug.WriteLine($"Failed to delete old file: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to run GIF cleanup: {ex.Message}");
            }
        }

        #endregion

        // ========================
        // Region: Cleanup
        // ========================
        #region Cleanup

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _viewTimer?.Stop();
            if (_viewTimer != null)
            {
                _viewTimer.Elapsed -= UpdateSimulationView;
                _viewTimer = null;
            }

            // Clean up session-specific files
            try { if (File.Exists(_tempFramePath)) File.Delete(_tempFramePath); } catch { }

            // Clean up all HTML wrappers (GIFs will remain, per user request)
            try
            {
                var dirInfo = new DirectoryInfo(_packageTempDir);
                var htmlFiles = dirInfo.GetFiles("lilypad_sim_view_*.html"); // This wildcard works
                foreach (var htmlFile in htmlFiles)
                {
                    htmlFile.Delete();
                }
            }
            catch { }
        }
        #endregion

        // ========================
        // Region: Public Properties
        // ========================
        #region Public Properties
        public string UIFramePath => _tempFramePath;
        public string UIGifPath => _tempGifPath;
        #endregion
    }
}