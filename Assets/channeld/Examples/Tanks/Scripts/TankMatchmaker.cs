using Channeld;
using Mirror;
using UnityEngine;
using System.Linq;

public class TankMatchmaker : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.C))
        {
            if (ChanneldConnection.Instance == null)
                return;

            var transport = Transport.activeTransport as ChanneldTransport;
            ChanneldConnection.Instance.ListChannel(ChannelType.Subworld, null, (resultMsg) =>
            {
                var channels = resultMsg.Channels;
                if (channels.Count == 0)
                {
                    Debug.LogWarning($"Can't find {ChannelType.Subworld} channel to sub!");
                    return;
                }
                // Sub to the last (created) channel
                var list = channels.OrderByDescending(ch => ch.ChannelId);
                ChanneldConnection.Instance.SubToChannel(list.First().ChannelId);
            });
        }
    }
}
