// ========================================
// FILE: LilyPad_Settings.cs
// PART 2: SETTINGS DATA MODEL WITH UI PATHS
// DESC: Holds all configuration values for the CFD analysis including real-time UI display
//       and final GIF output paths.
// --- REVISIONS ---
// - 2025-09-22: Added UIFramePath property for real-time display integration.
//   - Property allows dialog to specify where Julia should write live frames.
//   - Maintains backward compatibility with existing settings structure.
// - 2025-10-10: Added UIGifPath for final GIF display in simulation view.
//   - Julia saves final GIF to this path instead of opening external viewer.
//   - Dialog displays GIF directly in simulation view container.
// ========================================

namespace LilyPadGH.Components
{
    public class LilyPadCfdSettings
    {
        // =======================
        // Region: Flow Properties
        // =======================
        #region Flow Properties

        public double ReynoldsNumber { get; set; } = 250.0;
        public double Velocity { get; set; } = 1.0;

        #endregion

        // ===============================
        // Region: Domain & Simulation
        // ===============================
        #region Domain & Simulation

        public int GridResolutionX { get; set; } = 192;
        public int GridResolutionY { get; set; } = 128;
        public double Duration { get; set; } = 10.0; // seconds

        #endregion

        // ===========================
        // Region: Geometry Properties
        // ===========================
        #region Geometry Properties

        public int CurveDivisions { get; set; } = 50;

        #endregion

        // ==================================
        // Region: Julia Simulation Parameters
        // ==================================
        #region Julia Simulation Parameters

        public int SimulationL { get; set; } = 32; // 2^5 = 32, simulation scale
        public double SimulationU { get; set; } = 1.0; // Flow velocity scale
        public double AnimationDuration { get; set; } = 1.0; // Animation duration in seconds
        public bool PlotBody { get; set; } = true; // Show body in animation

        #endregion

        // ======================================
        // Region: Point Reduction Parameters
        // ======================================
        #region Point Reduction Parameters

        // Disabled by default for maximum accuracy
        public double SimplifyTolerance { get; set; } = 0.0; // Douglas-Peucker tolerance (0 = no simplification)
        public int MaxPointsPerPoly { get; set; } = 1000; // Maximum points per polyline (very high = no reduction)

        #endregion

        // =====================================
        // Region: Real-Time UI Display Integration
        // =====================================
        #region Real-Time UI Display Integration

        /// <summary>
        /// Path where Julia writes live PNG frames for real-time UI display during simulation.
        /// </summary>
        public string UIFramePath { get; set; } = string.Empty;

        /// <summary>
        /// Path where Julia saves the final GIF for display in the simulation view container.
        /// This prevents external viewer pop-ups and keeps everything in the UI.
        /// </summary>
        public string UIGifPath { get; set; } = string.Empty;

        #endregion

        // =======================
        // Region: Clone Method
        // =======================
        #region Clone Method

        /// <summary>
        /// Creates a memberwise clone of the settings.
        /// Simple memberwise clone is sufficient as the class only contains value types and strings.
        /// This prevents the dialog from modifying the component's state directly until committed.
        /// </summary>
        public LilyPadCfdSettings Clone()
        {
            return (LilyPadCfdSettings)this.MemberwiseClone();
        }

        #endregion
    }
}