using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiceAudio
{
    public class DAVirtualAudioFolder
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = ".";
        public Guid ParentFolderId { get; set; } = Guid.Empty;
        public List<DAVirtualAudioFolder> VirtualAudioSubFolders { get; set; } = new List<DAVirtualAudioFolder>();
        public DAVirtualAudioFolder() { }

        public DAVirtualAudioFolder(string name)
        {
            Name = name;
        }
    }
}
