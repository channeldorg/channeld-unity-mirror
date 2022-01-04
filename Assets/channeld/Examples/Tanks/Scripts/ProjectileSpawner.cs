using Mirror;
using UnityEngine;

namespace Channeld.Examples.Tanks
{
    public class ProjectileSpawner : MonoBehaviour
    {
        public GameObject projectilePrefab;
        public float spawnInterval = 2;
        private float latestSpawnTime = 0;

        void Update()
        {
            if (NetworkManager.singleton == null)
                return;

            if (NetworkManager.singleton.isNetworkActive && NetworkServer.active)
            {
                if (Time.time - latestSpawnTime >= spawnInterval)
                {
                    var projectile = Instantiate(projectilePrefab, transform.position, Quaternion.LookRotation(new Vector3(0, 0, -1)));
                    NetworkServer.Spawn(projectile);
                    latestSpawnTime = Time.time;
                }
            }
        }
    }
}