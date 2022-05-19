
using System;
using UnityEngine;

namespace Channeld.Examples.Tanks
{
    public class TankClientViewUI : MonoBehaviour
    {
        public ChanneldConnection Connection { get; set; }
        public Action<uint> OnChannelSelected {get; set; }
        public Action OnUnsubAll {get; set; }

        private void OnGUI()
        {
            if (Connection == null)
                return;

            GUI.BeginGroup(new Rect(50, Screen.height - 50, 800, 30));
            int x = 0;
            foreach (var kv in Connection.ListedChannels)
            {
                if (GUI.Button(new Rect(x, 0, 80, 30), $"{kv.Value.ChannelType}-{kv.Value.ChannelId}"))
                {
                    OnChannelSelected?.Invoke(kv.Value.ChannelId);
                }
                x += 100;
            }
            if (GUI.Button(new Rect(x, 0, 80, 30), "Unsub All"))
            {
                OnUnsubAll?.Invoke();
            }
            GUI.EndGroup();
        }
    }
}
