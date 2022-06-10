using Mirror;

namespace Channeld
{
    internal class ChanneldNetworkManager : NetworkManager
    {
        public override void OnStopClient()
        {
            if (NetworkClient.aoi && NetworkClient.aoi is ChanneldInterestManagement cim)
            {
                cim.CurrentView.OnDisconnect();
                cim.CurrentView.SendAllChannelUpdates();
            }
        }
    }
}
