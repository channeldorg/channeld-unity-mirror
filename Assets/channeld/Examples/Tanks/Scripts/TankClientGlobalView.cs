
using Channeldpb;
using Mirror;
using UnityEngine;

namespace Channeld.Examples.Tanks.Scripts
{
    [CreateAssetMenu(fileName = "TankClientGlobalView", menuName = "ScriptableObjects/TankClientGlobalView", order = 3)]
    public class TankClientGlobalView : TankClientViewBase
    {
        protected override void InitChannels()
        {
            base.InitChannels();

            Connection.SubToChannel(ChanneldConnection.GlobalChannelId, new ChannelSubscriptionOptions()
            {
                DataAccess = ChannelDataAccess.WriteAccess,
                FanOutIntervalMs = fanOutIntervalMs,
                FanOutDelayMs = 100,
            }, (_) =>
            {
                ChanneldTransport.Current.OnClientSubToChannel(ChanneldConnection.GlobalChannelId);
            });

        }
    }
}
