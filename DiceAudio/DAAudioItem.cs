using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiceAudio
{
    public class DAAudioItem
    {
        public Guid Id { get; private set; } = Guid.Empty;
        public string Name { get; set; } = "Default";
        public string SourceURL { get; set; } = string.Empty;
        public int SourceDownloadProgress { get; set; } = 0;
        public string SourceDownloadStatusMessage { get; set; } = String.Empty;
        public bool SourceIsDownloading { get; set; } = false;
        public bool IsLocallyAvailable { get; set; } = false;
        public string LocalFileName { get; set; } = String.Empty;

        public DAAudioItem(string name)
        {
            Name = name;
            Id = Guid.NewGuid();
        }

    }
}
