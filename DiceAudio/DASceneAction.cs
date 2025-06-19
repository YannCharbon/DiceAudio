using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiceAudio
{
    public abstract class DASceneAction
    {
        public enum Type { Play, Stop, SetVolume };
        public abstract Type ActionType { get; protected set; }
        public abstract string Name { get; protected set; }
        public DASceneAction() { }

        public abstract void SetAudioItems(List<DAAudioItem> items);
    }
}
