// ========================================
// FILE: LilyPad_Attributes.cs
// PART 4: CUSTOM COMPONENT ATTRIBUTES
// DESC: Handles custom rendering and UI interaction for the component on the canvas.
//       This keeps all canvas-specific drawing logic separate from the
//       component's core logic.
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

        // Store the button bounds separate from the main component bounds
        private GH_Rectangle ButtonBounds { get; set; }

        // Extend the component layout to make space for our custom button
        protected override void Layout()
        {
            base.Layout();

            GH_Rectangle rec0 = GH_Convert.ToRectangle(Bounds);
            rec0.Height += 30; // Add 30px of space at the bottom

            // Define the button area within that new space
            GH_Rectangle rec1 = rec0;
            rec1.Y = rec0.Bottom - 25;
            rec1.Height = 20;
            rec1.Inflate(-2, 0); // Give it a little horizontal padding

            Bounds = rec0;
            ButtonBounds = rec1;
        }

        // Custom rendering for the configuration button
        protected override void Render(GH_Canvas canvas, GH_Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            // Only render our custom UI on the 'Objects' channel
            if (channel == GH_CanvasChannel.Objects)
            {
                if (!(Owner is LilyPadCfdComponent comp)) return;

                // Visual feedback based on running state
                GH_Palette palette = comp._isRunning ? GH_Palette.Blue : GH_Palette.Black;
                string text = "Configure & Run";

                var button = GH_Capsule.CreateTextCapsule(ButtonBounds, ButtonBounds, palette, text);
                button.Render(graphics, Selected, Owner.Locked, false);
                button.Dispose();
            }
        }

        // Handle button clicks to show the dialog
        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button == MouseButtons.Left && ButtonBounds.Contains(Point.Round(e.CanvasLocation)))
            {
                if (Owner is LilyPadCfdComponent comp)
                {
                    comp.ShowConfigDialog();
                }
                return GH_ObjectResponse.Handled; // Absorb the click event
            }
            return base.RespondToMouseDown(sender, e);
        }
    }
}