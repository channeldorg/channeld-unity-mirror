
using Mirror;
using System.Collections.Generic;
using UnityEngine;

namespace Channeld.Examples.Tanks
{
    [CreateAssetMenu(fileName = "TankClientMultiSubworldsView", menuName = "ScriptableObjects/TankClientMultiSubworldsView", order = 3)]
    public class TankClientMultiSubworldsView : TankClientViewBase
    {
        public TankClientViewUI uiPrefab;

        private TankClientViewUI ui;

        protected override void InitChannels()
        {
            base.InitChannels();

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
                            ChanneldTransport.Current.OnClientSubToChannel(channelId);

                            // Make sure the player provider is added to the channel view
                            if (NetworkClient.localPlayer != null)
                            {
                                foreach (var provider in NetworkClient.localPlayer.GetComponents<IChannelDataProvider>())
                                    AddChannelDataProvider(channelId, provider);
                            }
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
        }

        protected override void OnUnsubFromChannel(uint channelId, IEnumerable<IChannelDataProvider> removedProviders)
        {
            foreach (var provider in removedProviders)
            {
                if (provider is NetworkBehaviour networkBehaviour)
                {
                    // Send the ObjectDestroyMessage to handle the destroy properly.
                    // ChanneldTransport.Current.OnClientMessageReceived(new ObjectDestroyMessage(){netId = ((NetworkBehaviour)provider).netId});
                    NetworkClient.DestroyObject(networkBehaviour.netId);
                }
            }
        }
    }
}
