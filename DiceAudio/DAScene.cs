using Plugin.Maui.Audio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiceAudio
{
    public class DAScene
    {
        readonly DiceAudioService diceAudioService;

        public Guid Id { get; private set; } = Guid.Empty;
        public string Name { get; set; } = "Default";
        public bool EditionEnabled { get; set; } = true;
        public List<DASceneAction> SceneActions { get; set; } = new List<DASceneAction>();
        public DAScene? PreviousScene { get; set; } = null;

        public DAScene(DiceAudioService diceAudioService)
        {
            this.diceAudioService = diceAudioService;
        }
    }
}
