
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
                    Connection.SubToChannel(channelId, new ChannelSubscriptionOptions()
                    {
                        CanUpdateData = true,
                        FanOutIntervalMs = fanOutIntervalMs
                    }, callback: (_) =>
                    {
                        ChanneldTransport.Current.OnClientSubToChannel(channelId);
                    });
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
    }
}
