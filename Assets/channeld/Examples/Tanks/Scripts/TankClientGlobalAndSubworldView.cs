
using Mirror;
using System.Collections.Generic;
using UnityEngine;

namespace Channeld.Examples.Tanks
{
    [CreateAssetMenu(fileName = "TankClientGlobalAndSubworldView", menuName = "ScriptableObjects/TankClientGlobalAndSubworldView", order = 3)]
    public class TankClientGlobalAndSubworldView : TankClientViewBase
    {
        public TankClientViewUI uiPrefab;

        private TankClientViewUI ui;

        protected override void InitChannels()
        {
            base.InitChannels();

            Connection.SubToChannel(ChanneldConnection.GlobalChannelId, new ChannelSubscriptionOptions()
            {
                CanUpdateData = false,
                FanOutIntervalMs = fanOutIntervalMs
            }, (_) =>
            {
                ChanneldTransport.Current.OnClientSubToChannel(ChanneldConnection.GlobalChannelId);
                Connection.ListChannel(channelType, callback: (resultMsg) =>
                {
                    ui = Instantiate(uiPrefab);
                    ui.Connection = Connection;
                    ui.OnChannelSelected = (channelId) =>
                    {
                        if (Connection.SubscribedChannels.ContainsKey(channelId))
                        {
                            Connection.UnsubFromChannel(channelId);
                        }
                        else
                        {
                            Connection.SubToChannel(channelId, new ChannelSubscriptionOptions()
                            {
                                CanUpdateData = true,
                                FanOutIntervalMs = fanOutIntervalMs
                            }, callback: (_) =>
                            {
                            });
                        }
                    };
                    ui.OnUnsubAll = () =>
                    {
                        foreach (var kv in Connection.SubscribedChannels)
                        {
                            Connection.UnsubFromChannel(kv.Key);
                        }
                    };
                });
            });
        }

        protected override void OnUnsubFromChannel(uint channelId, IEnumerable<IChannelDataProvider> removedProviders)
        {
            foreach (var provider in removedProviders)
            {
                if (provider is NetworkBehaviour networkBehaviour)
                {
                    if (!networkBehaviour.isLocalPlayer)
                        NetworkClient.DestroyObject(networkBehaviour.netId);
                }
            }
        }
    }
}
