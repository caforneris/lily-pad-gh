// ========================================
// FILE: LilyPad_EtoDialog.cs
// PART 3: ETO CONFIGURATION DIALOG WITH REAL-TIME DISPLAY
// DESC: Creates and manages the pop-up settings window with real-time simulation view.
//       Reorganised layout with simulation view on the right side.
// --- REVISIONS ---
// - 2025-09-22: Polished real-time simulation view with proper frame handling.
//   - Cleaned up button structure to four logical buttons.
//   - Enhanced frame reading with better error handling and file locking.
//   - Added proper temp file path communication to Julia server.
//   - Improved visual feedback and status updates.
// - 2025-10-10: UI reorganisation for better workflow.
//   - Moved simulation view to right side of window (vertical split).
//   - Renamed "Live Simulation View" to "Simulation View".
//   - Enhanced layout for better visual hierarchy.
//   - Added extensive debug output and test button for troubleshooting.
// ========================================

using Eto.Drawing;
using Eto.Forms;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace LilyPadGH.Components
{
    public sealed class LilyPadCfdDialog : Form
    {
        // ========================
        // Region: Fields & State
        // ========================
        #region Fields & State

        private readonly LilyPadCfdSettings _settings;
        private readonly string _tempFramePath;
        private readonly string _tempGifPath; // Path for final GIF display

        // UI control references - Settings
        private NumericStepper _reynoldsStepper;
        private NumericStepper _velocityStepper;
        private NumericStepper _gridXStepper;
        private NumericStepper _gridYStepper;
        private NumericStepper _durationStepper;
        private NumericStepper _curveDivisionsStepper;

        // UI control references - Julia parameters
        private NumericStepper _simulationLStepper;
        private NumericStepper _simulationUStepper;
        private NumericStepper _animationDurationStepper;
        private CheckBox _plotBodyCheckBox;
        private NumericStepper _simplifyToleranceStepper;
        private NumericStepper _maxPointsPerPolyStepper;

        // UI control references - View & status
        private WebView _simulationView;
        private UITimer _viewTimer;
        private Label _statusLabel;
        private Label _bottomStatusLabel; // Status label below the view

        // UI control references - Action buttons
        private Button _startServerButton;
        private Button _stopServerButton;
        private Button _applyAndRunButton;
        private Button _closeButton;

        #endregion

        // ========================
        // Region: Events
        // ========================
        #region Events

        // Events for server control and parameter application
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

            // Use the SAME temp directory as Julia uses - the package temp folder
            string packageTempDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "McNeel", "Rhinoceros", "packages", "8.0", "LilyPadGH", "0.0.1", "Temp"
            );

            // Ensure the directory exists
            if (!Directory.Exists(packageTempDir))
            {
                Directory.CreateDirectory(packageTempDir);
            }

            string processId = System.Diagnostics.Process.GetCurrentProcess().Id.ToString();
            _tempFramePath = Path.Combine(packageTempDir, $"lilypad_sim_frame_{processId}.png");
            _tempGifPath = Path.Combine(packageTempDir, $"lilypad_sim_final_{processId}.gif");

            Console.WriteLine($"[DIALOG INIT] Frame path: {_tempFramePath}");
            Console.WriteLine($"[DIALOG INIT] GIF path: {_tempGifPath}");

            BuildUI();
            LoadSettingsIntoUI();

            // Timer for real-time frame updates at 5 FPS
            _viewTimer = new UITimer();
            _viewTimer.Interval = 0.2;
            _viewTimer.Elapsed += UpdateSimulationView;
            _viewTimer.Start();
        }

        #endregion

        // ========================
        // Region: Frame Update Logic
        // ========================
        #region Frame Update Logic

        /// <summary>
        /// Updates the simulation view with the latest frame from the temp file.
        /// Looks for most recent GIF in the package temp folder.
        /// Uses WebView to display animated GIFs properly.
        /// </summary>
        private void UpdateSimulationView(object sender, EventArgs e)
        {
            try
            {
                // Priority 1: Check for any recent GIF files in the package temp folder
                string packageTempDir = Path.GetDirectoryName(_tempGifPath);

                if (Directory.Exists(packageTempDir))
                {
                    var gifFiles = Directory.GetFiles(packageTempDir, "simulation_*.gif")
                        .Select(f => new FileInfo(f))
                        .OrderByDescending(f => f.LastWriteTime)
                        .ToList();

                    if (gifFiles.Any())
                    {
                        // Get the most recent GIF
                        var latestGif = gifFiles.First();

                        // Only display if it's recent (modified in last 30 seconds)
                        if ((DateTime.Now - latestGif.LastWriteTime).TotalSeconds < 30)
                        {
                            Application.Instance.Invoke(() =>
                            {
                                try
                                {
                                    // Read GIF file as bytes and convert to base64 for embedding
                                    byte[] gifBytes = File.ReadAllBytes(latestGif.FullName);
                                    string base64Gif = Convert.ToBase64String(gifBytes);

                                    // Embed GIF as data URI in HTML for full animation support
                                    string html = $@"
                                        <html>
                                        <head>
                                            <style>
                                                body {{ 
                                                    margin: 0; 
                                                    padding: 10px; 
                                                    background: #ffffff; 
                                                    display: flex; 
                                                    justify-content: center; 
                                                    align-items: center; 
                                                    height: 100vh;
                                                    overflow: hidden;
                                                    box-sizing: border-box;
                                                }}
                                                ::-webkit-scrollbar {{
                                                    display: none;
                                                }}
                                                html {{
                                                    scrollbar-width: none;
                                                    -ms-overflow-style: none;
                                                    overflow: hidden;
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
                                            <img src='data:image/gif;base64,{base64Gif}' alt='Simulation Animation' />
                                        </body>
                                        </html>
                                    ";

                                    _simulationView.LoadHtml(html);
                                    _statusLabel.Text = $"✅ GIF PLAYING! {latestGif.Name} ({latestGif.Length / 1024}KB, {latestGif.LastWriteTime:HH:mm:ss})";
                                    _statusLabel.TextColor = Colors.Green;
                                    _bottomStatusLabel.Text = "Simulation Complete";
                                    _bottomStatusLabel.TextColor = Colors.Green;

                                    Console.WriteLine($"[SUCCESS] Animated GIF loaded: {latestGif.Name}");
                                }
                                catch (Exception ex)
                                {
                                    _statusLabel.Text = $"❌ GIF Load Error: {ex.Message}";
                                    _statusLabel.TextColor = Colors.Red;
                                    Console.WriteLine($"[ERROR] Failed to load GIF: {ex.Message}");
                                }
                            });
                            return;
                        }
                    }
                }

                // Priority 2: Check for live PNG frames (simulation in progress)
                if (File.Exists(_tempFramePath))
                {
                    var frameInfo = new FileInfo(_tempFramePath);

                    // Only update if file was modified in the last 2 seconds (active simulation)
                    if ((DateTime.Now - frameInfo.LastWriteTime).TotalSeconds < 2)
                    {
                        Application.Instance.Invoke(() =>
                        {
                            try
                            {
                                // Read PNG file as bytes and convert to base64 for embedding
                                byte[] pngBytes = File.ReadAllBytes(_tempFramePath);
                                string base64Png = Convert.ToBase64String(pngBytes);

                                // Display live PNG frame as data URI
                                string html = $@"
                                    <html>
                                    <head>
                                        <style>
                                            body {{ 
                                                margin: 0; 
                                                padding: 10px; 
                                                background: #ffffff; 
                                                display: flex; 
                                                justify-content: center; 
                                                align-items: center; 
                                                height: 100vh;
                                                overflow: hidden;
                                                box-sizing: border-box;
                                            }}
                                            ::-webkit-scrollbar {{
                                                display: none;
                                            }}
                                            html {{
                                                scrollbar-width: none;
                                                -ms-overflow-style: none;
                                                overflow: hidden;
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
                                        <img src='data:image/png;base64,{base64Png}' alt='Live Frame' />
                                    </body>
                                    </html>
                                ";

                                _simulationView.LoadHtml(html);
                                _statusLabel.Text = $"🔄 Live frame updated: {DateTime.Now:HH:mm:ss}";
                                _statusLabel.TextColor = Colors.Blue;
                                _bottomStatusLabel.Text = "Simulation Running...";
                                _bottomStatusLabel.TextColor = Colors.Blue;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Frame decode error: {ex.Message}");
                            }
                        });
                    }
                    else
                    {
                        // File is stale - simulation likely finished, waiting for GIF
                        Application.Instance.Invoke(() =>
                        {
                            _statusLabel.Text = $"⏳ Simulation finishing - Looking for GIF";
                            _statusLabel.TextColor = Colors.Orange;
                            _bottomStatusLabel.Text = "Generating Animation...";
                            _bottomStatusLabel.TextColor = Colors.Orange;
                        });
                    }
                }
                else
                {
                    // No frame file exists yet - only update status occasionally to avoid spam
                    if (DateTime.Now.Second % 3 == 0)
                    {
                        Application.Instance.Invoke(() =>
                        {
                            _statusLabel.Text = $"⏳ Waiting for simulation...";
                            _statusLabel.TextColor = Colors.Gray;
                            _bottomStatusLabel.Text = "Waiting for Simulation";
                            _bottomStatusLabel.TextColor = Colors.Gray;
                        });
                    }
                }
            }
            catch (IOException)
            {
                // File is locked by Julia during write - this is normal, ignore
            }
            catch (Exception ex)
            {
                // Other errors - log but don't crash
                Application.Instance.Invoke(() =>
                {
                    _statusLabel.Text = $"⚠️ View update error: {ex.Message}";
                    _statusLabel.TextColor = Colors.Red;
                });
                Console.WriteLine($"View update error: {ex.Message}");
            }
        }

        #endregion

        // ========================
        // Region: Server State Management
        // ========================
        #region Server State Management

        /// <summary>
        /// Updates UI elements to reflect current Julia server state.
        /// Enables/disables appropriate buttons and provides visual feedback.
        /// </summary>
        public void SetServerState(bool isServerRunning)
        {
            Application.Instance.Invoke(() =>
            {
                _startServerButton.Enabled = !isServerRunning;
                _stopServerButton.Enabled = isServerRunning;
                _applyAndRunButton.Enabled = isServerRunning;

                // Update button visual states
                _startServerButton.Text = isServerRunning ? "Server Running" : "Start Server";
                _startServerButton.BackgroundColor = isServerRunning ? Colors.LightGreen : SystemColors.Control;

                _applyAndRunButton.Text = isServerRunning ? "Apply Settings & Run" : "Start Server First";
                _applyAndRunButton.BackgroundColor = isServerRunning ? Colors.SteelBlue : Colors.Gray;

                // Update status message
                _statusLabel.Text = isServerRunning ? "Julia server is running - Ready for simulation" : "Julia server stopped";
                _bottomStatusLabel.Text = isServerRunning ? "Ready for Simulation" : "Server Stopped";
                _bottomStatusLabel.TextColor = isServerRunning ? Colors.Blue : Colors.Gray;

                // DON'T clear simulation view when server stops - keep last animation playing
                // Only clean up temp files
                if (!isServerRunning)
                {
                    try
                    {
                        if (File.Exists(_tempFramePath))
                            File.Delete(_tempFramePath);
                        // Don't delete GIF - keep it for the animation that's still playing
                    }
                    catch { }
                }
            });
        }

        #endregion

        // ========================
        // Region: UI Construction
        // ========================
        #region UI Construction

        /// <summary>
        /// Builds the complete UI layout with settings on left and simulation view on right.
        /// </summary>
        private void BuildUI()
        {
            Title = "LilyPad CFD Simulation Control";
            MinimumSize = new Size(1100, 700);
            Resizable = true;

            InitializeControls();

            // Status label at top
            _statusLabel = new Label
            {
                Text = "Ready to start server",
                Font = new Font(SystemFont.Default, 10),
                TextColor = Colors.Blue
            };

            // Simulation view using WebView for animated GIF support
            _simulationView = new WebView
            {
                Size = new Size(500, 400)
            };

            // Load initial placeholder HTML with hidden scrollbars
            _simulationView.LoadHtml(@"
                <html>
                <head>
                    <style>
                        body { 
                            margin: 0; 
                            padding: 20px; 
                            background: #ffffff; 
                            display: flex; 
                            justify-content: center; 
                            align-items: center; 
                            height: 100vh;
                            font-family: Arial, sans-serif;
                            overflow: hidden;
                        }
                        ::-webkit-scrollbar {
                            display: none;
                        }
                        html {
                            scrollbar-width: none;
                            -ms-overflow-style: none;
                        }
                        .placeholder {
                            text-align: center;
                            color: #888;
                        }
                    </style>
                </head>
                <body>
                    <div class='placeholder'>
                        <h2>Simulation View</h2>
                        <p>Waiting for Simulation</p>
                    </div>
                </body>
                </html>
            ");

            // Main layout structure
            var mainLayout = new DynamicLayout { Padding = new Padding(10) };

            // Top: Status bar
            mainLayout.Add(_statusLabel, yscale: false);

            // Middle: Settings (left) + Simulation View (right)
            var contentSplitter = new Splitter
            {
                Panel1 = CreateSettingsPanel(),
                Panel2 = CreateSimulationPanel(),
                Position = 350, // Reduced from 550 to give more space to simulation view
                Orientation = Orientation.Horizontal // Horizontal split = left/right panels
            };
            mainLayout.Add(contentSplitter, yscale: true);

            // Bottom: Action buttons
            mainLayout.Add(CreateButtonPanel(), yscale: false);

            Content = mainLayout;
        }

        /// <summary>
        /// Initialises all UI controls and wires up event handlers.
        /// </summary>
        private void InitializeControls()
        {
            // Flow parameter controls - reduced to 80 width (quarter of original)
            _reynoldsStepper = new NumericStepper { MinValue = 1, DecimalPlaces = 1, Width = 80 };
            _velocityStepper = new NumericStepper { MinValue = 0, DecimalPlaces = 2, Width = 80 };

            // Grid parameter controls - reduced to 80 width
            _gridXStepper = new NumericStepper { MinValue = 10, Increment = 8, Width = 80 };
            _gridYStepper = new NumericStepper { MinValue = 10, Increment = 8, Width = 80 };
            _durationStepper = new NumericStepper { MinValue = 0.1, DecimalPlaces = 2, Increment = 1.0, Width = 80 };
            _curveDivisionsStepper = new NumericStepper { MinValue = 2, Increment = 1, Width = 80 };

            // Julia simulation controls - reduced to 80 width
            _simulationLStepper = new NumericStepper { MinValue = 16, MaxValue = 128, Increment = 8, Value = 32, Width = 80 };
            _simulationUStepper = new NumericStepper { MinValue = 0.1, DecimalPlaces = 2, Increment = 0.1, Value = 1.0, Width = 80 };
            _animationDurationStepper = new NumericStepper { MinValue = 0.1, DecimalPlaces = 1, Increment = 0.5, Value = 1.0, Width = 80 };
            _plotBodyCheckBox = new CheckBox { Text = "Show Body in Animation", Checked = true };

            // Point reduction controls - reduced to 80 width
            _simplifyToleranceStepper = new NumericStepper { MinValue = 0.0, MaxValue = 0.5, DecimalPlaces = 3, Increment = 0.001, Value = 0.0, Width = 80 };
            _maxPointsPerPolyStepper = new NumericStepper { MinValue = 5, MaxValue = 1000, Increment = 10, Value = 1000, Width = 80 };

            // Action buttons
            _startServerButton = new Button { Text = "Start Server", Width = 120 };
            _stopServerButton = new Button { Text = "Stop Server", Width = 120, Enabled = false };
            _applyAndRunButton = new Button { Text = "Start Server First", Width = 150, Enabled = false, BackgroundColor = Colors.Gray };
            _closeButton = new Button { Text = "Close", Width = 80 };

            // Wire up button events
            _startServerButton.Click += (s, e) => OnStartServerClicked?.Invoke();
            _stopServerButton.Click += (s, e) => OnStopServerClicked?.Invoke();
            _applyAndRunButton.Click += OnApplyAndRunButtonClick;
            _closeButton.Click += (s, e) => Close();
        }

        /// <summary>
        /// Creates the left panel containing all simulation settings.
        /// Organised into logical groups with clear headings.
        /// </summary>
        private Control CreateSettingsPanel()
        {
            var layout = new DynamicLayout { DefaultSpacing = new Size(5, 5), Padding = new Padding(10) };

            // Flow parameters section
            layout.Add(new Label { Text = "Flow Parameters", Font = new Font(SystemFont.Bold) });
            layout.AddRow(new Label { Text = "Reynolds Number:" }, _reynoldsStepper);
            layout.AddRow(new Label { Text = "Flow Velocity (m/s):" }, _velocityStepper);

            layout.AddSpace();

            // Grid parameters section
            layout.Add(new Label { Text = "Grid Parameters", Font = new Font(SystemFont.Bold) });
            layout.AddRow(new Label { Text = "Grid Resolution X:" }, _gridXStepper);
            layout.AddRow(new Label { Text = "Grid Resolution Y:" }, _gridYStepper);
            layout.AddRow(new Label { Text = "Simulation Duration (s):" }, _durationStepper);
            layout.AddRow(new Label { Text = "Curve Divisions:" }, _curveDivisionsStepper);

            layout.AddSpace();

            // Julia simulation parameters section
            layout.Add(new Label { Text = "Julia Simulation Parameters", Font = new Font(SystemFont.Bold) });
            layout.AddRow(new Label { Text = "Simulation Scale (L):" }, _simulationLStepper);
            layout.AddRow(new Label { Text = "Flow Scale (U):" }, _simulationUStepper);
            layout.AddRow(new Label { Text = "Animation Duration (s):" }, _animationDurationStepper);
            layout.Add(_plotBodyCheckBox);

            layout.AddSpace();

            // Point reduction settings section
            layout.Add(new Label { Text = "Point Reduction Settings", Font = new Font(SystemFont.Bold) });
            layout.AddRow(new Label { Text = "Simplify Tolerance:" }, _simplifyToleranceStepper);
            layout.AddRow(new Label { Text = "Max Points Per Polyline:" }, _maxPointsPerPolyStepper);

            layout.Add(null, yscale: true); // Flexible spacer at bottom

            return layout;
        }

        /// <summary>
        /// Creates the right panel containing the simulation view.
        /// Shows real-time frames during simulation and final GIF when complete.
        /// </summary>
        private Control CreateSimulationPanel()
        {
            var layout = new DynamicLayout { Padding = new Padding(10) };

            layout.Add(new Label { Text = "Simulation View", Font = new Font(SystemFont.Bold) });
            layout.Add(_simulationView, yscale: true);

            // Bottom status label replacing the old placeholder text
            _bottomStatusLabel = new Label
            {
                Text = "Waiting for Simulation",
                TextAlignment = TextAlignment.Center,
                TextColor = Colors.Gray
            };
            layout.Add(_bottomStatusLabel, yscale: false);

            return layout;
        }

        /// <summary>
        /// Creates the bottom button panel with all action buttons.
        /// Organised left-to-right: server controls, spacer, run button, close button.
        /// </summary>
        private Control CreateButtonPanel()
        {
            return new TableLayout
            {
                Spacing = new Size(10, 5),
                Rows = {
                    new TableRow(
                        _startServerButton,
                        _stopServerButton,
                        new TableCell(null, true), // Flexible spacer
                        _applyAndRunButton,
                        _closeButton
                    )
                }
            };
        }

        #endregion

        // ========================
        // Region: Settings Management
        // ========================
        #region Settings Management

        /// <summary>
        /// Loads current settings into UI controls.
        /// Called during dialog initialisation.
        /// </summary>
        private void LoadSettingsIntoUI()
        {
            _reynoldsStepper.Value = _settings.ReynoldsNumber;
            _velocityStepper.Value = _settings.Velocity;
            _gridXStepper.Value = _settings.GridResolutionX;
            _gridYStepper.Value = _settings.GridResolutionY;
            _durationStepper.Value = _settings.Duration;
            _curveDivisionsStepper.Value = _settings.CurveDivisions;
            _simulationLStepper.Value = _settings.SimulationL;
            _simulationUStepper.Value = _settings.SimulationU;
            _animationDurationStepper.Value = _settings.AnimationDuration;
            _plotBodyCheckBox.Checked = _settings.PlotBody;
            _simplifyToleranceStepper.Value = _settings.SimplifyTolerance;
            _maxPointsPerPolyStepper.Value = _settings.MaxPointsPerPoly;

            SetServerState(false);
        }

        /// <summary>
        /// Updates settings object from current UI control values.
        /// Called before applying settings and running simulation.
        /// </summary>
        private void UpdateSettingsFromUI()
        {
            _settings.ReynoldsNumber = _reynoldsStepper.Value;
            _settings.Velocity = _velocityStepper.Value;
            _settings.GridResolutionX = (int)_gridXStepper.Value;
            _settings.GridResolutionY = (int)_gridYStepper.Value;
            _settings.Duration = _durationStepper.Value;
            _settings.CurveDivisions = (int)_curveDivisionsStepper.Value;
            _settings.SimulationL = (int)_simulationLStepper.Value;
            _settings.SimulationU = _simulationUStepper.Value;
            _settings.AnimationDuration = _animationDurationStepper.Value;
            _settings.PlotBody = _plotBodyCheckBox.Checked ?? true;
            _settings.SimplifyTolerance = _simplifyToleranceStepper.Value;
            _settings.MaxPointsPerPoly = (int)_maxPointsPerPolyStepper.Value;
        }

        #endregion

        // ========================
        // Region: Event Handlers
        // ========================
        #region Event Handlers

        /// <summary>
        /// Handles Apply Settings & Run button click.
        /// Updates settings from UI, passes temp paths to component, and triggers simulation.
        /// </summary>
        private void OnApplyAndRunButtonClick(object sender, EventArgs e)
        {
            UpdateSettingsFromUI();

            // Pass both temp paths to the component for Julia communication
            _settings.UIFramePath = _tempFramePath; // Live PNG frames
            _settings.UIGifPath = _tempGifPath;     // Final GIF output

            // Debug output to verify paths are set
            Console.WriteLine("=== DIALOG DEBUG: Paths Set ===");
            Console.WriteLine($"Frame Path: {_tempFramePath}");
            Console.WriteLine($"GIF Path: {_tempGifPath}");
            Console.WriteLine($"GIF Path Empty: {string.IsNullOrEmpty(_tempGifPath)}");
            Console.WriteLine("==============================");

            OnApplyAndRunClicked?.Invoke(_settings);

            _statusLabel.Text = "Simulation starting - Real-time view will update shortly...";
        }

        #endregion

        // ========================
        // Region: Cleanup
        // ========================
        #region Cleanup

        /// <summary>
        /// Handles dialog close event - cleans up resources and temp files.
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Stop and clean up timer
            _viewTimer?.Stop();
            if (_viewTimer != null)
            {
                _viewTimer.Elapsed -= UpdateSimulationView;
                _viewTimer = null;
            }

            // Remove temp files (both PNG frames and final GIF)
            try
            {
                if (File.Exists(_tempFramePath))
                    File.Delete(_tempFramePath);
                if (File.Exists(_tempGifPath))
                    File.Delete(_tempGifPath);
            }
            catch { }
        }

        #endregion

        // ========================
        // Region: Public Properties
        // ========================
        #region Public Properties

        /// <summary>
        /// Gets the temp frame path where Julia writes live simulation frames.
        /// </summary>
        public string UIFramePath => _tempFramePath;

        /// <summary>
        /// Gets the temp GIF path where Julia saves the final simulation GIF.
        /// </summary>
        public string UIGifPath => _tempGifPath;

        #endregion
    }
}