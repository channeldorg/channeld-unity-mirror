
using Channeldpb;
using UnityEngine;

namespace Channeld.Examples.Tanks.Scripts
{
    [CreateAssetMenu(fileName = "TankClientSpatialView", menuName = "ScriptableObjects/TankClientSpatialView", order = 7)]
    public class TankClientSpatialView : TankClientViewBase
    {
        protected override void InitChannels()
        {
            base.InitChannels();

            Connection.AddMessageHandler((uint)MessageType.SubToChannel, (_, channelId, msg) =>
            {
                var subResultMsg = (SubscribedToChannelResultMessage)msg;
                if (subResultMsg.ChannelType == ChannelType.Spatial)
                {
                    ChanneldTransport.Current.OnClientSubToChannel(channelId);
                }
            });
            Connection.SubToChannel(ChanneldConnection.GlobalChannelId);
        }
    }
}
