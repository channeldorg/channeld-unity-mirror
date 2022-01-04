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
            if (ChanneldClient.Instance == null)
                return;

            var transport = Transport.activeTransport as ChanneldTransport;
            ChanneldClient.Instance.ListChannel(transport.ServerChannelType, null, (resultMsg) =>
            {
                var channels = resultMsg.Channels;
                if (channels.Count == 0)
                {
                    Debug.LogWarning($"Can't find {transport.ServerChannelType} channel to sub!");
                    return;
                }
                // Sub to the last (created) channel
                var list = channels.OrderByDescending(ch => ch.ChannelId);
                ChanneldClient.Instance.SubToChannel(list.First().ChannelId);
            });
        }
    }
}
