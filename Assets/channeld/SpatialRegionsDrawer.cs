
using Channeldpb;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using System;

namespace Channeld
{
    public class SpatialRegionsDrawer : MonoBehaviour
    {
        public float height = 1.0f;

        private IList<SpatialRegion> regions = null;
        private List<Color> colors = new List<Color>();

#if UNITY_EDITOR
        private void Start()
        {
            ChanneldTransport.OnAuthenticated += (conn) =>
            {
                conn.SetMessageHandlerEntry((uint)MessageType.DebugGetSpatialRegions, DebugGetSpatialRegionsResultMessage.Parser, HandleSpatialRegionsResult);
                
                conn.Send(ChanneldConnection.GlobalChannelId, (uint)MessageType.DebugGetSpatialRegions, new DebugGetSpatialRegionsMessage());
            };
        }

        private void HandleSpatialRegionsResult(ChanneldConnection conn, uint channelId, IMessage msg)
        {
            var resultMsg = (DebugGetSpatialRegionsResultMessage)msg;
            regions = resultMsg.Regions;
            uint serverCount = regions.Max(r => r.ServerIndex) + 1;
            for (int i = 0; i < serverCount; i++)
            {
                colors.Add(Color.HSVToRGB(1.0f / serverCount * i, 0.5f, 0.5f));
            }
        }

        private void FixedUpdate()
        {
            if (regions == null)
                return;

            foreach (var region in regions)
            {
                DrawRectXZ(ToVector3(region.Min), ToVector3(region.Max), colors[(int)region.ServerIndex]);
            }
        }

        private void DrawRectXZ(Vector3 min, Vector3 max, Color color)
        {
            Debug.DrawLine(min, new Vector3(max.x, height, min.z), color);
            Debug.DrawLine(min, new Vector3(min.x, height, max.z), color);
            Debug.DrawLine(max, new Vector3(max.x, height, min.z), color);
            Debug.DrawLine(max, new Vector3(min.x, height, max.z), color);
        }

        private static Vector3 ToVector3(SpatialInfo info)
        {
            return new Vector3((float)info.X, (float)info.Y, (float)info.Z);
        }
#endif
    }

}
