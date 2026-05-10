using System.Drawing;
using System.Numerics;

namespace BizHawk.Bizware.Graphics
{
	public sealed class IGL_Bgfx : IGL
	{
		private readonly IGL_GDIPlus _gdiPlus = new();

		public EDispMethod DispMethodEnum => EDispMethod.Bgfx;

		public int MaxTextureDimension => _gdiPlus.MaxTextureDimension;

		internal IGL_GDIPlus GdiPlus => _gdiPlus;

		public void Dispose()
		{
			_gdiPlus.Dispose();
		}

		public void ClearColor(Color color)
			=> _gdiPlus.ClearColor(color);

		public void EnableBlending()
			=> _gdiPlus.EnableBlending();

		public void DisableBlending()
			=> _gdiPlus.DisableBlending();

		public IPipeline CreatePipeline(PipelineCompileArgs compileArgs)
			=> _gdiPlus.CreatePipeline(compileArgs);

		public void BindPipeline(IPipeline pipeline)
			=> _gdiPlus.BindPipeline(pipeline);

		public void Draw(int vertexCount)
			=> _gdiPlus.Draw(vertexCount);

		public void DrawIndexed(int indexCount, int indexStart, int vertexStart)
			=> _gdiPlus.DrawIndexed(indexCount, indexStart, vertexStart);

		public ITexture2D CreateTexture(int width, int height)
			=> _gdiPlus.CreateTexture(width, height);

		public ITexture2D WrapGLTexture2D(int glTexId, int width, int height)
			=> _gdiPlus.WrapGLTexture2D(glTexId, width, height);

		public Matrix4x4 CreateGuiProjectionMatrix(int width, int height)
			=> _gdiPlus.CreateGuiProjectionMatrix(width, height);

		public Matrix4x4 CreateGuiViewMatrix(int width, int height, bool autoFlip = true)
			=> _gdiPlus.CreateGuiViewMatrix(width, height, autoFlip);

		public void SetViewport(int x, int y, int width, int height)
			=> _gdiPlus.SetViewport(x, y, width, height);

		public IRenderTarget CreateRenderTarget(int width, int height)
			=> _gdiPlus.CreateRenderTarget(width, height);

		public void BindDefaultRenderTarget()
			=> _gdiPlus.BindDefaultRenderTarget();
	}
}
