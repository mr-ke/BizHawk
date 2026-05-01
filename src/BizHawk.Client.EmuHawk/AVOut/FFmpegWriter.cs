using System.Collections.Concurrent;
using System.IO;
using System.Threading;

using BizHawk.Client.Common;
using BizHawk.Common.PathExtensions;
using BizHawk.Emulation.Common;

namespace BizHawk.Client.EmuHawk
{
	[VideoWriter("ffmpeg", "FFmpeg writer", "Uses FFmpeg library to encode video and audio. Various formats supported. Splits on resolution change.")]
	public class FFmpegWriter : IVideoWriter
	{
		private readonly IDialogParent _dialogParent;

		private FFmpegEncoder _encoder;

		private int _segment;
		private string _baseName;
		private FFmpegWriterForm.FormatPreset _token;
		private string _ext;

		private BlockingCollection<object> _threadQ;
		private Thread _workerT;

		private class VideoCopy : IVideoProvider
		{
			private readonly int[] _vb;
			public int VirtualWidth { get; }
			public int VirtualHeight { get; }
			public int BufferWidth { get; }
			public int BufferHeight { get; }
			public int BackgroundColor { get; }
			public int VsyncNumerator { get; }
			public int VsyncDenominator { get; }

			public VideoCopy(IVideoProvider c)
			{
				_vb = c.GetVideoBufferCopy();
				BufferWidth = c.BufferWidth;
				BufferHeight = c.BufferHeight;
				BackgroundColor = c.BackgroundColor;
				VirtualWidth = c.VirtualWidth;
				VirtualHeight = c.VirtualHeight;
				VsyncNumerator = c.VsyncNumerator;
				VsyncDenominator = c.VsyncDenominator;
			}

			public int[] GetVideoBuffer() => _vb;
		}

		public FFmpegWriter(IDialogParent dialogParent) => _dialogParent = dialogParent;

		public void SetFrame(int frame)
		{
		}

		private void ThreadProc()
		{
			try
			{
				while (true)
				{
					var o = _threadQ.Take();
					switch (o)
					{
						case IVideoProvider provider:
							AddFrameEx(provider);
							break;
						case short[] samples:
							AddSamplesEx(samples);
							break;
						default:
							return;
					}
				}
			}
			catch { }
		}

		public void OpenFile(string baseName)
		{
			var (dir, fileNoExt, ext) = baseName.SplitPathToDirFileAndExt();
			_baseName = Path.Combine(dir!, fileNoExt);
			_ext = ext;
			_segment = 0;
			OpenFileSegment();
			_threadQ = new BlockingCollection<object>(30);
			_workerT = new Thread(ThreadProc) { IsBackground = true };
			_workerT.Start();
		}

		private void OpenFileSegment()
		{
			string outputPath = $"{_baseName}{(_segment == 0 ? string.Empty : $"_{_segment}")}{_ext}";

			_encoder = new FFmpegEncoder();
			string formatName = GetFormatName(_ext);
			string videoCodec = GetVideoCodecName(_token);
			string audioCodec = GetAudioCodecName(_token);

			_encoder.Initialize(outputPath, formatName, videoCodec, audioCodec,
				_width, _height, _fpsnum, _fpsden, _sampleRate, _channels);
		}

		private static string GetFormatName(string ext)
		{
			return ext.ToLowerInvariant() switch
			{
				".mp4" => "mp4",
				".mkv" => "matroska",
				".avi" => "avi",
				".webm" => "webm",
				".mov" => "mov",
				".flv" => "flv",
				_ => "mp4",
			};
		}

		private static string GetVideoCodecName(FFmpegWriterForm.FormatPreset token)
		{
			var nameLower = token?.Name?.ToLowerInvariant() ?? "";
			return nameLower switch
			{
				"vp8" or "webm" => "libvpx",
				"vp9" => "libvpx-vp9",
				"ogg" => "libtheora",
				"xvid" => "libxvid",
				"h264" => "libx264",
				"h265" or "hevc" => "libx265",
				"av1" => "libaom-av1",
				_ when nameLower.Contains("ffv1") => "ffv1",
				_ when nameLower.Contains("ut video") => "utvideo",
				_ when nameLower.Contains("lossless avc") || nameLower.Contains("matroska lossless") => "libx264rgb",
				_ when nameLower.Contains("uncompressed") => "rawvideo",
				_ when nameLower.Contains("hevc") || nameLower.Contains("h265") => "libx265",
				_ => "libx264",
			};
		}

		private static string GetAudioCodecName(FFmpegWriterForm.FormatPreset token)
		{
			var nameLower = token?.Name?.ToLowerInvariant() ?? "";
			return nameLower switch
			{
				"vp8" or "vp9" or "webm" => "libopus",
				"av1" => "libopus",
				"ogg" => "libvorbis",
				"xvid" => "libmp3lame",
				_ when nameLower.Contains("lossless") || nameLower.Contains("uncompressed") => "pcm_s16le",
				_ when nameLower.Contains("matroska") && !nameLower.Contains("lossless") => "libvorbis",
				_ => "aac",
			};
		}

		private void CloseFileSegment()
		{
			if (_encoder != null)
			{
				_encoder.Close();
				_encoder.Dispose();
				_encoder = null;
			}
		}

		public void CloseFile()
		{
			_threadQ?.Add(new object());
			_workerT?.Join();
			CloseFileSegment();
			_baseName = null;
		}

		public void AddFrame(IVideoProvider source)
		{
			while (!_threadQ.TryAdd(new VideoCopy(source), 1000))
			{
				if (_workerT == null || !_workerT.IsAlive)
				{
					throw new Exception("FFmpeg worker thread died!");
				}
			}
		}

		private void AddFrameEx(IVideoProvider source)
		{
			if (source.BufferWidth != _width || source.BufferHeight != _height)
			{
				SetVideoParameters(source.BufferWidth, source.BufferHeight);
			}

			var video = source.GetVideoBuffer();
			_encoder.EncodeVideoFrame(video);
		}

		public IDisposable AcquireVideoCodecToken(Config config)
		{
			if (!FFmpegLibrary.QueryServiceAvailable())
			{
				using var form = new FFmpegDownloaderForm();
				_dialogParent.ShowDialogWithTempMute(form);
				if (!FFmpegLibrary.QueryServiceAvailable()) return null;
			}
			return FFmpegWriterForm.DoFFmpegWriterDlg(_dialogParent.AsWinFormsHandle(), config);
		}

		public void SetVideoCodecToken(IDisposable token)
		{
			if (token is FFmpegWriterForm.FormatPreset preset)
			{
				_token = preset;
			}
			else
			{
				throw new ArgumentException(message: $"{nameof(FFmpegWriter)} can only take its own codec tokens!", paramName: nameof(token));
			}
		}

		private int _fpsnum, _fpsden, _width, _height, _sampleRate, _channels;

		public void SetMovieParameters(int fpsNum, int fpsDen)
		{
			_fpsnum = fpsNum;
			_fpsden = fpsDen;
		}

		public void SetVideoParameters(int width, int height)
		{
			_width = width;
			_height = height;

			if (_encoder != null)
			{
				CloseFileSegment();
				_segment++;
				OpenFileSegment();
			}
		}

		public void SetMetaData(string gameName, string authors, ulong lengthMS, ulong rerecords)
		{
		}

		public void Dispose()
		{
			if (_encoder != null)
			{
				CloseFile();
			}
		}

		public void AddSamples(short[] samples)
		{
			if (samples.Length == 0)
			{
				return;
			}

			_threadQ.Add(samples);
		}

		private void AddSamplesEx(short[] samples)
		{
			_encoder.EncodeAudioFrame(samples);
		}

		public void SetAudioParameters(int sampleRate, int channels, int bits)
		{
			if (bits != 16)
			{
				throw new ArgumentOutOfRangeException(nameof(bits), "Sampling depth must be 16 bits!");
			}

			_sampleRate = sampleRate;
			_channels = channels;
		}

		public string DesiredExtension()
		{
			return _token.Extension;
		}

		public void SetDefaultVideoCodecToken(Config config)
		{
			_token = FFmpegWriterForm.FormatPreset.GetDefaultPreset(config);
		}

		public bool UsesAudio => true;

		public bool UsesVideo => true;
	}
}
