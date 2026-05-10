using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

using Bgfx;
using BizHawk.Bizware.Graphics;
using BizHawk.Client.Common;
using BizHawk.Client.Common.Bgfx;
using BizHawk.Common.PathExtensions;
using BizHawk.Emulation.Common;

namespace BizHawk.Client.EmuHawk
{
	public sealed class BgfxDisplayManager : IDisposable
	{
		private Config _config;
		private IEmulator _emulator;
		private readonly PresentationPanel _presentationPanel;

		private BgfxPipeline _pipeline;
		private bool _bgfxInitialized;
		private bool _disposed;

		private bgfx.TextureHandle _sourceTexture;
		private bgfx.ProgramHandle _blitProgram;

		private int _textureWidth;
		private int _textureHeight;
		private int _lastWindowWidth;
		private int _lastWindowHeight;

		public OSDManager OSD { get; }

		public (int Left, int Top, int Right, int Bottom) GameExtraPadding { get; set; }
		public (int Left, int Top, int Right, int Bottom) ClientExtraPadding { get; set; }

		private static readonly string DefaultPresetPath =
			Path.Combine(PathUtils.ExeDirectoryPath, "Bgfx", "chains", "crt-lottes-vhs.json");

		public BgfxDisplayManager(
			Config config,
			IEmulator emulator,
			InputManager inputManager,
			IMovieSession movieSession,
			PresentationPanel presentationPanel)
		{
			_config = config;
			_emulator = emulator;
			_presentationPanel = presentationPanel;
			OSD = new(config, emulator, inputManager, movieSession);
		}

		public unsafe void InitializeBgfx()
		{
			if (_bgfxInitialized)
				return;

			var control = _presentationPanel.GraphicsControl;
			control.CreateControl();

			bgfx.Init init;
			bgfx.init_ctor(&init);

			init.type = bgfx.RendererType.Direct3D11;
			init.platformData.nwh = control.Handle.ToPointer();
			init.resolution.width = (uint)Math.Max(control.ClientSize.Width, 1);
			init.resolution.height = (uint)Math.Max(control.ClientSize.Height, 1);
			init.resolution.reset = (uint)bgfx.ResetFlags.Vsync;
			init.resolution.formatColor = bgfx.TextureFormat.BGRA8;

			if (!bgfx.init(&init))
				throw new InvalidOperationException("Failed to initialize bgfx");

			bgfx.set_debug(0);
			bgfx.set_view_clear(0, (ushort)(bgfx.ClearFlags.Color | bgfx.ClearFlags.Depth), 0x000000ff, 1.0f, 0);

			_pipeline = new BgfxPipeline(PathUtils.ExeDirectoryPath);
			_pipeline.Initialize();

			if (!CreateBlitProgram())
				throw new InvalidOperationException("Failed to create bgfx blit program");

			_pipeline.SetBlitProgram(_blitProgram);

			_sourceTexture = new bgfx.TextureHandle { idx = ushort.MaxValue };
			_bgfxInitialized = true;

			_lastWindowWidth = 0;
			_lastWindowHeight = 0;

			TryLoadDefaultPreset();
		}

		private void TryLoadDefaultPreset()
		{
			if (_config.TargetDisplayFilter == 0)
			{
				_pipeline.ClearChain();
				return;
			}

			string configPath = _config.DispBgfxShaderPath;
			if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
			{
				_pipeline.LoadChain(configPath);
			}
			else if (File.Exists(DefaultPresetPath))
			{
				_pipeline.LoadChain(DefaultPresetPath);
			}
		}

		private unsafe bool CreateBlitProgram()
		{
			string exeDir = PathUtils.ExeDirectoryPath;
			string[] searchDirs = {
				Path.Combine(exeDir, "Bgfx", "shaders", "dx11", "chains", "default"),
				Path.Combine(exeDir, "Bgfx", "shaders", "dx11", "chains", "unfiltered"),
			};

			string shaderDir = null;
			foreach (var dir in searchDirs)
			{
				if (Directory.Exists(dir))
				{
					shaderDir = dir;
					break;
				}
			}

			if (shaderDir == null)
				return false;

			string vsPath = Path.Combine(shaderDir, "vs_blit.bin");
			string fsPath = Path.Combine(shaderDir, "fs_blit.bin");

			if (!File.Exists(vsPath) || !File.Exists(fsPath))
				return false;

			var vsh = LoadShader(vsPath);
			var fsh = LoadShader(fsPath);
			if (vsh.idx == ushort.MaxValue || fsh.idx == ushort.MaxValue)
				return false;

			_blitProgram = bgfx.create_program(vsh, fsh, true);
			return _blitProgram.idx != ushort.MaxValue;
		}

		private unsafe bgfx.ShaderHandle LoadShader(string path)
		{
			byte[] shaderData = File.ReadAllBytes(path);
			fixed (byte* pData = shaderData)
			{
				bgfx.Memory* mem = bgfx.copy(pData, (uint)shaderData.Length);
				return bgfx.create_shader(mem);
			}
		}

		public unsafe void UpdateSource(IVideoProvider videoProvider, bool useSnow = false)
		{
			if (!_bgfxInitialized)
				return;

			var control = _presentationPanel.GraphicsControl;
			var clientSize = control.ClientSize;

			if (clientSize.Width <= 0 || clientSize.Height <= 0)
				return;

			if (clientSize.Width != _lastWindowWidth || clientSize.Height != _lastWindowHeight)
			{
				HandleResize(clientSize.Width, clientSize.Height);
				_lastWindowWidth = clientSize.Width;
				_lastWindowHeight = clientSize.Height;
			}

			UploadVideoTexture(videoProvider);

			_pipeline.Render(clientSize.Width, clientSize.Height);
		}

		private unsafe void UploadVideoTexture(IVideoProvider videoProvider)
		{
			int bufferWidth = videoProvider.BufferWidth;
			int bufferHeight = videoProvider.BufferHeight;
			int[] videoBuffer = videoProvider.GetVideoBuffer();

			if (bufferWidth <= 0 || bufferHeight <= 0 || videoBuffer == null || videoBuffer.Length == 0)
				return;

			bool needsNewTexture = _sourceTexture.idx == ushort.MaxValue
				|| _textureWidth != bufferWidth
				|| _textureHeight != bufferHeight;

			var rgbaData = ConvertVideoBufferToRgba(videoBuffer, bufferWidth, bufferHeight);

			if (needsNewTexture)
			{
				if (_sourceTexture.idx != ushort.MaxValue)
				{
					bgfx.destroy_texture(_sourceTexture);
				}

				_sourceTexture = bgfx.create_texture_2d(
					(ushort)bufferWidth,
					(ushort)bufferHeight,
					false,
					1,
					bgfx.TextureFormat.RGBA8,
					0,
					null,
					0);

				_textureWidth = bufferWidth;
				_textureHeight = bufferHeight;
			}

			var handle = GCHandle.Alloc(rgbaData, GCHandleType.Pinned);
			try
			{
				bgfx.Memory* mem = bgfx.copy(handle.AddrOfPinnedObject().ToPointer(), (uint)rgbaData.Length);
				bgfx.update_texture_2d(_sourceTexture, 0, 0, 0, 0, (ushort)bufferWidth, (ushort)bufferHeight, mem, ushort.MaxValue);
			}
			finally
			{
				handle.Free();
			}

			_pipeline.SetSourceTexture(_sourceTexture, bufferWidth, bufferHeight);
		}

		private static byte[] ConvertVideoBufferToRgba(int[] videoBuffer, int width, int height)
		{
			var data = new byte[width * height * 4];
			for (int y = 0; y < height; y++)
			{
				int srcRow = (height - 1 - y) * width;
				for (int x = 0; x < width; x++)
				{
					int pixel = videoBuffer[srcRow + x];
					int dstIdx = (y * width + x) * 4;
					data[dstIdx + 0] = (byte)((pixel >> 16) & 0xFF);
					data[dstIdx + 1] = (byte)((pixel >> 8) & 0xFF);
					data[dstIdx + 2] = (byte)(pixel & 0xFF);
					data[dstIdx + 3] = (byte)((pixel >> 24) & 0xFF);
				}
			}
			return data;
		}

		public unsafe void Blank()
		{
			if (!_bgfxInitialized) return;

			var control = _presentationPanel.GraphicsControl;
			var clientSize = control.ClientSize;
			int w = Math.Max(clientSize.Width, 1);
			int h = Math.Max(clientSize.Height, 1);

			bgfx.set_view_rect(0, 0, 0, (ushort)w, (ushort)h);
			bgfx.set_view_clear(0, (ushort)(bgfx.ClearFlags.Color | bgfx.ClearFlags.Depth), 0x000000ff, 1.0f, 0);
			bgfx.touch(0);
			_ = bgfx.frame(0);
		}

		public void ActivateOpenGLContext()
		{
		}

		public Point UntransformPoint(Point p)
		{
			return p;
		}

		public Point TransformPoint(Point p)
		{
			return p;
		}

		public Size GetPanelNativeSize() => _presentationPanel.NativeSize;

		public Size CalculateClientSize(IVideoProvider videoProvider, int zoom)
		{
			int w = videoProvider.BufferWidth * zoom;
			int h = videoProvider.BufferHeight * zoom;
			return new Size(w, h);
		}

		public BitmapBuffer RenderOffscreen(IVideoProvider videoProvider, bool includeOSD)
		{
			return new BitmapBuffer(videoProvider.BufferWidth, videoProvider.BufferHeight, videoProvider.GetVideoBuffer());
		}

		public BitmapBuffer RenderOffscreenLua(IVideoProvider videoProvider)
		{
			return new BitmapBuffer(videoProvider.BufferWidth, videoProvider.BufferHeight, videoProvider.GetVideoBuffer());
		}

		public void UpdateGlobals(Config config, IEmulator emulator)
		{
			_config = config;
			_emulator = emulator;
			OSD.UpdateGlobals(config, emulator);
		}

		public void DiscardApiHawkSurfaces()
		{
		}

		public void RefreshUserShader()
		{
			if (_pipeline == null || !_bgfxInitialized) return;
			TryLoadDefaultPreset();
		}

		public bool LoadChainPreset(string presetPath)
		{
			if (_pipeline == null || !_bgfxInitialized) return false;
			return _pipeline.LoadChain(presetPath);
		}

		public unsafe void HandleResize(int width, int height)
		{
			if (!_bgfxInitialized) return;
			bgfx.reset((uint)width, (uint)height, (uint)bgfx.ResetFlags.Vsync, bgfx.TextureFormat.BGRA8);
		}

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;

			if (_bgfxInitialized)
			{
				_pipeline?.Dispose();

				if (_sourceTexture.idx != ushort.MaxValue)
					bgfx.destroy_texture(_sourceTexture);

				if (_blitProgram.idx != ushort.MaxValue)
					bgfx.destroy_program(_blitProgram);

				bgfx.shutdown();
			}
		}
	}
}
