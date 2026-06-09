using System;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour, IRagPlayerMovementHost
{
    [SerializeField] Animator playerAnimator;
    [Header("Movement Settings")]
    public float moveSpeed = 6f;
    public float crouchSpeed = 3f;
    public float jumpForce = 5f;

    [Header("Mouse Settings")]
    [SerializeField] private Transform cameraContainerTransform;
    [SerializeField] private Transform cameraTransform;
    public float mouseSensitivity = 100f;

    

    [Header("Crouch Settings")]
    [SerializeField] private CapsuleCollider capsuleCollider;
    public float crouchHeight = 1f;
    public float standingHeight = 2f;
    public float crouchTransitionSpeed = 8f;

    public event Action OnInteract;
    public event Action<bool> OnCrouchStateChange;
    public event Action OnJump;

    public Rigidbody rb;
    private float _xRotation;
    private bool _isCrouching, _bCanMove = true, _bCanCrouch = true, _bCanJump = true, _bCanInteract = true, _bCanLook = true;

   [HideInInspector] public FixedJoystick fixedJoystick;
    [HideInInspector] public FixedTouchField fixedTouchField;

    private PhotonView pv;
    private AgentGroundMotor _ragGroundMotor;
    private bool _useGroundMotorForMovement;

    [Header("RAG physical autopilot (multiplayer zone 0)")]
    [Tooltip("When true, joystick input is ignored while RAG drives the player through physical steps.")]
    [SerializeField] private bool ragAutopilotActive;
    private Vector2 _ragVirtualJoystick;
    private int _ragZoneIndex = -1;

    public bool RagAutopilotActive => ragAutopilotActive;

    public void SetRagAutopilot(bool active, int zoneIndex = 0)
    {
        ragAutopilotActive = active;
        if (active)
            _ragZoneIndex = zoneIndex;
        if (!active)
        {
            _ragVirtualJoystick = Vector2.zero;
            UpdateAnimator(0f);
        }
    }

    public void SetRagVirtualJoystick(Vector2 axes)
    {
        _ragVirtualJoystick = Vector2.ClampMagnitude(axes, 1f);
    }

    public void ApplyRagMovementDelta(Vector3 delta)
    {
        ApplyRagGroundMovementDelta(delta);
    }

    public void ApplyRagGroundMovementDelta(Vector3 delta)
    {
        SetRagVirtualJoystick(RagMovementInputFeed.DeltaToVirtualJoystick(delta, transform));
    }

    public void EnableRagGroundMotorMovement(AgentGroundMotor motor)
    {
        _ragGroundMotor = motor;
        _useGroundMotorForMovement = motor != null;
        if (_useGroundMotorForMovement && rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            _ragGroundMotor.SnapFeetToGround();
        }
    }

    private void Start()
    {
        pv = GetComponent<PhotonView>();
        rb = GetComponent<Rigidbody>();

        if (!pv.IsMine)
        {
            rb.isKinematic = true;

            if (cameraTransform != null)
                cameraTransform.gameObject.SetActive(false);

            return;
        }

        rb.freezeRotation = true;

        fixedJoystick = FindAnyObjectByType<FixedJoystick>();
        fixedTouchField = FindAnyObjectByType<FixedTouchField>();

        if (cameraTransform != null)
            cameraTransform.gameObject.tag = "MainCamera";

        if (RagPhysicalAgentLocalMode.ShouldUseRagPhysicalAgentMode())
            RagPhysicalAgentLocalMode.ApplyForLocalClient(this);
    }


    private void Update()
    {
        if(!pv.IsMine) return;

        if (_bCanLook)
            HandleLook();

        if (ragAutopilotActive)
            return;

        if (_bCanCrouch) HandleCrouch();
        if (_bCanJump) HandleJump();
        if (_bCanInteract && Input.GetKeyDown(KeyCode.E)) HandleInteraction();
    }

    private void FixedUpdate()
    {
        if(!pv.IsMine) return;

        // RagSequenceAgentMover drives AgentGroundMotor directly during physical autopilot.
        if (ragAutopilotActive)
            return;

        if (!_bCanMove)
            return;

        HandleMovement();
    }

    private void HandleLook()
    {
        var touchDist = fixedTouchField ? fixedTouchField.touchDist : new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * mouseSensitivity;
        var mouseX = touchDist.x * mouseSensitivity * Time.deltaTime;
        var mouseY = touchDist.y * mouseSensitivity * Time.deltaTime;

        _xRotation -= mouseY;
        _xRotation = Mathf.Clamp(_xRotation, -85f, 85f);

        cameraContainerTransform.localRotation = Quaternion.Euler(_xRotation, 0f, 0f);
        transform.Rotate(Vector3.up * mouseX);
    }

    private void HandleMovement()
    {
        Vector2 moveInput = ReadMoveInput();
        float horizontal = moveInput.x;
        float vertical = moveInput.y;

        var moveDir = (transform.right * horizontal + transform.forward * vertical).normalized;
        var currentSpeed = _isCrouching ? crouchSpeed : moveSpeed;

        if (_useGroundMotorForMovement && _ragGroundMotor != null)
        {
            Vector3 direction = ragAutopilotActive
                ? transform.forward * vertical + transform.right * horizontal
                : moveDir;
            if (direction.sqrMagnitude > 1e-8f)
                direction = direction.normalized * currentSpeed;

            Vector3 delta = direction * Time.fixedDeltaTime;
            _ragGroundMotor.TryMoveGround(delta);
            UpdateAnimator(vertical != 0f ? vertical : (moveDir.sqrMagnitude > 0.01f ? 1f : 0f));
            return;
        }

        if (!fixedJoystick && !ragAutopilotActive)
        {
            var targetVelocity = moveDir * currentSpeed;
            rb.linearVelocity = new Vector3(
                targetVelocity.x,
                rb.linearVelocity.y,
                targetVelocity.z
            );

            UpdateAnimator(vertical);
            return;
        }

        Vector3 joyDirection = transform.forward * vertical + transform.right * horizontal;
        joyDirection = joyDirection.normalized * currentSpeed;

        rb.linearVelocity = new Vector3(
            joyDirection.x,
            rb.linearVelocity.y,
            joyDirection.z
        );

        if (ragAutopilotActive && _ragZoneIndex >= 0)
        {
            Vector3 clamped = ZonePlayAreaBounds.ClampPosition(_ragZoneIndex, rb.position);
            if ((clamped - rb.position).sqrMagnitude > 1e-8f)
            {
                rb.position = clamped;
                rb.linearVelocity = new Vector3(joyDirection.x, rb.linearVelocity.y, joyDirection.z);
            }
        }

        UpdateAnimator(vertical);
    }

    Vector2 ReadMoveInput()
    {
        if (ragAutopilotActive)
            return _ragVirtualJoystick;

        if (fixedJoystick)
            return new Vector2(fixedJoystick.Horizontal, fixedJoystick.Vertical);

        return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
    }

    private void UpdateAnimator(float verticalInput)
    {
        float walkValue = 0f;

        if (Mathf.Abs(verticalInput) > 0.1f)
        {
            if (verticalInput > 0f)
                walkValue = 0.5f;
            else
                walkValue = 1f;
        }

        playerAnimator.SetFloat("WalkSpeed", walkValue);
    }

    private void HandleJump()
    {
        if (_useGroundMotorForMovement)
            return;
        if (!Input.GetKeyDown(KeyCode.Space) || !IsGrounded()) return;
        rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        OnJump?.Invoke();
    }

    private bool IsGrounded()
    {
        return Physics.Raycast(transform.position, Vector3.down, standingHeight / 2 + 0.1f);
    }

    private void HandleCrouch()
    {
        if (Input.GetKeyDown(KeyCode.LeftShift))
        {
            _isCrouching = true;
            OnCrouchStateChange?.Invoke(true);
        }

        if (Input.GetKeyUp(KeyCode.LeftShift))
        {
            _isCrouching = false;
            OnCrouchStateChange?.Invoke(false);
        }

        var targetHeight = _isCrouching ? crouchHeight : standingHeight;
        var smoothHeight = Mathf.Lerp(capsuleCollider.height, targetHeight, Time.deltaTime * crouchTransitionSpeed);

        capsuleCollider.height = smoothHeight;
    }

    public bool GetCrouching()
    {
        return _isCrouching;
    }

    public void HandleInteraction()
    {
        OnInteract?.Invoke();
    }

    public void DisableAll(bool move, bool crouch, bool interact, bool jump, bool look)
    {
        _bCanMove = move;
        _bCanCrouch = crouch;
        _bCanInteract = interact;
        _bCanJump = jump;
        _bCanLook = look;
    }
}
