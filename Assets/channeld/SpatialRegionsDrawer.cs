
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
        public GameObject regionBoxPrefab;
        //public float height = 1.0f;
        public Vector3 minSize = new Vector3(0.1f, 0.1f, 0.1f);

        private IList<SpatialRegion> regions = null;
        private List<Color> colors = new List<Color>();

#if DEBUG
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

            foreach (var region in regions)
            {
                var box = Instantiate(regionBoxPrefab, transform);
                box.name = $"Channel-{region.ChannelId}-Server-{region.ServerIndex}";
                var bounds = region.ToBounds();
                box.transform.position = bounds.center;
                box.transform.localScale = Vector3.Max(minSize, bounds.size);
                var renderer = box.GetComponent<Renderer>();
                if (renderer == null)
                    continue;
                var color = colors[(int)region.ServerIndex];
                renderer.material.color = new Color(color.r, color.g, color.b, renderer.material.color.a);
            }
        }

        /*
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
        */
#endif
    }

}
