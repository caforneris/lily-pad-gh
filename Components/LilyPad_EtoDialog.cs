// ========================================
// FILE: LilyPad_EtoDialog.cs
// DESC: CFD configuration dialogue with tabbed interface.
// ========================================

using Eto.Drawing;
using Eto.Forms;
using System;
using System.IO;
using System.Linq;

// Note: Resolves CS0104 ambiguity between Eto.Forms.Button and
// System.Windows.Forms.VisualStyles.VisualStyleElement.Button
// by explicitly aliasing 'Button' to the Eto.Forms version.
using Button = Eto.Forms.Button;

// Note: Removed the faulty 'using Separator = ...' alias which caused CS0234.
// That control does not exist for layouts.

namespace LilyPadGH.Components
{
    public sealed class LilyPadCfdDialog : Form
    {
        // =======================
        // Part 1 — Fields & State
        // =======================
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

        // --- REVISION: Button Consolidation ---
        // Note: Merged _startServerButton and _stopServerButton into a single toggle button.
        private Button _serverToggleButton;
        // --- END REVISION ---

        private Button _applyAndRunButton, _closeButton;

        #endregion

        // ==============
        // Part 2 — Events
        // ==============
        #region Events

        public event Action OnStartServerClicked;
        public event Action OnStopServerClicked;
        public event Action<LilyPadCfdSettings> OnApplyAndRunClicked;

        #endregion

        // ===================
        // Part 3 — Constructor
        // ===================
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

        // =============================
        // Part 4 — Frame Update Logic
        // =============================
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
                        _lastGifSizeTime = DateTime.Now;
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
                                    <!DOCTYPE html>
                                    <html>
                                    <head>
                                        <style>
                                            body {{
                                                margin: 0;
                                                padding: 0;
                                                background: #fff;
                                                display: flex;
                                                justify-content: center;
                                                align-items: center;
                                                height: 100vh;
                                                overflow: hidden;
                                                box-sizing: border-box;
                                            }}
                                            ::-webkit-scrollbar {{ display: none; }}
                                            html {{
                                                scrollbar-width: none;
                                                -ms-overflow-style: none;
                                            }}
                                            img {{
                                                width: 100%;
                                                height: auto;
                                                max-height: 100%;
                                                object-fit: contain;
                                            }}
                                        </style>
                                    </head>
                                    <body>
                                        <img src='{relativeGifName}' alt='CFD Animation' />
                                    </body>
                                    </html>";

                                // 3. Write the HTML to the temp path
                                File.WriteAllText(_tempHtmlPath, html);

                                // 4. Navigate to the HTML file (which lives in the same directory as the GIF)
                                _simulationView.Url = new Uri(_tempHtmlPath);

                                _isGifDisplayed = true;
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Failed to display GIF: {ex.Message}");
                            }
                        });
                    }
                    // If we get here, the file exists but the size hasn't been stable for long enough yet.
                    return;
                }

                // STATE 1: CHECKING FOR A LIVE FRAME.
                if (File.Exists(_tempFramePath))
                {
                    var frameInfo = new FileInfo(_tempFramePath);
                    double frameAge = (DateTime.Now - frameInfo.LastWriteTime).TotalSeconds;

                    // If the frame is fresh (< 5s old), the simulation is actively running.
                    if (frameAge < 5.0)
                    {
                        Application.Instance.Invoke(() =>
                        {
                            // Only reload the frame if it's been modified since our last check
                            if (frameInfo.LastWriteTime != _lastFrameWriteTime)
                            {
                                try
                                {
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
                        });
                    }
                    else
                    {
                        // Frame exists but is old - simulation is generating GIF
                    }
                    return; // Don't fall through to idle state.
                }

                // STATE 4: IDLE / WAITING STATE.
                // This is the default state, only reached if no temp files are found.
                Application.Instance.Invoke(() =>
                {
                    _simulationView.LoadHtml(@"
                        <html><head><style>body{margin:0;padding:20px;background:#fff;display:flex;justify-content:center;align-items:center;height:100vh;font-family:Arial,sans-serif;overflow:hidden;}::-webkit-scrollbar{display:none;}html{scrollbar-width:none;-ms-overflow-style:none;}.placeholder{text-align:center;color:#888;}</style></head>
                        <body><div class='placeholder'><h2>Simulation View</h2><p>Waiting for Simulation</p></div></body></html>");
                });
            }
            catch (IOException) { }
            catch (Exception ex)
            {
                // Log errors without updating UI
                System.Diagnostics.Debug.WriteLine($"UpdateSimulationView Error: {ex.Message}");
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

        // ====================================
        // Part 5 — Server State Management
        // ====================================
        #region Server State Management

        public void SetServerState(bool isServerRunning)
        {
            Application.Instance.Invoke(() =>
            {
                // --- REVISION: Single Toggle Button Logic ---
                // Note: This logic now controls the single _serverToggleButton
                // instead of two separate buttons.
                _serverToggleButton.Enabled = true; // Re-enable after "Starting..."/"Stopping..."
                _serverToggleButton.Text = isServerRunning ? "Stop Server" : "Start Server";
                _serverToggleButton.BackgroundColor = isServerRunning ? Colors.LightSalmon : SystemColors.Control;
                // --- END REVISION ---

                _applyAndRunButton.Enabled = isServerRunning;
                _applyAndRunButton.Text = isServerRunning ? "Apply Settings && Run" : "Start Server First";
                _applyAndRunButton.BackgroundColor = isServerRunning ? Colors.SteelBlue : Colors.Gray;

                string statusText = isServerRunning ? "✅ Julia Server Ready" : "Server Not Running";
                string statusColor = isServerRunning ? "#4CAF50" : "#999";

                _simulationView.LoadHtml($@"
                    <html><head><style>body{{margin:0;padding:20px;background:#fff;display:flex;justify-content:center;align-items:center;height:100vh;font-family:Arial,sans-serif;overflow:hidden;}}::-webkit-scrollbar{{display:none;}}html{{scrollbar-width:none;-ms-overflow-style:none;}}.placeholder{{text-align:center;color:#888;}}</style></head>
                    <body><div class='placeholder'><h2>Simulation View</h2><p>Waiting for Simulation</p><p style='color:{statusColor};font-size:0.9em;'>{statusText}</p></div></body></html>");

                if (!isServerRunning)
                {
                    try { if (File.Exists(_tempFramePath)) File.Delete(_tempFramePath); } catch { }
                }
            });
        }

        #endregion

        // ==========================
        // Part 6 — UI Construction
        // ==========================
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
                <body><div class='placeholder'><h2>Simulation View</h2><p>Waiting for Simulation</p><p style='color:#999;font-size:0.9em;'>Server Not Running</p></div></body></html>");

            // Note: This layout controls the *outer* padding of the window
            // (Left, Top, Right, Bottom)
            var mainLayout = new DynamicLayout { Padding = new Padding(16, 10, 10, 16) };

            // --- REVISION ---
            // Note: Added the horizontal line back.
            // This is the correct way to add a separator in Eto.
            mainLayout.Add(new Panel { BackgroundColor = Colors.Gainsboro, Height = 1 }, yscale: false);
            // --- END REVISION ---

            // 1. Create the TabControl first
            var settingsTabs = CreateSettingsTabs();

            // 2. Create a wrapper DynamicLayout to *hold* the tabs
            var settingsPanelWrapper = new DynamicLayout
            {
                // 3. Set the independent padding for the settings panel here.
                //    (Left, Top, Right, Bottom).
                Padding = new Padding(0, 0, 0, 10)
            };

            // 4. Add the tabs to the wrapper and make them scale
            settingsPanelWrapper.Add(settingsTabs, yscale: true);


            var contentSplitter = new Splitter
            {
                // 5. Assign the new *wrapper* to Panel1
                Panel1 = settingsPanelWrapper,
                Panel2 = CreateSimulationPanel(),
                Position = 320,
                Orientation = Orientation.Horizontal,
                FixedPanel = SplitterFixedPanel.Panel1
            };

            mainLayout.Add(contentSplitter, yscale: true);
            mainLayout.Add(CreateButtonPanel(), yscale: false);

            Content = mainLayout;
        }

        private void InitializeControls()
        {
            // ===========================================
            // Part 6.1 — Control Initialisation
            // ===========================================
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

            // --- REVISION: Button Consolidation ---
            _serverToggleButton = new Button { Text = "Start Server", Width = 150 };
            // Note: _startServerButton and _stopServerButton are removed.
            // --- END REVISION ---

            _applyAndRunButton = new Button { Text = "Start Server First", Width = 150, Enabled = false, BackgroundColor = Colors.Gray };
            _closeButton = new Button { Text = "Close", Width = 150 };

            // --- REVISION: New Toggle Click Handler ---
            // Note: We now point to a single handler that checks the
            // button's state before firing the correct event.
            _serverToggleButton.Click += OnServerToggleClick;
            // --- END REVISION ---

            _applyAndRunButton.Click += OnApplyAndRunButtonClick;
            _closeButton.Click += (s, e) => Close();
            #endregion
        }

        private Control CreateSettingsTabs()
        {
            // ===========================================
            // Part 6.2 — Settings Tabs Panel
            // ===========================================
            var tabs = new TabControl();

            #region Tab: Flow & Domain
            var tab1 = new TabPage { Text = "Flow & Domain" };
            var layout1 = new DynamicLayout { DefaultSpacing = new Size(5, 5), Padding = new Padding(10, 10, 10, 10) };

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
            var layout2 = new DynamicLayout { DefaultSpacing = new Size(5, 5), Padding = new Padding(10, 10, 10, 10) };

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
            var content = new DynamicLayout { Padding = new Padding(10, 0, 0, 10) };
            content.Add(_simulationView, yscale: true);

            // TabControl has ~28px tab header, add matching top space
            var container = new DynamicLayout { Padding = new Padding(0, 23, 10, 0) };
            container.Add(content, yscale: true);

            return container;
        }

        private Control CreateButtonPanel()
        {
            // All buttons on the right, with 10px padding from right edge to match simulation view
            var buttonLayout = new TableLayout
            {
                Spacing = new Size(10, 5),
                Rows = {
                    new TableRow(
                        new TableCell(null, true), // Elastic spacer pushes all buttons right
                        // --- REVISION: Updated Button Layout ---
                        _serverToggleButton,
                        // Note: _stopServerButton removed from layout
                        // --- END REVISION ---
                        _applyAndRunButton,
                        _closeButton
                    )
                },
                Padding = new Padding(0, 0, 10, 0) // Right padding of 10 to match simulation view
            };

            return buttonLayout;
        }

        #endregion

        // ==============================
        // Part 7 — Settings Management
        // ==============================
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
        // Part 8 — Event Handlers
        // ========================
        #region Event Handlers

        // --- REVISION: New Click Handler for Toggle Button ---
        /// <summary>
        /// Handles clicks for the single server toggle button.
        /// Checks the button's current text to determine whether to
        /// start or stop the server, then fires the appropriate event.
        /// </summary>
        private void OnServerToggleClick(object sender, EventArgs e)
        {
            // Note: We check the text to determine the current state
            // and desired action.
            if (_serverToggleButton.Text == "Start Server")
            {
                // Temporarily disable to prevent double-clicks
                _serverToggleButton.Enabled = false;
                _serverToggleButton.Text = "Starting...";
                OnStartServerClicked?.Invoke();
            }
            else if (_serverToggleButton.Text == "Stop Server")
            {
                // Temporarily disable to prevent double-clicks
                _serverToggleButton.Enabled = false;
                _serverToggleButton.Text = "Stopping...";
                OnStopServerClicked?.Invoke();
            }
        }
        // --- END REVISION ---

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
        }

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

        // ================
        // Part 9 — Cleanup
        // ================
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

        // =============================
        // Part 10 — Public Properties
        // =============================
        #region Public Properties
        public string UIFramePath => _tempFramePath;
        public string UIGifPath => _tempGifPath;
        #endregion
    }
}