using UnityEngine;

/// <summary>
/// Attach this script to the MiniMap Camera.
/// Call Initiate(playerTransform) once to begin following the player.
/// </summary>
public class MiniMapCameraManager : MonoBehaviour
{
    // ─── Configuration ────────────────────────────────────────────────────────

    [Header("Follow Settings")]
    [Tooltip("Height above the player at which the minimap camera hovers.")]
    [SerializeField] private float cameraHeight = 20f;

    [Tooltip("How smoothly the camera follows the player (0 = instant, higher = more lag).")]
    [SerializeField] private float smoothSpeed = 0f;

    // ─── Private State ─────────────────────────────────────────────────────────

    private Transform _playerTransform;
    private bool _isFollowing = false;

    // ─── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises the minimap camera to follow the given player transform.
    /// Call this once from your GameManager or Player script after the player
    /// has been spawned.
    /// </summary>
    /// <param name="playerTransform">The Transform of the player to follow.</param>
    public void Initiate(Transform playerTransform)
    {
        if (playerTransform == null)
        {
            Debug.LogWarning("[MiniMapCameraManager] Initiate() called with a null playerTransform. Aborting.");
            return;
        }

        _playerTransform = playerTransform;
        _isFollowing = true;

        // Snap camera into position immediately on init
        SnapToPlayer();

        
    }

    /// <summary>
    /// Stops the camera from following the player.
    /// </summary>
    public void StopFollowing()
    {
        _isFollowing = false;
    }

    /// <summary>
    /// Resumes following after StopFollowing() was called.
    /// Requires Initiate() to have been called first.
    /// </summary>
    public void ResumeFollowing()
    {
        if (_playerTransform == null)
        {
            Debug.LogWarning("[MiniMapCameraManager] ResumeFollowing() called but no player transform is set. Call Initiate() first.");
            return;
        }

        _isFollowing = true;
    }

    // ─── Unity Lifecycle ───────────────────────────────────────────────────────

    private void LateUpdate()
    {
        if (!_isFollowing || _playerTransform == null)
            return;

        FollowPlayer();
    }

    // ─── Private Helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Moves the camera to the player's XZ position at the configured height.
    /// Uses SmoothDamp if smoothSpeed > 0, otherwise snaps instantly.
    /// </summary>
    private void FollowPlayer()
    {
        Vector3 targetPosition = new Vector3(
            _playerTransform.position.x,
            _playerTransform.position.y + cameraHeight,
            _playerTransform.position.z
        );

        if (smoothSpeed <= 0f)
        {
            transform.position = targetPosition;
        }
        else
        {
            transform.position = Vector3.Lerp(
                transform.position,
                targetPosition,
                Time.deltaTime * (1f / smoothSpeed)
            );
        }
    }

    /// <summary>
    /// Instantly snaps the camera to the player position with no interpolation.
    /// Called on Initiate() to avoid a visible slide-in on first frame.
    /// </summary>
    private void SnapToPlayer()
    {
        transform.position = new Vector3(
            _playerTransform.position.x,
            _playerTransform.position.y + cameraHeight,
            _playerTransform.position.z
        );
    }
}