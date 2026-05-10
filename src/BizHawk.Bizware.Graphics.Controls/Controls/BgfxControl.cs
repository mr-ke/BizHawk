using System.Windows.Forms;

namespace BizHawk.Bizware.Graphics.Controls
{
	internal sealed class BgfxControl : GraphicsControl
	{
		public BgfxControl()
		{
			SetStyle(ControlStyles.Opaque, true);
			SetStyle(ControlStyles.UserPaint, true);
			SetStyle(ControlStyles.AllPaintingInWmPaint, true);
			SetStyle(ControlStyles.UserMouse, true);
			DoubleBuffered = false;
		}

		public override void AllowTearing(bool state)
		{
		}

		public override void SetVsync(bool state)
		{
		}

		public override void Begin()
		{
		}

		public override void End()
		{
		}

		public override void SwapBuffers()
		{
		}

		protected override void OnPaint(PaintEventArgs e)
		{
		}
	}
}
