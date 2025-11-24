using System;

namespace FarmacopilotAgent.Core.Models
{
    public class VersionInfo
    {
        public string Version { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public DateTime ReleaseDate { get; set; }
        public bool IsMandatory { get; set; }
        public string Sha256Hash { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
    }
}
