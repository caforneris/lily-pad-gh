// ========================================
// FILE: LilyPad_Settings.cs
// PART 2: SETTINGS DATA MODEL WITH UI FRAME PATH
// DESC: Holds all configuration values for the CFD analysis including real-time UI display.
//       Added UI frame path for communication between dialog and Julia server.
// --- REVISIONS ---
// - 2025-09-22: Added UIFramePath property for real-time display integration.
//   - Property allows dialog to specify where Julia should write live frames.
//   - Maintains backward compatibility with existing settings structure.
// ========================================

namespace LilyPadGH.Components
{
    public class LilyPadCfdSettings
    {
        // Flow properties
        public double ReynoldsNumber { get; set; } = 250.0;
        public double Velocity { get; set; } = 1.0;

        // Domain and simulation properties
        public int GridResolutionX { get; set; } = 192;
        public int GridResolutionY { get; set; } = 128;
        public double Duration { get; set; } = 10.0; // seconds

        // Geometry properties
        public int CurveDivisions { get; set; } = 50;

        // Julia simulation parameters
        public int SimulationL { get; set; } = 32; // 2^5 = 32, simulation scale
        public double SimulationU { get; set; } = 1.0; // Flow velocity scale
        public double AnimationDuration { get; set; } = 1.0; // Animation duration in seconds
        public bool PlotBody { get; set; } = true; // Show body in animation

        // Julia point reduction parameters (disabled by default)
        public double SimplifyTolerance { get; set; } = 0.0; // Douglas-Peucker tolerance (0 = no simplification)
        public int MaxPointsPerPoly { get; set; } = 1000; // Maximum points per polyline (very high = no reduction)

        // Real-time UI display integration
        public string UIFramePath { get; set; } = string.Empty; // Path where Julia writes live frames for UI display

        // NOTE: Simple memberwise clone is sufficient for a deep copy here as the class only
        // contains value types and strings. This prevents the dialog from modifying the component's 
        // state directly until the user commits the changes.
        public LilyPadCfdSettings Clone()
        {
            return (LilyPadCfdSettings)this.MemberwiseClone();
        }
    }
}