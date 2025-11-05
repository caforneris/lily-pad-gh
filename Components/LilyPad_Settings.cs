// ========================================
// FILE: LilyPad_Settings.cs (CORRECTED VERSION)
// DESC: Industry-standard CFD settings data model with clarified time parameters.
// --- REVISIONS ---
// - 2025-10-11: Removed AnimationDuration parameter.
//   - TotalSimulationTime now controls both simulation length and animation duration.
//   - Simplified parameter model to eliminate confusion.
//   - All duration settings now use a single time value.
// ========================================

namespace LilyPadGH.Components
{
    public class LilyPadCfdSettings
    {
        // =======================
        // Region: Fluid Properties
        // =======================
        #region Fluid Properties

        /// <summary>
        /// Fluid density in kg/m³.
        /// Water = 1000, Air = 1.225
        /// </summary>
        public double FluidDensity { get; set; } = 1000.0;

        /// <summary>
        /// Kinematic viscosity (ν) in m²/s.
        /// Water at 20°C = 1.004e-6, Air at 20°C = 1.516e-5
        /// </summary>
        public double KinematicViscosity { get; set; } = 1.004e-6;

        /// <summary>
        /// Flow inlet velocity in m/s.
        /// </summary>
        public double InletVelocity { get; set; } = 1.0;

        /// <summary>
        /// Reynolds number (Re = UL/ν).
        /// Automatically calculated but can be overridden.
        /// Re < 2300: Laminar, Re > 4000: Turbulent
        /// </summary>
        public double ReynoldsNumber { get; set; } = 250.0;

        #endregion

        // ===============================
        // Region: Domain Configuration
        // ===============================
        #region Domain Configuration

        /// <summary>
        /// Physical domain width in metres.
        /// </summary>
        public double DomainWidth { get; set; } = 2.0;

        /// <summary>
        /// Physical domain height in metres.
        /// </summary>
        public double DomainHeight { get; set; } = 1.0;

        /// <summary>
        /// Grid cells in X direction (width).
        /// Higher = more accurate but slower.
        /// Typical range: 64-512
        /// </summary>
        public int GridResolutionX { get; set; } = 192;

        /// <summary>
        /// Grid cells in Y direction (height).
        /// Higher = more accurate but slower.
        /// Typical range: 32-256
        /// </summary>
        public int GridResolutionY { get; set; } = 128;

        /// <summary>
        /// Characteristic length scale for grid refinement.
        /// Typically: domain_width / grid_resolution_x
        /// Used in Reynolds number calculation.
        /// </summary>
        public double CharacteristicLength { get; set; } = 0.01;

        #endregion

        // ===========================
        // Region: Time Stepping
        // ===========================
        #region Time Stepping

        /// <summary>
        /// Time step size in seconds.
        /// Smaller = more stable but slower.
        /// Typical: 0.001 - 0.1
        /// </summary>
        public double TimeStep { get; set; } = 0.01;

        /// <summary>
        /// CORRECTED: Total simulation time in seconds.
        /// This controls both:
        ///   1. How long the physical simulation runs
        ///   2. The duration of the output animation
        /// Example: 200 seconds means the simulation runs for 200 seconds of physical time.
        /// </summary>
        public double TotalSimulationTime { get; set; } = 10.0;

        /// <summary>
        /// CFL (Courant–Friedrichs–Lewy) number for stability.
        /// Must be < 1 for stability. Typical: 0.5-0.9
        /// </summary>
        public double CFLNumber { get; set; } = 0.5;

        #endregion

        // ===========================
        // Region: Solver Settings
        // ===========================
        #region Solver Settings

        /// <summary>
        /// Maximum iterations per time step.
        /// Higher = more accurate pressure solution.
        /// Typical: 100-1000
        /// </summary>
        public int MaxIterations { get; set; } = 100;

        /// <summary>
        /// Convergence tolerance for pressure solver.
        /// Smaller = more accurate but slower.
        /// Typical: 1e-4 to 1e-8
        /// </summary>
        public double ConvergenceTolerance { get; set; } = 1e-5;

        /// <summary>
        /// Under-relaxation factor for stability.
        /// Range: 0.1-1.0, lower = more stable but slower.
        /// </summary>
        public double RelaxationFactor { get; set; } = 0.7;

        #endregion

        // ===========================
        // Region: Geometry Settings
        // ===========================
        #region Geometry Settings

        /// <summary>
        /// Number of points to discretize curves.
        /// Higher = more accurate geometry.
        /// </summary>
        public int CurveDivisions { get; set; } = 50;

        /// <summary>
        /// Douglas-Peucker simplification tolerance.
        /// 0 = no simplification.
        /// Higher = fewer points, faster simulation.
        /// </summary>
        public double SimplifyTolerance { get; set; } = 0.0;

        /// <summary>
        /// Maximum points per polyline after simplification.
        /// </summary>
        public int MaxPointsPerPoly { get; set; } = 1000;

        /// <summary>
        /// Object scale factor relative to domain.
        /// 0.3 = object occupies 30% of domain.
        /// </summary>
        public double ObjectScaleFactor { get; set; } = 0.3;

        #endregion

        // =====================================
        // Region: Visualization & Output
        // =====================================
        #region Visualization & Output

        /// <summary>
        /// Minimum value for color scale (vorticity).
        /// </summary>
        public double ColorScaleMin { get; set; } = -10.0;

        /// <summary>
        /// Maximum value for color scale (vorticity).
        /// </summary>
        public double ColorScaleMax { get; set; } = 10.0;

        /// <summary>
        /// Frames per second for animation output.
        /// Higher = smoother but larger file.
        /// </summary>
        public int AnimationFPS { get; set; } = 30;

        /// <summary>
        /// Show body outline in visualization.
        /// </summary>
        public bool ShowBody { get; set; } = true;

        /// <summary>
        /// Show velocity vectors.
        /// </summary>
        public bool ShowVelocityVectors { get; set; } = false;

        /// <summary>
        /// Show pressure contours.
        /// </summary>
        public bool ShowPressure { get; set; } = false;

        /// <summary>
        /// Show vorticity field (default visualization).
        /// </summary>
        public bool ShowVorticity { get; set; } = true;

        #endregion

        // =====================================
        // Region: Advanced WaterLily Settings
        // =====================================
        #region Advanced WaterLily Settings

        /// <summary>
        /// WaterLily grid scale parameter.
        /// Controls internal grid refinement.
        /// Power of 2 recommended: 16, 32, 64, 128
        /// </summary>
        public int WaterLilyL { get; set; } = 32;

        /// <summary>
        /// Use GPU acceleration if available (CUDA).
        /// Requires CUDA-compatible GPU.
        /// </summary>
        public bool UseGPU { get; set; } = false;

        /// <summary>
        /// Precision type for calculations.
        /// Float32 = faster, Float64 = more accurate.
        /// </summary>
        public string PrecisionType { get; set; } = "Float32";

        #endregion

        // =====================================
        // Region: Real-Time UI Display Integration
        // =====================================
        #region Real-Time UI Display Integration

        /// <summary>
        /// Path where Julia writes live PNG frames for real-time UI display.
        /// </summary>
        public string UIFramePath { get; set; } = string.Empty;

        /// <summary>
        /// Path where Julia saves the final GIF for in-UI display.
        /// When this path is provided, external GIF viewers are disabled.
        /// </summary>
        public string UIGifPath { get; set; } = string.Empty;

        #endregion

        // =======================
        // Region: Helper Methods
        // =======================
        #region Helper Methods

        /// <summary>
        /// Calculates Reynolds number from current settings.
        /// Re = (InletVelocity × CharacteristicLength) / KinematicViscosity
        /// </summary>
        public double CalculateReynoldsNumber()
        {
            return (InletVelocity * CharacteristicLength) / KinematicViscosity;
        }

        /// <summary>
        /// Calculates kinematic viscosity from Reynolds number.
        /// ν = (InletVelocity × CharacteristicLength) / ReynoldsNumber
        /// </summary>
        public double CalculateKinematicViscosity()
        {
            return (InletVelocity * CharacteristicLength) / ReynoldsNumber;
        }

        /// <summary>
        /// Calculates maximum stable time step based on CFL condition.
        /// Δt_max = CFL × (grid_spacing / velocity)
        /// </summary>
        public double CalculateMaxTimeStep()
        {
            double gridSpacing = DomainWidth / GridResolutionX;
            return CFLNumber * gridSpacing / InletVelocity;
        }

        /// <summary>
        /// Creates a memberwise clone of the settings.
        /// </summary>
        public LilyPadCfdSettings Clone()
        {
            return (LilyPadCfdSettings)this.MemberwiseClone();
        }

        #endregion
    }
}