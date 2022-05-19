
using Mirror;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;

namespace Channeld.Examples.Tanks
{
    public class TankClientViewBase : ChannelDataView
    {
        public ChannelType channelType = ChannelType.Global;
        public uint fanOutIntervalMs = 50;

        protected override void LoadCmdLineArgs()
        {
            CmdLineArgParser.Default.GetEnumOptionFromString("--channel-type", "-ct", ref channelType);
            CmdLineArgParser.Default.GetOptionValue("--fan-out-interval", "-fo", ref fanOutIntervalMs);
        }

        protected override void InitChannels()
        {
            RegisterChannelDataParser(channelType, new TankGameChannelData(), TankGameChannelData.Parser);
        }

        protected override void OnUnsubFromChannel(uint channelId, IEnumerable<IChannelDataProvider> removedProviders)
        {
            foreach (var provider in removedProviders)
            {
                if (provider is NetworkBehaviour)
                {
                    // Send the ObjectDestroyMessage to handle the destroy properly.
                    // ChanneldTransport.Current.OnClientMessageReceived(new ObjectDestroyMessage(){netId = ((NetworkBehaviour)provider).netId});
                    NetworkClient.DestroyObject(((NetworkBehaviour)provider).netId);
                }
            }
        }

        protected override void UninitChannels()
        {
            // No need to unsub, as channeld will handle the closed connections properly.
        }
    }
}
