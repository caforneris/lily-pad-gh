// ========================================
// FILE: LilyPad_Settings.cs
// PART 2: SETTINGS DATA MODEL
// DESC: Holds all configuration values for the CFD analysis. This decouples
//       the component's state from its execution logic and UI.
//       Updated to include
//       all simulation parameters from the Eto dialog.
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


        // NOTE: Simple memberwise clone is sufficient for a deep copy here as the class only
        // contains value types. This prevents the dialog from modifying the component's state
        // directly until the user commits the changes.
        public LilyPadCfdSettings Clone()
        {
            return (LilyPadCfdSettings)this.MemberwiseClone();
        }
    }
}