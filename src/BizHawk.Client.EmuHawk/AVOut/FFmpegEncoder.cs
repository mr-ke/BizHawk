using System.Collections.Generic;
using System.Runtime.InteropServices;

using FFmpeg.AutoGen;

namespace BizHawk.Client.EmuHawk
{
	public unsafe class FFmpegEncoder : IDisposable
	{
		private AVFormatContext* _formatContext;
		private AVCodecContext* _videoCodecContext;
		private AVCodecContext* _audioCodecContext;
		private AVStream* _videoStream;
		private AVStream* _audioStream;
		private AVFrame* _videoFrame;
		private AVFrame* _audioFrame;
		private AVPacket* _packet;
		private SwsContext* _swsContext;
		private SwrContext* _swrContext;
		private int _swsContextWidth;
		private int _swsContextHeight;

		private int _width;
		private int _height;
		private int _fpsNum;
		private int _fpsDen;
		private int _sampleRate;
		private int _channels;
		private int _crf;
		private string _preset;
		private string _videoCodecName;
		private long _videoPts;
		private long _audioPts;

		private List<short> _audioBuffer = new List<short>();
		private int _audioFrameSize;

		private bool _isInitialized;
		private bool _disposed;

		private const int AVERROR_EOF = -0x20;

		public bool IsInitialized => _isInitialized;

		public void Initialize(string outputPath, string formatName,
			string videoCodecName, string audioCodecName,
			int width, int height, int fpsNum, int fpsDen,
			int sampleRate, int channels,
			int crf = 23, string preset = "medium")
		{
			if (_isInitialized)
				throw new InvalidOperationException("Encoder already initialized");

			FFmpegLibrary.Initialize();

			_width = width;
			_height = height;
			_fpsNum = fpsNum;
			_fpsDen = fpsDen;
			_sampleRate = sampleRate;
			_channels = channels;
			_crf = crf;
			_preset = preset;
			_videoCodecName = videoCodecName;

			int ret;
			AVFormatContext* formatContext = null;
			ret = ffmpeg.avformat_alloc_output_context2(&formatContext, null, null, outputPath);
			if (ret < 0 || formatContext == null)
			{
				throw new InvalidOperationException($"Failed to allocate output context: {GetErrorMessage(ret)}");
			}
			_formatContext = formatContext;

			SetupVideoEncoder(videoCodecName);
			SetupAudioEncoder(audioCodecName);

			if ((_formatContext->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
			{
				ret = ffmpeg.avio_open(&_formatContext->pb, outputPath, ffmpeg.AVIO_FLAG_WRITE);
				if (ret < 0)
				{
					throw new InvalidOperationException($"Failed to open output file: {GetErrorMessage(ret)}");
				}
			}

			ret = ffmpeg.avformat_write_header(_formatContext, null);
			if (ret < 0)
			{
				throw new InvalidOperationException($"Failed to write header: {GetErrorMessage(ret)}");
			}

			_videoPts = 0;
			_audioPts = 0;
			_isInitialized = true;
		}

		private void SetupVideoEncoder(string codecName)
		{
			var codec = ffmpeg.avcodec_find_encoder_by_name(codecName);
			if (codec == null)
			{
				codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_H264);
			}
			if (codec == null)
			{
				throw new InvalidOperationException($"Video codec '{codecName}' not found");
			}

			_videoStream = ffmpeg.avformat_new_stream(_formatContext, codec);
			if (_videoStream == null)
			{
				throw new InvalidOperationException("Failed to create video stream");
			}

			_videoCodecContext = ffmpeg.avcodec_alloc_context3(codec);
			if (_videoCodecContext == null)
			{
				throw new InvalidOperationException("Failed to allocate video codec context");
			}

			_videoCodecContext->codec_id = codec->id;
			_videoCodecContext->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
			_videoCodecContext->width = _width;
			_videoCodecContext->height = _height;
			_videoCodecContext->time_base = new AVRational { num = _fpsDen, den = _fpsNum };
			_videoCodecContext->framerate = new AVRational { num = _fpsNum, den = _fpsDen };
			_videoCodecContext->pix_fmt = GetPixelFormat(_videoCodecName);
			_videoCodecContext->gop_size = 12;

			if (!IsRgbPixelFormat(_videoCodecContext->pix_fmt))
			{
				_videoCodecContext->max_b_frames = 1;
				_videoCodecContext->color_range = AVColorRange.AVCOL_RANGE_JPEG;
				_videoCodecContext->colorspace = AVColorSpace.AVCOL_SPC_BT709;
				_videoCodecContext->color_primaries = AVColorPrimaries.AVCOL_PRI_BT709;
				_videoCodecContext->color_trc = AVColorTransferCharacteristic.AVCOL_TRC_BT709;
			}

			if ((_formatContext->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
			{
				_videoCodecContext->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
			}

			AVDictionary* opts = null;
			int ret;
			try
			{
				if (codec->id == AVCodecID.AV_CODEC_ID_VP8 || codec->id == AVCodecID.AV_CODEC_ID_VP9)
				{
					ffmpeg.av_dict_set(&opts, "deadline", "realtime", 0);
					ffmpeg.av_dict_set(&opts, "cpu-used", "4", 0);
					var vpCrf = Math.Min(_crf, 15);
					ffmpeg.av_dict_set(&opts, "crf", vpCrf.ToString(), 0);
				}
				else if (_videoCodecName == "libx264rgb")
				{
					ffmpeg.av_dict_set(&opts, "qp", "0", 0);
					ffmpeg.av_dict_set(&opts, "preset", "ultrafast", 0);
				}
				else if (_videoCodecName == "libxvid")
				{
					_videoCodecContext->bit_rate = 4000000;
				}
				else
				{
					ffmpeg.av_dict_set(&opts, "crf", _crf.ToString(), 0);
					ffmpeg.av_dict_set(&opts, "preset", _preset, 0);
				}

				if (codec->id == AVCodecID.AV_CODEC_ID_HEVC)
				{
					ffmpeg.av_dict_set(&opts, "x265-params", $"crf={_crf}", 0);
				}

				ret = ffmpeg.avcodec_open2(_videoCodecContext, codec, &opts);
				if (ret < 0)
				{
					throw new InvalidOperationException($"Failed to open video codec: {GetErrorMessage(ret)}");
				}
			}
			finally
			{
				ffmpeg.av_dict_free(&opts);
			}

			ret = ffmpeg.avcodec_parameters_from_context(_videoStream->codecpar, _videoCodecContext);
			if (ret < 0)
			{
				throw new InvalidOperationException($"Failed to copy codec parameters: {GetErrorMessage(ret)}");
			}
			_videoStream->time_base = _videoCodecContext->time_base;

			_videoFrame = ffmpeg.av_frame_alloc();
			if (_videoFrame == null)
			{
				throw new InvalidOperationException("Failed to allocate video frame");
			}

			_videoFrame->format = (int)_videoCodecContext->pix_fmt;
			_videoFrame->width = _width;
			_videoFrame->height = _height;

			ret = ffmpeg.av_frame_get_buffer(_videoFrame, 0);
			if (ret < 0)
			{
				throw new InvalidOperationException($"Failed to allocate video frame buffer: {GetErrorMessage(ret)}");
			}

			_swsContextWidth = _width;
			_swsContextHeight = _height;
		}

		private void EnsureSwsContext()
		{
			if (_swsContext != null) return;

			var srcFormat = AVPixelFormat.AV_PIX_FMT_BGRA;

			_swsContext = ffmpeg.sws_getContext(
				_width, _height, srcFormat,
				_width, _height, _videoCodecContext->pix_fmt,
				2, null, null, null);

			if (_swsContext == null)
			{
				throw new InvalidOperationException("Failed to create sws context");
			}
		}

		private void SetupAudioEncoder(string codecName)
		{
			var codec = ffmpeg.avcodec_find_encoder_by_name(codecName);
			if (codec == null)
			{
				codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_AAC);
			}
			if (codec == null)
			{
				throw new InvalidOperationException($"Audio codec '{codecName}' not found");
			}

			_audioStream = ffmpeg.avformat_new_stream(_formatContext, codec);
			if (_audioStream == null)
			{
				throw new InvalidOperationException("Failed to create audio stream");
			}

			_audioCodecContext = ffmpeg.avcodec_alloc_context3(codec);
			if (_audioCodecContext == null)
			{
				throw new InvalidOperationException("Failed to allocate audio codec context");
			}

			_audioCodecContext->codec_id = codec->id;
			_audioCodecContext->codec_type = AVMediaType.AVMEDIA_TYPE_AUDIO;
			_audioCodecContext->sample_rate = GetSupportedSampleRate(codec, _sampleRate);
			_audioCodecContext->sample_fmt = GetSupportedSampleFormat(codec);
			_audioCodecContext->time_base = new AVRational { num = 1, den = _audioCodecContext->sample_rate };

			if (codec->id == AVCodecID.AV_CODEC_ID_OPUS)
			{
				_audioCodecContext->bit_rate = 128000;
			}
			else if (codec->id == AVCodecID.AV_CODEC_ID_AAC)
			{
				_audioCodecContext->bit_rate = 192000;
			}
			else if (codec->id == AVCodecID.AV_CODEC_ID_VORBIS)
			{
				_audioCodecContext->bit_rate = 160000;
			}
			else if (codec->id == AVCodecID.AV_CODEC_ID_MP3)
			{
				_audioCodecContext->bit_rate = 192000;
			}

			var chLayout = default(AVChannelLayout);
			ffmpeg.av_channel_layout_default(&chLayout, _channels);
			_audioCodecContext->ch_layout = chLayout;

			if ((_formatContext->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
			{
				_audioCodecContext->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
			}

			int ret = ffmpeg.avcodec_open2(_audioCodecContext, codec, null);
			if (ret < 0)
			{
				throw new InvalidOperationException($"Failed to open audio codec: {GetErrorMessage(ret)}");
			}

			ret = ffmpeg.avcodec_parameters_from_context(_audioStream->codecpar, _audioCodecContext);
			if (ret < 0)
			{
				throw new InvalidOperationException($"Failed to copy audio codec parameters: {GetErrorMessage(ret)}");
			}
			_audioStream->time_base = _audioCodecContext->time_base;

			_audioFrameSize = _audioCodecContext->frame_size;
			if (_audioFrameSize <= 0)
			{
				_audioFrameSize = 1024;
			}

			_audioFrame = ffmpeg.av_frame_alloc();
			if (_audioFrame == null)
			{
				throw new InvalidOperationException("Failed to allocate audio frame");
			}

			_audioFrame->format = (int)_audioCodecContext->sample_fmt;
			_audioFrame->nb_samples = _audioFrameSize;
			_audioFrame->ch_layout = _audioCodecContext->ch_layout;
			_audioFrame->sample_rate = _audioCodecContext->sample_rate;

			ret = ffmpeg.av_frame_get_buffer(_audioFrame, 0);
			if (ret < 0)
			{
				throw new InvalidOperationException($"Failed to allocate audio frame buffer: {GetErrorMessage(ret)}");
			}

			_swrContext = ffmpeg.swr_alloc();
			if (_swrContext == null)
			{
				throw new InvalidOperationException("Failed to allocate swr context");
			}

			var inLayout = default(AVChannelLayout);
			ffmpeg.av_channel_layout_default(&inLayout, _channels);
			var outLayout = _audioCodecContext->ch_layout;

			ffmpeg.av_opt_set_chlayout(_swrContext, "in_chlayout", &inLayout, 0);
			ffmpeg.av_opt_set_chlayout(_swrContext, "out_chlayout", &outLayout, 0);
			ffmpeg.av_opt_set_int(_swrContext, "in_sample_rate", _sampleRate, 0);
			ffmpeg.av_opt_set_int(_swrContext, "out_sample_rate", _audioCodecContext->sample_rate, 0);
			ffmpeg.av_opt_set_sample_fmt(_swrContext, "in_sample_fmt", AVSampleFormat.AV_SAMPLE_FMT_S16, 0);
			ffmpeg.av_opt_set_sample_fmt(_swrContext, "out_sample_fmt", _audioCodecContext->sample_fmt, 0);

			ret = ffmpeg.swr_init(_swrContext);
			if (ret < 0)
			{
				throw new InvalidOperationException($"Failed to initialize swr context: {GetErrorMessage(ret)}");
			}

			_packet = ffmpeg.av_packet_alloc();
			if (_packet == null)
			{
				throw new InvalidOperationException("Failed to allocate packet");
			}
		}

		public void EncodeVideoFrame(int[] frameData)
		{
			if (!_isInitialized)
				throw new InvalidOperationException("Encoder not initialized");

			EnsureSwsContext();

			int ret = ffmpeg.av_frame_make_writable(_videoFrame);
			if (ret < 0)
			{
				throw new InvalidOperationException($"Failed to make video frame writable: {GetErrorMessage(ret)}");
			}

			GCHandle frameDataHandle = GCHandle.Alloc(frameData, GCHandleType.Pinned);
			try
			{
				byte* srcData = (byte*)frameDataHandle.AddrOfPinnedObject();

				AVFrame* srcFrame = ffmpeg.av_frame_alloc();
				if (srcFrame == null)
				{
					throw new InvalidOperationException("Failed to allocate source frame");
				}

				try
				{
					srcFrame->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
					srcFrame->width = _width;
					srcFrame->height = _height;

					ret = ffmpeg.av_frame_get_buffer(srcFrame, 0);
					if (ret < 0)
					{
						throw new InvalidOperationException($"Failed to allocate source frame buffer: {GetErrorMessage(ret)}");
					}

					ret = ffmpeg.av_frame_make_writable(srcFrame);
					if (ret < 0)
					{
						throw new InvalidOperationException($"Failed to make source frame writable: {GetErrorMessage(ret)}");
					}

					for (int row = 0; row < _height; row++)
					{
						Buffer.MemoryCopy(srcData + row * _width * 4, srcFrame->data[0] + row * srcFrame->linesize[0], _width * 4, _width * 4);
					}

					int result = ffmpeg.sws_scale(_swsContext, srcFrame->data, srcFrame->linesize, 0, _height, _videoFrame->data, _videoFrame->linesize);

					if (result < 0)
					{
						throw new InvalidOperationException($"sws_scale failed: {GetErrorMessage(result)}");
					}
				}
				finally
				{
					ffmpeg.av_frame_free(&srcFrame);
				}
			}
			finally
			{
				frameDataHandle.Free();
			}

			_videoFrame->pts = _videoPts++;

			ret = ffmpeg.avcodec_send_frame(_videoCodecContext, _videoFrame);
			if (ret < 0)
			{
				throw new InvalidOperationException($"Failed to send video frame: {GetErrorMessage(ret)}");
			}

			while (ret >= 0)
			{
				ret = ffmpeg.avcodec_receive_packet(_videoCodecContext, _packet);
				if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == AVERROR_EOF)
				{
					break;
				}
				else if (ret < 0)
				{
					throw new InvalidOperationException($"Failed to receive video packet: {GetErrorMessage(ret)}");
				}

				_packet->stream_index = _videoStream->index;
				ffmpeg.av_packet_rescale_ts(_packet, _videoCodecContext->time_base, _videoStream->time_base);

				ret = ffmpeg.av_interleaved_write_frame(_formatContext, _packet);
				if (ret < 0)
				{
					throw new InvalidOperationException($"Failed to write video packet: {GetErrorMessage(ret)}");
				}
			}
		}

		public void EncodeAudioFrame(short[] samples)
		{
			if (!_isInitialized)
				throw new InvalidOperationException("Encoder not initialized");

			if (samples == null || samples.Length == 0)
				return;

			_audioBuffer.AddRange(samples);

			int frameSamples = _audioFrameSize * _channels;
			while (_audioBuffer.Count >= frameSamples)
			{
				short[] frameData = new short[frameSamples];
				_audioBuffer.CopyTo(0, frameData, 0, frameSamples);
				_audioBuffer.RemoveRange(0, frameSamples);

				EncodeAudioFrameInternal(frameData);
			}
		}

		private void EncodeAudioFrameInternal(short[] samples)
		{
			int ret = ffmpeg.av_frame_make_writable(_audioFrame);
			if (ret < 0)
			{
				throw new InvalidOperationException($"Failed to make audio frame writable: {GetErrorMessage(ret)}");
			}

			int inSamples = samples.Length / _channels;

			GCHandle samplesHandle = GCHandle.Alloc(samples, GCHandleType.Pinned);
			try
			{
				byte* inData = (byte*)samplesHandle.AddrOfPinnedObject();
				byte*[] outDataArr = new byte*[8];
				for (int i = 0; i < _channels && i < 8; i++)
				{
					outDataArr[i] = _audioFrame->data[(uint)i];
				}

				fixed (byte** outDataPtr = outDataArr)
				{
					int converted = ffmpeg.swr_convert(
						_swrContext,
						outDataPtr,
						_audioFrameSize,
						&inData,
						inSamples);

					if (converted < 0)
					{
						throw new InvalidOperationException($"swr_convert failed: {GetErrorMessage(converted)}");
					}

					_audioFrame->nb_samples = converted;
					_audioFrame->pts = _audioPts;
					_audioPts += converted;
				}
			}
			finally
			{
				samplesHandle.Free();
			}

			ret = ffmpeg.avcodec_send_frame(_audioCodecContext, _audioFrame);
			if (ret < 0)
			{
				throw new InvalidOperationException($"Failed to send audio frame: {GetErrorMessage(ret)}");
			}

			while (ret >= 0)
			{
				ret = ffmpeg.avcodec_receive_packet(_audioCodecContext, _packet);
				if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == AVERROR_EOF)
				{
					break;
				}
				else if (ret < 0)
				{
					throw new InvalidOperationException($"Failed to receive audio packet: {GetErrorMessage(ret)}");
				}

				_packet->stream_index = _audioStream->index;
				ffmpeg.av_packet_rescale_ts(_packet, _audioCodecContext->time_base, _audioStream->time_base);

				ret = ffmpeg.av_interleaved_write_frame(_formatContext, _packet);
				if (ret < 0)
				{
					throw new InvalidOperationException($"Failed to write audio packet: {GetErrorMessage(ret)}");
				}
			}
		}

		public void Finish()
		{
			if (!_isInitialized)
				return;

			if (_audioBuffer.Count > 0)
			{
				int frameSamples = _audioFrameSize * _channels;
				short[] frameData = new short[frameSamples];
				int copyCount = Math.Min(_audioBuffer.Count, frameSamples);
				_audioBuffer.CopyTo(0, frameData, 0, copyCount);
				_audioBuffer.Clear();

				if (copyCount > 0)
				{
					EncodeAudioFrameInternal(frameData);
				}
			}

			ffmpeg.avcodec_send_frame(_videoCodecContext, null);
			ffmpeg.avcodec_send_frame(_audioCodecContext, null);

			FlushEncoder(_videoCodecContext, _videoStream);
			FlushEncoder(_audioCodecContext, _audioStream);

			ffmpeg.av_write_trailer(_formatContext);
		}

		public void Close()
		{
			Finish();
		}

		private void FlushEncoder(AVCodecContext* codecContext, AVStream* stream)
		{
			int ret;
			while ((ret = ffmpeg.avcodec_receive_packet(codecContext, _packet)) >= 0)
			{
				_packet->stream_index = stream->index;
				ffmpeg.av_packet_rescale_ts(_packet, codecContext->time_base, stream->time_base);
				ret = ffmpeg.av_interleaved_write_frame(_formatContext, _packet);
				if (ret < 0)
				{
					break;
				}
			}
		}

		private static string GetErrorMessage(int errorCode)
		{
			byte* buffer = stackalloc byte[ffmpeg.AV_ERROR_MAX_STRING_SIZE];
			ffmpeg.av_strerror(errorCode, buffer, ffmpeg.AV_ERROR_MAX_STRING_SIZE);
			return Marshal.PtrToStringAnsi((IntPtr)buffer) ?? $"Error code: {errorCode}";
		}

		private static AVPixelFormat GetPixelFormat(string codecName)
		{
			return codecName?.ToLowerInvariant() switch
			{
				"libx264rgb" => AVPixelFormat.AV_PIX_FMT_RGB24,
				"utvideo" => AVPixelFormat.AV_PIX_FMT_GBRP,
				"ffv1" => AVPixelFormat.AV_PIX_FMT_BGR0,
				"rawvideo" => AVPixelFormat.AV_PIX_FMT_BGR0,
				_ => AVPixelFormat.AV_PIX_FMT_YUV420P,
			};
		}

		private static bool IsRgbPixelFormat(AVPixelFormat pixFmt)
		{
			return pixFmt == AVPixelFormat.AV_PIX_FMT_RGB24 ||
				   pixFmt == AVPixelFormat.AV_PIX_FMT_BGR0 ||
				   pixFmt == AVPixelFormat.AV_PIX_FMT_GBRP;
		}

#pragma warning disable CS0618
		private static AVSampleFormat GetSupportedSampleFormat(AVCodec* codec)
		{
			if (codec->sample_fmts != null)
			{
				return codec->sample_fmts[0];
			}
			return AVSampleFormat.AV_SAMPLE_FMT_S16;
		}

		private static int GetSupportedSampleRate(AVCodec* codec, int requestedRate)
		{
			if (codec->supported_samplerates != null)
			{
				for (int i = 0; codec->supported_samplerates[i] != 0; i++)
				{
					if (codec->supported_samplerates[i] == requestedRate)
					{
						return requestedRate;
					}
				}
				return codec->supported_samplerates[0];
			}
			return requestedRate;
		}
#pragma warning restore CS0618

		public void Dispose()
		{
			if (_disposed)
				return;

			AVPacket* packet = _packet;
			if (packet != null)
			{
				ffmpeg.av_packet_free(&packet);
				_packet = null;
			}

			AVFrame* videoFrame = _videoFrame;
			if (videoFrame != null)
			{
				ffmpeg.av_frame_free(&videoFrame);
				_videoFrame = null;
			}

			AVFrame* audioFrame = _audioFrame;
			if (audioFrame != null)
			{
				ffmpeg.av_frame_free(&audioFrame);
				_audioFrame = null;
			}

			AVCodecContext* videoCodecContext = _videoCodecContext;
			if (videoCodecContext != null)
			{
				ffmpeg.avcodec_free_context(&videoCodecContext);
				_videoCodecContext = null;
			}

			AVCodecContext* audioCodecContext = _audioCodecContext;
			if (audioCodecContext != null)
			{
				ffmpeg.avcodec_free_context(&audioCodecContext);
				_audioCodecContext = null;
			}

			if (_swsContext != null)
			{
				ffmpeg.sws_freeContext(_swsContext);
				_swsContext = null;
			}

			SwrContext* swrContext = _swrContext;
			if (swrContext != null)
			{
				ffmpeg.swr_free(&swrContext);
				_swrContext = null;
			}

			AVFormatContext* formatContext = _formatContext;
			if (formatContext != null)
			{
				if ((_formatContext->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
				{
					ffmpeg.avio_closep(&_formatContext->pb);
				}
				ffmpeg.avformat_free_context(formatContext);
				_formatContext = null;
			}

			_disposed = true;
		}
	}
}
