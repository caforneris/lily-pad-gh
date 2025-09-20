using Grasshopper.Kernel;
using System;
using System.Drawing;

namespace LilyPadGH
{
    public class LilyPadGHInfo : GH_AssemblyInfo
    {
        public override string Name => "LilyPadGH";
        public override Bitmap Icon => null;
        public override string Description => "LilyPadGH - Fluid dynamics simulation for Grasshopper";
        public override Guid Id => new Guid("a8f5c2d1-7b3e-4f92-9d6a-e2c8b5f3a9d7");
        public override string AuthorName => "Your Name";
        public override string AuthorContact => "your.email@example.com";
    }
}