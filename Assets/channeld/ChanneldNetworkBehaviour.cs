using Mirror;
using System;
using System.Collections.Generic;

namespace Channeld
{
    public abstract class ChanneldNetworkBehaviour : NetworkBehaviour
    {
        public override void OnStartServer()
        {
            AddDataProviders(NetworkServer.aoi as ChanneldInterestManagement);
        }
        public override void OnStopServer()
        {
            RemoveDataProviders(NetworkServer.aoi as ChanneldInterestManagement);
        }
        public override void OnStartClient()
        {
            AddDataProviders(NetworkServer.aoi as ChanneldInterestManagement);
        }
        public override void OnStopClient()
        {
            RemoveDataProviders(NetworkServer.aoi as ChanneldInterestManagement);
        }

        private void AddDataProviders(ChanneldInterestManagement aoi)
        {
            if (aoi == null || aoi.CurrentView == null)
            {
                Log.Error("ChanneldInterestManagement or ChannelDataView is not properly initialized.");
                return;
            }

            /* A ChanneldNetworkBehaviour can implement multiple IChannelDataProvider<T>
            foreach (var interfaceType in GetType().GetInterfaces())
            {
                if (interfaceType.IsSubclassOf(typeof(IChannelDataProvider<>)))
                {
                    aoi.CurrentView.AddChannelDataProviderToDefaultChannel((IChannelDataProvider)this, interfaceType.GetGenericArguments()[0]);
                }
            }
            */

            if (this is IChannelDataProvider)
            {
                aoi.CurrentView.AddChannelDataProviderToDefaultChannel((IChannelDataProvider)this);
            }
        }

        private void RemoveDataProviders(ChanneldInterestManagement aoi)
        {
            if (aoi == null || aoi.CurrentView == null)
            {
                Log.Error("ChanneldInterestManagement or ChannelDataView is not properly initialized.");
                return;
            }

            /* A ChanneldNetworkBehaviour can implement multiple IChannelDataProvider<T>
            foreach (var interfaceType in GetType().GetInterfaces())
            {
                if (interfaceType.IsSubclassOf(typeof(IChannelDataProvider<>)))
                {
                    aoi.CurrentView.RemoveChannelDataProviderFromAllChannels((IChannelDataProvider)this, interfaceType.GetGenericArguments()[0]);
                }
            }
            */

            if (this is IChannelDataProvider)
            {
                aoi.CurrentView.RemoveChannelDataProviderFromAllChannels((IChannelDataProvider)this);
            }
        }
    }
}
