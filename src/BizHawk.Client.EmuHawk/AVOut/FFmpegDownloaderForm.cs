using System.IO;
using System.Linq;

using BizHawk.Common;

namespace BizHawk.Client.EmuHawk
{
	public sealed class FFmpegDownloaderForm : DownloaderForm
	{
		protected override string ComponentName
			=> "FFmpeg";

		protected override string DownloadTemp { get; }
			= TempFileManager.GetTempFilename("ffmpeg_download", ".7z", delete: false);

		public FFmpegDownloaderForm()
		{
			Description = "BizHawk requires FFmpeg 8.0 shared libraries for video encoding."
				+ "\n\nThe required version could not be found."
				+ "\n\nUse this dialog to download it automatically, or download it yourself from the URL below and extract the DLLs to the specified location.";
			DownloadFrom = FFmpegService.Url;
			DownloadTo = FFmpegService.LibraryPath;
		}

		protected override bool ExtractFiles(HawkFile downloaded, string destinationPath)
		{
			var archiveName = Path.GetFileNameWithoutExtension(DownloadFrom);
			var binPrefix = $"{archiveName}/bin/";
			var dllFiles = downloaded.ArchiveItems
				.Where(item => item.Name.StartsWith(binPrefix) && string.Equals(Path.GetExtension(item.Name), ".dll", System.StringComparison.OrdinalIgnoreCase))
				.ToList();

			if (dllFiles.Count == 0)
				return false;

			Directory.CreateDirectory(destinationPath);

			foreach (var dll in dllFiles)
			{
				downloaded.BindArchiveMember(dll.Index);
				var fileName = Path.GetFileName(dll.Name);
				var destPath = Path.Combine(destinationPath, fileName);
				using var srcStream = downloaded.GetStream();
				using var destStream = File.Create(destPath);
				srcStream.CopyTo(destStream);
				downloaded.Unbind();
			}

			return true;
		}

		protected override bool PostChmodCheck()
		{
			return true;
		}
	}
}
