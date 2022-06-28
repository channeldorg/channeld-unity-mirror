
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
        public GameObject subscriptionBoxPrefab;
        //public float height = 1.0f;
        public Vector3 minSize = new Vector3(0.1f, 0.1f, 0.1f);
        public Vector3 maxSize = new Vector3(1000, 10f, 1000f);

        private IList<SpatialRegion> regions = null;
        private Dictionary<uint, GameObject> regionBoxes = new Dictionary<uint, GameObject>();
        private List<GameObject> subBoxes = new List<GameObject>();
        private List<Color> colors = new List<Color>();

#if DEBUG
        private void Start()
        {
            ChanneldTransport.OnAuthenticated += (conn) =>
            {
                conn.SetMessageHandlerEntry((uint)MessageType.SpatialRegionsUpdate, SpatialRegionsUpdateMessage.Parser, HandleSpatialRegionsResult);
                conn.AddMessageHandler((uint)MessageType.SubToChannel, UpdateSubBoxes);
                conn.AddMessageHandler((uint)MessageType.UnsubFromChannel, UpdateSubBoxes);

                conn.Send(ChanneldConnection.GlobalChannelId, (uint)MessageType.DebugGetSpatialRegions, new DebugGetSpatialRegionsMessage());
            };
        }

        private void HandleSpatialRegionsResult(ChanneldConnection conn, uint channelId, IMessage msg)
        {
            var resultMsg = (SpatialRegionsUpdateMessage)msg;
            regions = resultMsg.Regions;

            foreach (var box in regionBoxes.Values)
            {
                Destroy(box);
            }
            regionBoxes.Clear();

            uint serverCount = regions.Max(r => r.ServerIndex) + 1;
            colors.Clear();
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
                box.transform.localScale = Vector3.Min(maxSize, bounds.size);
                var renderer = box.GetComponent<Renderer>();
                if (renderer == null)
                    continue;
                var color = colors[(int)region.ServerIndex];
                renderer.material.color = new Color(color.r, color.g, color.b, renderer.material.color.a);
                regionBoxes.Add(region.ChannelId, box);
            }
        }

        private void UpdateSubBoxes(ChanneldConnection conn, uint channelId, IMessage msg)
        {
            if (regions == null)
                return;

            foreach (var box in subBoxes)
            {
                Destroy(box);
            }
            subBoxes.Clear();

            foreach (var kv in conn.SubscribedChannels)
            {
                if (regionBoxes.TryGetValue(kv.Key, out var regionBox))
                {
                    var subBox = Instantiate(subscriptionBoxPrefab, transform);
                    subBox.name = $"Channel-{kv.Key}-Sub";
                    subBox.transform.position = regionBox.transform.position;
                    subBox.transform.localScale = regionBox.transform.localScale;
                    subBoxes.Add(subBox);
                }
            }
        }

#endif
    }

}
