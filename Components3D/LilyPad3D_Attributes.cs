// ========================================
// FILE: LilyPad3D_Attributes.cs
// PART 4: CUSTOM COMPONENT ATTRIBUTES
// DESC: Handles custom rendering of a "Configure" button on the component.
// --- REVISIONS ---
// - 2025-09-21 @ 09:05: Updated rendering to reflect server state.
// ========================================

using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using System.Drawing;
using System.Windows.Forms;
using GH_Graphics = System.Drawing.Graphics;
using GH_Rectangle = System.Drawing.Rectangle;

namespace LilyPadGH.Components3D
{
    public class LilyPad3DAttributes : GH_ComponentAttributes
    {
        public LilyPad3DAttributes(LilyPad3DComponent owner) : base(owner) { }

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
                if (!(Owner is LilyPad3DComponent comp)) return;

                // NOTE: Visual feedback now reflects the server's running state.
                GH_Palette palette = comp._isServerRunning ? GH_Palette.Blue : GH_Palette.Black;
                string text = comp._isServerRunning ? "Server Control..." : "Configure...";

                var button = GH_Capsule.CreateTextCapsule(ButtonBounds, ButtonBounds, palette, text);
                button.Render(graphics, Selected, Owner.Locked, false);
                button.Dispose();
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button == MouseButtons.Left && ButtonBounds.Contains(Point.Round(e.CanvasLocation)))
            {
                if (Owner is LilyPad3DComponent comp)
                {
                    comp.ShowConfigDialog();
                    return GH_ObjectResponse.Handled;
                }
            }
            return base.RespondToMouseDown(sender, e);
        }
    }
}