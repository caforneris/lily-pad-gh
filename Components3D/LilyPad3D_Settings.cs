// ========================================
// FILE: LilyPad3D_Settings.cs
// PART 2: SETTINGS DATA MODEL
// DESC: Data model for storing all non-geometric simulation parameters for LilyPad3D.
// --- REVISIONS ---
// - 2025-09-21 @ 09:05: Aligned settings with the 3D server architecture.
// ========================================

using Rhino.Geometry;

namespace LilyPadGH.Components3D
{
    public class LilyPad3DSettings
    {
        public Vector3d Resolution { get; set; } = new Vector3d(64, 64, 64);
        public double Timestep { get; set; } = 0.05; // Corresponds to viz cadence
        public double Duration { get; set; } = 5.0;
        public double Viscosity { get; set; } = 0.001;
        public Vector3d Velocity { get; set; } = new Vector3d(0.1, 0, 0);
        public string FilePath { get; set; } = "C:\\temp\\lilypad3d_sim.json";

        public LilyPad3DSettings Clone() => (LilyPad3DSettings)this.MemberwiseClone();
    }
}