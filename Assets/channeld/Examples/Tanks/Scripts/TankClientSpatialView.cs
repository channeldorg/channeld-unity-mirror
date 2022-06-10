
using Channeldpb;
using UnityEngine;

namespace Channeld.Examples.Tanks.Scripts
{
    [CreateAssetMenu(fileName = "TankClientSpatialView", menuName = "ScriptableObjects/TankClientSpatialView", order = 7)]
    public class TankClientSpatialView : ChannelDataView
    {
        protected override void InitChannels()
        {
            RegisterChannelDataParser(ChannelType.Spatial, new TankGameChannelData(), TankGameChannelData.Parser);

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

        protected override void UninitChannels()
        {
        }
    }
}
