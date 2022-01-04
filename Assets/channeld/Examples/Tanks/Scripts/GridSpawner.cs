using Mirror;
using UnityEngine;

public class GridSpawner : MonoBehaviour
{
    public int xCount = 2;
    public int zCount = 2;
    public float xSpacing = 6f;
    public float zSpacing = 6f;

    private void Awake()
    {
        for (int z = -zCount/2; z < zCount/2; z++)
        {
            for (int x = -xCount/2; x < xCount/2; x++)
            {
                var sp = new GameObject("Spawn");
                sp.transform.SetParent(transform);
                sp.transform.position = new Vector3(
                    xSpacing * (x - 0.5f * (xCount % 2 - 1)), 0, zSpacing * (z - 0.5f * (zCount % 2 - 1)));
                NetworkManager.RegisterStartPosition(sp.transform);
            }
        }
    }
}
