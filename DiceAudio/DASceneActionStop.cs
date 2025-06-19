using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiceAudio
{
    public class DASceneActionStop : DASceneAction
    {
        public override Type ActionType { get; protected set; } = Type.Stop;
        public override string Name { get; protected set; } = "Stop";
        public List<DAAudioItem> AudioItems { get; set; } = new List<DAAudioItem>();
        public int FadeOutDuration { get; set; } = 0;
        public string SelectedAudioItemName { get; set; } = string.Empty;
        public bool StopAll { get; set; } = false;
        public DASceneActionStop() {}

        public override void SetAudioItems(List<DAAudioItem> items)
        {
            AudioItems = items;
        }
    }
}
