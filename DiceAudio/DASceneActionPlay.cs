using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiceAudio
{
    public class DASceneActionPlay : DASceneAction
    {
        public override Type ActionType { get; protected set; } = Type.Play;
        public override string Name { get; protected set; } = "Play";
        public List<DAAudioItem> AudioItems { get; set; } = new List<DAAudioItem>();
        public DASceneActionPlay() {}

        public override void SetAudioItems(List<DAAudioItem> items)
        {
            AudioItems = items;
        }
    }
}
