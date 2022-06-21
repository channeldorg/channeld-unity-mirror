using Mirror;
using System;
using UnityEngine;

namespace Channeld
{
    // The replacement of Mirror's SpawnMessage
    public struct SpawnInChannelMessage : NetworkMessage
    {
        // In which channel the object is spawned
        public uint channelId;
        // netId of new or existing object
        public uint netId;
        public bool isLocalPlayer;
        // Sets hasAuthority on the spawned object
        public bool isOwner;
        public ulong sceneId;
        // If sceneId != 0 then it is used instead of assetId
        public Guid assetId;
        // Local position
        public Vector3 position;
        // Local rotation
        public Quaternion rotation;
        // Local scale
        public Vector3 scale;
        // serialized component data
        // ArraySegment to avoid unnecessary allocations
        public ArraySegment<byte> payload;
    }

    /// <summary>
    /// When a player subs to multiple channels that need to create the player prefab in order to call RPC,
    /// we need to bind the same netId with the player objects. 
    /// The netId is created by the first channel that the player subs (usually the Global channel).
    /// We call the player object on the server owns the first channel 'Authority Player' and other player objects 'Proxy Player'.
    /// </summary>
    public struct AddPlayerProxyMessage : NetworkMessage
    {
        public uint netId;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
    }
}
