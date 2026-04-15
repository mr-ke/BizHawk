using System.IO;

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

		public FFmpegWriter(IDialogParent dialogParent) => _dialogParent = dialogParent;

		public void SetFrame(int frame)
		{
		}

		public void OpenFile(string baseName)
		{
			var (dir, fileNoExt, ext) = baseName.SplitPathToDirFileAndExt();
			_baseName = Path.Combine(dir!, fileNoExt);
			_ext = ext;
			_segment = 0;
			OpenFileSegment();
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
			return token?.Name?.ToLowerInvariant() switch
			{
				"vp8" => "libvpx",
				"vp9" => "libvpx-vp9",
				"h264" => "libx264",
				"h265" or "hevc" => "libx265",
				"av1" => "libaom-av1",
				_ => "libx264",
			};
		}

		private static string GetAudioCodecName(FFmpegWriterForm.FormatPreset token)
		{
			return token?.Name?.ToLowerInvariant() switch
			{
				"vp8" or "vp9" or "webm" => "libopus",
				"av1" => "libopus",
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
			CloseFileSegment();
			_baseName = null;
		}

		public void AddFrame(IVideoProvider source)
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
