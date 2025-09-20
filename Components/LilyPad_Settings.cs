// ========================================
// PART 1: SETTINGS DATA MODEL
// ========================================
// Defines a plain C# object to hold all configuration values for the CFD analysis.
// This decouples the component's state from its execution logic and UI.

namespace LilyPadGH.Components
{
    public class LilyPadCfdSettings
    {
        public double Duration { get; set; } = 10.0; // seconds
        public double Timestep { get; set; } = 0.1; // seconds

        // NOTE: A simple memberwise clone is sufficient for a deep copy here as the class only
        // contains value types. This prevents the dialog from modifying the component's state 
        // directly until the user commits the changes.
        public LilyPadCfdSettings Clone()
        {
            return (LilyPadCfdSettings)this.MemberwiseClone();
        }
    }
}
