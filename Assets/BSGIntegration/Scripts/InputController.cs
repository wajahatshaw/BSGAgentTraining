using UnityEngine;

namespace PAP.EarnoutBSG
{
    public class InputController : MonoBehaviour
    {
        private Vector2 _moveInput;

        public Vector2 MoveInput
        {
            get => _moveInput;
            set => _moveInput = value.normalized;
        }
    }
}