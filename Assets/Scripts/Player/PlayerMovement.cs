using System;
using Photon.Pun;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PlayerMovementInputProcessor))]
public class PlayerMovement : MonoBehaviour
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

    [HideInInspector] public FixedTouchField fixedTouchField;

    /// <summary>Backward-compatible joystick hook (GameManager, etc.) — forwards to input processor.</summary>
    public FixedJoystick fixedJoystick
    {
        get => _inputProcessor != null ? _inputProcessor.Joystick : null;
        set
        {
            if (_inputProcessor != null)
                _inputProcessor.BindJoystick(value);
        }
    }

    public bool RagAutopilotActive => _inputProcessor != null && _inputProcessor.RagAutopilotActive;

    PlayerMovementInputProcessor _inputProcessor;
    PhotonView pv;
    AgentGroundMotor _ragGroundMotor;
    bool _useGroundMotorForMovement;
    float _xRotation;
    bool _isCrouching;
    bool _bCanMove = true;
    bool _bCanCrouch = true;
    bool _bCanJump = true;
    bool _bCanInteract = true;
    bool _bCanLook = true;

    public PlayerMovementInputProcessor InputProcessor => _inputProcessor;

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

    void Awake()
    {
        _inputProcessor = GetComponent<PlayerMovementInputProcessor>();
        if (_inputProcessor == null)
            _inputProcessor = gameObject.AddComponent<PlayerMovementInputProcessor>();
    }

    void Start()
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

        _inputProcessor.BindJoystick(FindAnyObjectByType<FixedJoystick>());
        fixedTouchField = FindAnyObjectByType<FixedTouchField>();

        if (cameraTransform != null)
            cameraTransform.gameObject.tag = "MainCamera";

        if (RagPhysicalAgentLocalMode.ShouldUseRagPhysicalAgentMode())
            RagPhysicalAgentLocalMode.ApplyForLocalClient(this);
    }

    void Update()
    {
        if (!pv.IsMine)
            return;

        if (_bCanLook)
            HandleLook();

        if (_inputProcessor.SuppressesManualMovement)
            return;

        if (_bCanCrouch)
            HandleCrouch();
        if (_bCanJump)
            HandleJump();
        if (_bCanInteract && Input.GetKeyDown(KeyCode.E))
            HandleInteraction();
    }

    void FixedUpdate()
    {
        if (!pv.IsMine)
            return;

        // RagSequenceAgentMover drives AgentGroundMotor directly during physical autopilot.
        if (_inputProcessor.SuppressesManualMovement)
            return;

        if (!_bCanMove)
            return;

        HandleMovement();
    }

    void HandleLook()
    {
        Vector2 touchDist = fixedTouchField
            ? fixedTouchField.touchDist
            : new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * mouseSensitivity;
        float mouseX = touchDist.x * mouseSensitivity * Time.deltaTime;
        float mouseY = touchDist.y * mouseSensitivity * Time.deltaTime;

        _xRotation -= mouseY;
        _xRotation = Mathf.Clamp(_xRotation, -85f, 85f);

        cameraContainerTransform.localRotation = Quaternion.Euler(_xRotation, 0f, 0f);
        transform.Rotate(Vector3.up * mouseX);
    }

    void HandleMovement()
    {
        Vector2 moveInput = _inputProcessor.GetMoveAxes();
        float horizontal = moveInput.x;
        float vertical = moveInput.y;

        Vector3 moveDir = (transform.right * horizontal + transform.forward * vertical).normalized;
        float currentSpeed = _isCrouching ? crouchSpeed : moveSpeed;

        if (_useGroundMotorForMovement && _ragGroundMotor != null)
        {
            Vector3 direction = moveDir;
            if (direction.sqrMagnitude > 1e-8f)
                direction = direction.normalized * currentSpeed;

            Vector3 delta = direction * Time.fixedDeltaTime;
            _ragGroundMotor.TryMoveGround(delta);
            UpdateAnimator(vertical != 0f ? vertical : (moveDir.sqrMagnitude > 0.01f ? 1f : 0f));
            return;
        }

        if (!_inputProcessor.HasJoystickReference)
        {
            Vector3 targetVelocity = moveDir * currentSpeed;
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

        UpdateAnimator(vertical);
    }

    void UpdateAnimator(float verticalInput)
    {
        float walkValue = 0f;

        if (Mathf.Abs(verticalInput) > 0.1f)
            walkValue = verticalInput > 0f ? 0.5f : 1f;

        if (playerAnimator != null)
            playerAnimator.SetFloat("WalkSpeed", walkValue);
    }

    void HandleJump()
    {
        if (_useGroundMotorForMovement)
            return;
        if (!Input.GetKeyDown(KeyCode.Space) || !IsGrounded())
            return;

        rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        OnJump?.Invoke();
    }

    bool IsGrounded()
    {
        return Physics.Raycast(transform.position, Vector3.down, standingHeight / 2 + 0.1f);
    }

    void HandleCrouch()
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

        float targetHeight = _isCrouching ? crouchHeight : standingHeight;
        capsuleCollider.height = Mathf.Lerp(
            capsuleCollider.height,
            targetHeight,
            Time.deltaTime * crouchTransitionSpeed);
    }

    public bool GetCrouching() => _isCrouching;

    public void HandleInteraction() => OnInteract?.Invoke();

    public void DisableAll(bool move, bool crouch, bool interact, bool jump, bool look)
    {
        _bCanMove = move;
        _bCanCrouch = crouch;
        _bCanInteract = interact;
        _bCanJump = jump;
        _bCanLook = look;
    }

}
