using System.IO;
using System.Runtime.InteropServices;

using FFmpeg.AutoGen;

namespace BizHawk.Client.EmuHawk
{
	public static class FFmpegLibrary
	{
		private static bool _isInitialized;
		private static string _libraryPath;
		private static string _initError;
		private static readonly bool _isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
		private static readonly object _lock = new object();

		public static string LibraryPath
		{
			get
			{
				if (_libraryPath == null)
				{
					var currentDir = AppDomain.CurrentDomain.BaseDirectory;
					_libraryPath = Path.Combine(currentDir, "dll");
				}
				return _libraryPath;
			}
		}

		public static bool IsInitialized => _isInitialized;
		public static string LastError => _initError;

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool SetDllDirectory(string lpPathName);

		public static void Initialize()
		{
			if (_isInitialized) return;

			lock (_lock)
			{
				if (_isInitialized) return;

				try
				{
					var libraryPath = LibraryPath;

					if (!_isLinux && Directory.Exists(libraryPath))
					{
						SetDllDirectory(libraryPath);
					}

					ffmpeg.RootPath = libraryPath;
					ffmpeg.avformat_version();

					_isInitialized = true;
					_initError = null;
				}
				catch (Exception ex)
				{
					_initError = ex.Message;
					throw;
				}
			}
		}

		public static void Initialize(string customPath)
		{
			if (_isInitialized) return;

			lock (_lock)
			{
				if (_isInitialized) return;

				try
				{
					_libraryPath = customPath;

					if (!_isLinux && Directory.Exists(customPath))
					{
						SetDllDirectory(customPath);
					}

					ffmpeg.RootPath = customPath;

					_isInitialized = true;
					_initError = null;
				}
				catch (Exception ex)
				{
					_initError = ex.Message;
					throw;
				}
			}
		}

		public static bool QueryServiceAvailable()
		{
			try
			{
				Initialize();
				ffmpeg.avformat_version();
				return true;
			}
			catch
			{
				return false;
			}
		}

		public static string GetVersion()
		{
			try
			{
				Initialize();
				return ffmpeg.av_version_info();
			}
			catch
			{
				return null;
			}
		}

		public static string GetDebugInfo()
		{
			var info = new System.Text.StringBuilder();
			info.AppendLine($"Platform: {(_isLinux ? "Linux" : "Windows")}");
			info.AppendLine($"Library Path: {LibraryPath}");
			info.AppendLine($"Directory Exists: {Directory.Exists(LibraryPath)}");
			info.AppendLine($"Is Initialized: {_isInitialized}");
			info.AppendLine($"Last Error: {_initError ?? "None"}");

			if (_isInitialized)
			{
				try
				{
					info.AppendLine($"FFmpeg Version: {ffmpeg.av_version_info()}");
				}
				catch (Exception ex)
				{
					info.AppendLine($"Version check error: {ex.Message}");
				}
			}

			return info.ToString();
		}
	}
}
