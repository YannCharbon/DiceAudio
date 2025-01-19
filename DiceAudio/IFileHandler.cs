using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiceAudio
{
    public interface IFileHandler
    {
        Task SaveFileAsync(string fileName, byte[] data);
        Task<byte[]> LoadFileAsync();
    }

}
