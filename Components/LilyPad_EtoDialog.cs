// ========================================
// FILE: LilyPad_EtoDialog.cs
// PART 3: ETO CONFIGURATION DIALOG WITH REAL-TIME DISPLAY
// DESC: Creates and manages the pop-up settings window with real-time simulation view.
//       Clean button structure and proper frame display for live simulation feedback.
// --- REVISIONS ---
// - 2025-09-22: Polished real-time simulation view with proper frame handling.
//   - Cleaned up button structure to four logical buttons.
//   - Enhanced frame reading with better error handling and file locking.
//   - Added proper temp file path communication to Julia server.
//   - Improved visual feedback and status updates.
// ========================================

using Eto.Drawing;
using Eto.Forms;
using System;
using System.IO;

namespace LilyPadGH.Components
{
    public sealed class LilyPadCfdDialog : Form
    {
        private readonly LilyPadCfdSettings _settings;
        private readonly string _tempFramePath;

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

        // Real-time view and clean button structure
        private ImageView _simulationView;
        private UITimer _viewTimer;
        private Label _statusLabel;
        
        // Four clean buttons as requested
        private Button _startServerButton;
        private Button _stopServerButton;
        private Button _applyAndRunButton;
        private Button _closeButton;

        // Events for server control and parameter application
        public event Action OnStartServerClicked;
        public event Action OnStopServerClicked;
        public event Action<LilyPadCfdSettings> OnApplyAndRunClicked;

        public LilyPadCfdDialog(LilyPadCfdSettings currentSettings)
        {
            _settings = currentSettings.Clone();
            
            // Create unique temp file path for this dialog instance
            string tempDir = Path.GetTempPath();
            _tempFramePath = Path.Combine(tempDir, $"lilypad_sim_frame_{System.Diagnostics.Process.GetCurrentProcess().Id}.png");
            
            BuildUI();
            LoadSettingsIntoUI();

            // Timer for real-time frame updates
            _viewTimer = new UITimer();
            _viewTimer.Interval = 0.2; // 5 FPS update rate
            _viewTimer.Elapsed += UpdateSimulationView;
            _viewTimer.Start();
        }

        private void UpdateSimulationView(object sender, EventArgs e)
        {
            try
            {
                if (File.Exists(_tempFramePath))
                {
                    // Get file info to check if it's been recently updated
                    var fileInfo = new FileInfo(_tempFramePath);
                    
                    // Only update if file was modified in the last 2 seconds (active simulation)
                    if ((DateTime.Now - fileInfo.LastWriteTime).TotalSeconds < 2)
                    {
                        // Read file into memory to avoid locking issues
                        byte[] imageData;
                        using (var fs = new FileStream(_tempFramePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            imageData = new byte[fs.Length];
                            int totalBytesRead = 0;
                            int bytesRead;
                            while (totalBytesRead < imageData.Length)
                            {
                                bytesRead = fs.Read(imageData, totalBytesRead, imageData.Length - totalBytesRead);
                                if (bytesRead == 0)
                                    break;
                                totalBytesRead += bytesRead;
                            }
                        }

                        // Update UI on main thread
                        Application.Instance.Invoke(() =>
                        {
                            try
                            {
                                using (var ms = new MemoryStream(imageData))
                                {
                                    _simulationView.Image = new Bitmap(ms);
                                    _statusLabel.Text = $"Live simulation - Frame updated: {DateTime.Now:HH:mm:ss}";
                                }
                            }
                            catch (Exception ex)
                            {
                                // Image format error - ignore and try next frame
                                Console.WriteLine($"Frame decode error: {ex.Message}");
                            }
                        });
                    }
                    else
                    {
                        // File is stale - simulation likely finished
                        Application.Instance.Invoke(() =>
                        {
                            _statusLabel.Text = "Simulation view - No active simulation";
                        });
                    }
                }
            }
            catch (IOException)
            {
                // File is locked by Julia - this is normal during writing, ignore
            }
            catch (Exception ex)
            {
                // Other errors - log but don't crash
                Console.WriteLine($"View update error: {ex.Message}");
            }
        }

        public void SetServerState(bool isServerRunning)
        {
            Application.Instance.Invoke(() =>
            {
                _startServerButton.Enabled = !isServerRunning;
                _stopServerButton.Enabled = isServerRunning;
                _applyAndRunButton.Enabled = isServerRunning;

                // Update button text and appearance
                _startServerButton.Text = isServerRunning ? "Server Running" : "Start Server";
                _startServerButton.BackgroundColor = isServerRunning ? Colors.LightGreen : SystemColors.Control;
                
                _applyAndRunButton.Text = isServerRunning ? "Apply Settings & Run" : "Start Server First";
                _applyAndRunButton.BackgroundColor = isServerRunning ? Colors.SteelBlue : Colors.Gray;

                // Update status
                _statusLabel.Text = isServerRunning ? "Julia server is running - Ready for simulation" : "Julia server stopped";
                
                // Clear simulation view if server stopped
                if (!isServerRunning)
                {
                    _simulationView.Image = null;
                    // Clean up temp file
                    try
                    {
                        if (File.Exists(_tempFramePath))
                            File.Delete(_tempFramePath);
                    }
                    catch { }
                }
            });
        }

        private void BuildUI()
        {
            Title = "LilyPad CFD Simulation Control";
            MinimumSize = new Size(900, 700);
            Resizable = true;

            // Initialize all controls
            InitializeControls();
            
            // Create status label
            _statusLabel = new Label 
            { 
                Text = "Ready to start server",
                Font = new Font(SystemFont.Default, 10),
                TextColor = Colors.Blue
            };

            // Create simulation view with placeholder
            _simulationView = new ImageView 
            { 
                BackgroundColor = Colors.LightGrey,
                Size = new Size(400, 300)
            };
            
            // Add placeholder text overlay (this will be replaced by actual simulation)
            var viewPanel = new Panel
            {
                Content = _simulationView,
                BackgroundColor = Colors.LightGrey
            };

            // Build main layout
            var mainLayout = new DynamicLayout { Padding = new Padding(10) };
            
            // Status bar at top
            mainLayout.Add(_statusLabel, yscale: false);
            
            // Main content splitter
            var splitter = new Splitter
            {
                Panel1 = CreateSettingsPanel(),
                Panel2 = CreateSimulationPanel(),
                Position = 400,
                Orientation = Orientation.Vertical
            };
            mainLayout.Add(splitter, yscale: true);
            
            // Button panel at bottom
            mainLayout.Add(CreateButtonPanel(), yscale: false);

            Content = mainLayout;
        }

        private void InitializeControls()
        {
            _reynoldsStepper = new NumericStepper { MinValue = 1, DecimalPlaces = 1, Width = 100 };
            _velocityStepper = new NumericStepper { MinValue = 0, DecimalPlaces = 2, Width = 100 };
            _gridXStepper = new NumericStepper { MinValue = 10, Increment = 8, Width = 100 };
            _gridYStepper = new NumericStepper { MinValue = 10, Increment = 8, Width = 100 };
            _durationStepper = new NumericStepper { MinValue = 0.1, DecimalPlaces = 2, Increment = 1.0, Width = 100 };
            _curveDivisionsStepper = new NumericStepper { MinValue = 2, Increment = 1, Width = 100 };
            _simulationLStepper = new NumericStepper { MinValue = 16, MaxValue = 128, Increment = 8, Value = 32, Width = 100 };
            _simulationUStepper = new NumericStepper { MinValue = 0.1, DecimalPlaces = 2, Increment = 0.1, Value = 1.0, Width = 100 };
            _animationDurationStepper = new NumericStepper { MinValue = 0.1, DecimalPlaces = 1, Increment = 0.5, Value = 1.0, Width = 100 };
            _plotBodyCheckBox = new CheckBox { Text = "Show Body in Animation", Checked = true };
            _simplifyToleranceStepper = new NumericStepper { MinValue = 0.0, MaxValue = 0.5, DecimalPlaces = 3, Increment = 0.001, Value = 0.0, Width = 100 };
            _maxPointsPerPolyStepper = new NumericStepper { MinValue = 5, MaxValue = 1000, Increment = 10, Value = 1000, Width = 100 };

            // Four clean buttons as requested
            _startServerButton = new Button { Text = "Start Server", Width = 120 };
            _stopServerButton = new Button { Text = "Stop Server", Width = 120, Enabled = false };
            _applyAndRunButton = new Button { Text = "Start Server First", Width = 150, Enabled = false, BackgroundColor = Colors.Gray };
            _closeButton = new Button { Text = "Close", Width = 80 };

            // Wire up events
            _startServerButton.Click += (s, e) => OnStartServerClicked?.Invoke();
            _stopServerButton.Click += (s, e) => OnStopServerClicked?.Invoke();
            _applyAndRunButton.Click += OnApplyAndRunButtonClick;
            _closeButton.Click += (s, e) => Close();
        }

        private Control CreateSettingsPanel()
        {
            var layout = new DynamicLayout { DefaultSpacing = new Size(5, 5), Padding = new Padding(10) };
            
            // Flow parameters
            layout.Add(new Label { Text = "Flow Parameters", Font = new Font(SystemFont.Bold) });
            layout.AddRow(new Label { Text = "Reynolds Number:" }, _reynoldsStepper);
            layout.AddRow(new Label { Text = "Flow Velocity (m/s):" }, _velocityStepper);
            
            layout.AddSpace();
            
            // Grid parameters
            layout.Add(new Label { Text = "Grid Parameters", Font = new Font(SystemFont.Bold) });
            layout.AddRow(new Label { Text = "Grid Resolution X:" }, _gridXStepper);
            layout.AddRow(new Label { Text = "Grid Resolution Y:" }, _gridYStepper);
            layout.AddRow(new Label { Text = "Simulation Duration (s):" }, _durationStepper);
            layout.AddRow(new Label { Text = "Curve Divisions:" }, _curveDivisionsStepper);
            
            layout.AddSpace();
            
            // Julia simulation parameters
            layout.Add(new Label { Text = "Julia Simulation Parameters", Font = new Font(SystemFont.Bold) });
            layout.AddRow(new Label { Text = "Simulation Scale (L):" }, _simulationLStepper);
            layout.AddRow(new Label { Text = "Flow Scale (U):" }, _simulationUStepper);
            layout.AddRow(new Label { Text = "Animation Duration (s):" }, _animationDurationStepper);
            layout.Add(_plotBodyCheckBox);
            
            layout.AddSpace();
            
            // Point reduction settings
            layout.Add(new Label { Text = "Point Reduction Settings", Font = new Font(SystemFont.Bold) });
            layout.AddRow(new Label { Text = "Simplify Tolerance:" }, _simplifyToleranceStepper);
            layout.AddRow(new Label { Text = "Max Points Per Polyline:" }, _maxPointsPerPolyStepper);
            
            layout.Add(null, yscale: true); // Flexible spacer
            
            return layout;
        }

        private Control CreateSimulationPanel()
        {
            var layout = new DynamicLayout { Padding = new Padding(10) };
            
            layout.Add(new Label { Text = "Live Simulation View", Font = new Font(SystemFont.Bold) });
            layout.Add(_simulationView, yscale: true);
            
            var infoLabel = new Label 
            { 
                Text = "Real-time simulation frames will appear here when running",
                TextAlignment = TextAlignment.Center,
                TextColor = Colors.Gray
            };
            layout.Add(infoLabel, yscale: false);
            
            return layout;
        }

        private Control CreateButtonPanel()
        {
            return new TableLayout
            {
                Spacing = new Size(10, 5),
                Rows = { 
                    new TableRow(
                        _startServerButton, 
                        _stopServerButton, 
                        new TableCell(null, true), // Spacer
                        _applyAndRunButton, 
                        _closeButton
                    ) 
                }
            };
        }

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

        private void OnApplyAndRunButtonClick(object sender, EventArgs e)
        {
            UpdateSettingsFromUI();
            
            // Pass the temp frame path to the component for Julia communication
            _settings.UIFramePath = _tempFramePath;
            
            OnApplyAndRunClicked?.Invoke(_settings);
            
            _statusLabel.Text = "Simulation starting - Real-time view will update shortly...";
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            
            // Clean up resources
            _viewTimer?.Stop();
            if (_viewTimer != null)
            {
                _viewTimer.Elapsed -= UpdateSimulationView;
                _viewTimer = null;
            }
            
            // Clean up temp file
            try
            {
                if (File.Exists(_tempFramePath))
                    File.Delete(_tempFramePath);
            }
            catch { }
        }

        // Property to get the temp frame path for the component
        public string UIFramePath => _tempFramePath;
    }
}