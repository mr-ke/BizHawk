using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

using BizHawk.Common.PathExtensions;
using BizHawk.Common.StringExtensions;

namespace BizHawk.Common
{
	public static class FFmpegService
	{
		private const string BIN_HOST_URI_WIN = "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-8.0.1-full_build-shared.7z";

		private const string VERSION = "ffmpeg version 8.";

		public static string FFmpegPath => Path.Combine(LibraryPath, "ffmpeg.exe");

		public static string LibraryPath => Path.Combine(PathUtils.DataDirectoryPath, "dll");

		public static readonly string Url = BIN_HOST_URI_WIN;

		public class AudioQueryResult
		{
			public bool IsAudio;
		}

		private static readonly Regex rxHasAudio = new Regex(@"Stream \#(\d*(\.|\:)\d*)\: Audio", RegexOptions.Compiled);
		public static AudioQueryResult QueryAudio(string path)
		{
			var ret = new AudioQueryResult();
			string stdout = Run("-i", path).Text;
			ret.IsAudio = rxHasAudio.Matches(stdout).Count > 0;
			return ret;
		}

		/// <summary>
		/// queries whether this service is available. if ffmpeg is broken or missing, then you can handle it gracefully
		/// </summary>
		public static bool QueryServiceAvailable()
		{
			try
			{
				return Run("-version").Text.Contains(VERSION);
			}
			catch
			{
				return false;
			}
		}

		public struct RunResults
		{
			public string Text;
			public int ExitCode;
		}

		public static RunResults Run(params string[] args)
		{
			var escapedArgs = args.Select(static s => s.ContainsOrdinal(' ') ? $"\"{s}\"" : s);
			var sbCmdline = new StringBuilder();
			foreach (var arg in escapedArgs)
			{
				sbCmdline.Append(arg);
				sbCmdline.Append(' ');
			}

			var oInfo = new ProcessStartInfo(FFmpegPath, sbCmdline.ToString().TrimEnd())
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			};

			using var proc = new Process { StartInfo = oInfo };
			var m = new Mutex();
			var outputBuilder = new StringBuilder();

			proc.OutputDataReceived += (_, e) =>
			{
				if (e.Data != null)
				{
					m.WaitOne();
					outputBuilder.Append(e.Data);
					m.ReleaseMutex();
				}
			};

			proc.ErrorDataReceived += (_, e) =>
			{
				if (e.Data != null)
				{
					m.WaitOne();
					outputBuilder.Append(e.Data);
					m.ReleaseMutex();
				}
			};

			proc.Start();
			proc.BeginOutputReadLine();
			proc.BeginErrorReadLine();
			proc.WaitForExit();

			m.WaitOne();
			var resultText = outputBuilder.ToString();
			m.ReleaseMutex();
			m.Dispose();

			return new RunResults
			{
				ExitCode = proc.ExitCode,
				Text = resultText,
			};
		}

		public static byte[] DecodeAudio(string path)
		{
			string tempfile = Path.GetTempFileName();
			try
			{
				var runResults = Run("-i", path, "-xerror", "-f", "wav", "-ar", "44100", "-ac", "2", "-acodec", "pcm_s16le", "-y", tempfile);
				if (runResults.ExitCode != 0)
					throw new InvalidOperationException($"Failure running ffmpeg for audio decode. here was its output:\r\n{runResults.Text}");
				byte[] ret = File.ReadAllBytes(tempfile);
				if (ret.Length == 0)
					throw new InvalidOperationException($"Failure running ffmpeg for audio decode. here was its output:\r\n{runResults.Text}");
				return ret;
			}
			finally
			{
				File.Delete(tempfile);
			}
		}
	}
}
