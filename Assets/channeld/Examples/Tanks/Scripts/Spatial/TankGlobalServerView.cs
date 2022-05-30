
using Channeldpb;
using Mirror;
using UnityEngine;

namespace Channeld.Examples.Tanks.Scripts
{
    [CreateAssetMenu(fileName = "TankGlobalServerView", menuName = "ScriptableObjects/TankGlobalServerView", order = 1)]
    public class TankGlobalServerView : ChannelDataView
    {
        protected override void InitChannels()
        {
            Connection.AddMessageHandler((uint)MessageType.SubToChannel, (_, channelId, msg) =>
            {
                var subResultMsg = (SubscribedToChannelResultMessage)msg;
                // Received client subs to Global channel
                if (subResultMsg.ConnType == ConnectionType.Client && subResultMsg.ChannelType == ChannelType.Global)
                {
                    var startPos = NetworkManager.startPositions[(int)subResultMsg.ConnId % NetworkManager.startPositions.Count];
                    Connection.QuerySpatialChannel(new Vector3[]{startPos.position }, (queryResultMsg) =>
                    {
                        var startChannelId = queryResultMsg.ChannelId[0];
                        if (startChannelId == 0)
                        {
                            Log.Error($"Unable to map the player start position ({startPos.position}) to a spatial channel ID");
                            return;
                        }

                        // Sub the client to the spatial channel
                        Connection.SubConnectionToChannel(subResultMsg.ConnId, startChannelId);
                    });
                }
            });

            Connection.CreateChannel(ChannelType.Global, "Tanks");
        }

        protected override void UninitChannels()
        {
        }
    }
}
