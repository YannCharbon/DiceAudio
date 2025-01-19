using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiceAudio
{
    public class PopupNotificationService
    {
        public enum Type { SUCCESS, FAILURE }

        public event Action<string, string, Type, int> OnShow;
        public event Action OnClose;

        public void Show(string title, string message, Type type, int durationMs)
        {
            OnShow?.Invoke(title, message, type, durationMs);
        }

        public void Close()
        {
            OnClose?.Invoke();
        }
    }

}
