using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Channeld
{
    public class ChanneldInterestManagement : InterestManagement
    {
        public ChannelDataView ChannelViewPrefab;

        public ChannelDataView CurrentView { get; private set; }

        // We cannot use Awake as it will 'override' the InterestManagement.Awake() in Unity.
        protected virtual void Start()
        {
            string viewClassName = CmdLineArgParser.Default.GetOptionValue("--view", "-V");
            if (!string.IsNullOrEmpty(viewClassName))
            { 
                CurrentView = ScriptableObject.CreateInstance(viewClassName) as ChannelDataView;
                if (CurrentView == null)
                {
                    Log.Error($"Failed to create view by class name '{viewClassName}'");
                    return;
                }
            }
            else if (ChannelViewPrefab != null)
            {
                CurrentView = Instantiate(ChannelViewPrefab);
            }
            else
            {
                Log.Warning(@"No view is set for the interest management.
                    Use '-view' command line argument to set the view, 
                    or set the prefab in ChanneldInterestManagement.");
                return;
            }

            ChanneldTransport.OnAuthenticated += CurrentView.Initialize;

            NetworkLoop.OnLateUpdate += CurrentView.SendAllChannelUpdates;
        }

        private void OnDestroy()
        {
            if (CurrentView != null)
            {
                ChanneldTransport.OnAuthenticated -= CurrentView.Initialize;

                NetworkLoop.OnLateUpdate -= CurrentView.SendAllChannelUpdates;

                CurrentView.Unintialize();
            }
        }

        // TODO: add client callback

        // Server callback
        public override void OnSpawned(NetworkIdentity identity)
        {
            if (CurrentView == null)
                return;

            //uint channelId = ChanneldTransport.SetOwningChannel(identity.netId);

            foreach (var nb in identity.NetworkBehaviours)
            {
                /*
                if (nb is IChannelDataProvider)
                {
                    CurrentView.AddChannelDataProvider(channelId, nb as IChannelDataProvider);
                }

                if (nb is ChanneldNetworkTransform)
                {
                    var networkTransform = (ChanneldNetworkTransform)nb;

                    // We need to utilize the PingMessage to sync the remote timestamp, 
                    // otherwise if there's no other Mirror message, the remote timestamp will never get updated,
                    // and the new snapshot can never be buffered, so SnapshotInterpolation.Compute always returns false.
                    if (NetworkTime.PingFrequency > networkTransform.bufferTime)
                        NetworkTime.PingFrequency = networkTransform.bufferTime;
                }
                */
            }

            // TODO: Send the init state (with TransformState) to channeld, even there's no change yet
        }

        // TODO: Send ChannelDataUpdate with Removed = true
        /*
        public override void OnDestroyed(NetworkIdentity identity)
        {
            if (CurrentView == null)
                return;


            uint channelId = ChanneldTransport.GetOwningChannel(identity.netId);

            foreach (var nb in identity.NetworkBehaviours)
            {
                if (nb is IChannelDataProvider)
                {
                    CurrentView.RemoveChannelDataProvider(channelId, nb as IChannelDataProvider);
                }
            }

            ChanneldTransport.ResetOwningChannel(identity.netId);
        }
        */

        public override bool OnCheckObserver(NetworkIdentity identity, NetworkConnection newObserver)
        {
            return true;
        }

        public override void OnRebuildObservers(NetworkIdentity identity, HashSet<NetworkConnection> newObservers, bool initialize)
        {
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
            {
                // authenticated and joined world with a player?
                if (conn != null && conn.isAuthenticated && conn.identity != null)
                {
                    newObservers.Add(conn);
                }
            }
        }
    }
}
