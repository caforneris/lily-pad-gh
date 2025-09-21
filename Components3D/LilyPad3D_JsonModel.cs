// ========================================
// FILE: LilyPad3D_JsonModel.cs
// PART 4: JSON DATA MODEL
// DESC: Contains all Plain Old C# Objects (POCOs) with DataContract attributes,
//       defining the structure of the output JSON file for the 3D Julia script.
// --- REVISIONS ---
// - 2025-09-21 @ 09:00: Created to match julietta.jl specification.
// ========================================

using System.Runtime.Serialization;

namespace LilyPadGH.Components3D
{
    [DataContract]
    public class DomainSpec3D { [DataMember] public double[] min; [DataMember] public double[] max; [DataMember] public int[] resolution; [DataMember] public string units; [DataMember] public string axis_order; }
    [DataContract]
    public class TimeSpec3D { [DataMember] public double dt; [DataMember] public double duration; [DataMember(EmitDefaultValue = false)] public int warmup_steps; }
    [DataContract]
    public class ForcingSpec3D { [DataMember] public string type; [DataMember] public double[] velocity; }
    [DataContract]
    public class FluidSpec3D { [DataMember] public double nu; [DataMember] public double rho; [DataMember] public ForcingSpec3D forcing; }
    [DataContract]
    public class ObstaclesSpec3D { [DataMember] public string type; [DataMember] public int[] shape; [DataMember] public string data; }
    [DataContract]
    public class RootSpec3D { [DataMember] public string version; [DataMember] public DomainSpec3D domain; [DataMember] public TimeSpec3D time; [DataMember] public FluidSpec3D fluid; [DataMember] public ObstaclesSpec3D obstacles; [DataMember(EmitDefaultValue = false)] public string notes; }
}