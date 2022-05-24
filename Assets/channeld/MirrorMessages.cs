using Mirror;
using UnityEngine;

namespace Channeld
{
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
