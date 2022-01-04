using System;
using UnityEngine;

namespace Channeld.Examples.Tanks
{
    public interface ITankController
    {
        float GetRotation();
        float GetMovement();
        bool GetFired();
    }

    public class TankKeyboardController : MonoBehaviour, ITankController
    {
        public KeyCode shootKey = KeyCode.Space;

        public float GetMovement()
        {
            return Input.GetAxis("Vertical");
        }

        public float GetRotation()
        {
            return Input.GetAxis("Horizontal");
        }

        public bool GetFired()
        {
            return Input.GetKeyDown(shootKey);
        }

        void Update()
        {
            if (Input.mouseScrollDelta.y != 0)
            {
                Camera.main.fieldOfView -= Time.deltaTime * Input.mouseScrollDelta.y * 20f;
            }

            var up = Camera.main.transform.up;
            Camera.main.transform.position = new Vector3(transform.position.x, Camera.main.transform.position.y, Camera.main.transform.position.z);
            Camera.main.transform.LookAt(transform.position, up);
        }
    }

}
