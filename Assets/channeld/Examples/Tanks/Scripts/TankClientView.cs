﻿
using Mirror;
using UnityEngine;

namespace Channeld.Examples.Tanks
{
    [CreateAssetMenu(fileName = "TankClientView", menuName = "ScriptableObjects/TankClientView", order = 2)]
    public class TankClientView : ChannelDataView
    {
        public ChannelType channelType = ChannelType.Global;
        public uint fanOutIntervalMs = 50;

        protected override void InitChannels()
        {
            RegisterChannelDataParser(channelType, new TankGameChannelData(), TankGameChannelData.Parser);

            /*
            // Sub to the global channel, and then the server will proceed the client connection logic.
            Connection.SubToChannel(ChanneldConnection.GlobalChannelId, new ChannelSubscriptionOptions()
            {
                CanUpdateData = true,
                FanOutIntervalMs = fanOutIntervalMs
            });
            */
            Connection.ListChannel(channelType, callback: (resultMsg) =>
            {
                if (resultMsg.Channels.Count > 0)
                {
                    uint channelId = resultMsg.Channels[resultMsg.Channels.Count - 1].ChannelId;
                    Connection.SubToChannel(channelId, new ChannelSubscriptionOptions()
                    {
                        CanUpdateData = true,
                        FanOutIntervalMs = fanOutIntervalMs
                    }, callback: (_) =>
                    {
                        (Transport.activeTransport as ChanneldTransport).OnClientSubToChannel(channelId);
                    });
                }
            });
        }

        protected override void UninitChannels()
        {
            // No need to unsub, as channeld will handle the closed connections properly.
        }
    }
}
