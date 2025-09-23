// ========================================
// FILE: LilyPad_Attributes.cs
// PART 4: CUSTOM COMPONENT ATTRIBUTES
// DESC: Handles custom rendering and UI interaction for the component on the canvas.
// --- REVISIONS ---
// - 2025-09-22: Resolved compiler error.
//   - Replaced reference to obsolete `_isRunning` flag with `_isServerRunning`.
//     The component's button will now appear blue when the Julia server is active.
// ========================================

using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using System.Drawing;
using System.Windows.Forms;

// Resolve namespace conflicts
using GH_Graphics = System.Drawing.Graphics;
using GH_Rectangle = System.Drawing.Rectangle;

namespace LilyPadGH.Components
{
    public class LilyPadCfdAttributes : GH_ComponentAttributes
    {
        public LilyPadCfdAttributes(LilyPadCfdComponent owner) : base(owner) { }

        private GH_Rectangle ButtonBounds { get; set; }

        protected override void Layout()
        {
            base.Layout();

            GH_Rectangle rec0 = GH_Convert.ToRectangle(Bounds);
            rec0.Height += 30;

            GH_Rectangle rec1 = rec0;
            rec1.Y = rec0.Bottom - 25;
            rec1.Height = 20;
            rec1.Inflate(-2, 0);

            Bounds = rec0;
            ButtonBounds = rec1;
        }

        protected override void Render(GH_Canvas canvas, GH_Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel == GH_CanvasChannel.Objects)
            {
                if (!(Owner is LilyPadCfdComponent comp)) return;

                // NOTE: The visual state of the button now reflects whether the Julia server
                // is running, which is more relevant than the old simulation task state.
                GH_Palette palette = comp._isServerRunning ? GH_Palette.Blue : GH_Palette.Black;
                string text = "Configure & Run";

                var button = GH_Capsule.CreateTextCapsule(ButtonBounds, ButtonBounds, palette, text);
                button.Render(graphics, Selected, Owner.Locked, false);
                button.Dispose();
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button == MouseButtons.Left && ButtonBounds.Contains(Point.Round(e.CanvasLocation)))
            {
                if (Owner is LilyPadCfdComponent comp)
                {
                    comp.ShowConfigDialog();
                }
                return GH_ObjectResponse.Handled;
            }
            return base.RespondToMouseDown(sender, e);
        }
    }
}