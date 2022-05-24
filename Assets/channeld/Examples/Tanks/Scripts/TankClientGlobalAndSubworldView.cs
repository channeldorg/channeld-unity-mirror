
using Channeldpb;
using Mirror;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Channeld.Examples.Tanks
{
    [CreateAssetMenu(fileName = "TankClientGlobalAndSubworldView", menuName = "ScriptableObjects/TankClientGlobalAndSubworldView", order = 4)]
    public class TankClientGlobalAndSubworldView : TankClientViewBase
    {
        public TankClientViewUI uiPrefab;

        private TankClientViewUI ui;


        protected override void InitChannels()
        {
            base.InitChannels();

            /*
            // Hide the player's tank after sub to the Global channel
            TankChanneld.OnLocalPlayerCreated += (tank) =>
            {
                tank.gameObject.SetActive(false);
            };

            Connection.AddMessageHandler((uint)MessageType.SubToChannel, (_, channelId, msg) =>
            {
                if (msg is SubscribedToChannelResultMessage resultMsg && resultMsg.ChannelType == channelType)
                { 
                    // Show the player's tank after sub to a Subworld channel
                    NetworkClient.localPlayer.gameObject.SetActive(true);
                    // Set the netId-channelId mapping to the new channel
                    netIdOwningChannels[NetworkClient.localPlayer.netId] = channelId;
                }
            });

            Connection.AddMessageHandler((uint)MessageType.UnsubFromChannel, (_, channelId, msg) =>
            {
                bool inChannel = Connection.SubscribedChannels.Any(kv => kv.Value.ChannelType == channelType);
                // Hide the player's tank again if no Subworld channel is subscribed.
                NetworkClient.localPlayer.gameObject.SetActive(inChannel);
                // Reset the netId-channelId mapping to the Global channel
                if (!inChannel)
                    netIdOwningChannels[NetworkClient.localPlayer.netId] = ChanneldConnection.GlobalChannelId;
            });
            */

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

                            if (NetworkClient.localPlayer != null)
                                NetworkClient.DestroyObject(NetworkClient.localPlayer.netId);
                        }
                        else
                        {
                            Connection.SubToChannel(channelId, new ChannelSubscriptionOptions()
                            {
                                CanUpdateData = true,
                                FanOutIntervalMs = fanOutIntervalMs
                            }, callback: (_) =>
                            {
                                /*
                                var player = NetworkClient.localPlayer;
                                // Spawn the player on the server that owns the channel, but don't let the server sends SpawnMessage back to the client.
                                // (as calling NetworkClient.connection.Send(new AddPlayerMessage()) will do)
                                ChanneldTransport.Current.ClientSendNetworkMessage(channelId, new AddPlayerProxyMessage()
                                {
                                    netId = player.netId,
                                    position = player.transform.localPosition,
                                    rotation = player.transform.localRotation,
                                    scale = player.transform.localScale,
                                });

                                // Sync the states to the new channel (only works in client-authoritative mode)
                                foreach (var provider in player.GetComponents<IChannelDataProvider>())
                                    AddChannelDataProvider(channelId, provider);
                                */
                            });
                        }
                    };
                    ui.OnUnsubAll = () =>
                    {
                        foreach (var kv in Connection.SubscribedChannels)
                        {
                            if (kv.Value.ChannelType != ChannelType.Global)
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
