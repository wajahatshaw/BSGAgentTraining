using UnityEngine;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    [SerializeField] GameEvent Event_gameInit;
    [SerializeField] float initTime;
    public static GameManager SGameManager;
    [Header("PlayerSetup")]
    [SerializeField] private Transform playerStart;

    [SerializeField] private GameObject playerPrefab;

    [SerializeField] private FixedJoystick fixedJoystick;

    [SerializeField] private FixedTouchField fixedTouchField;

    [SerializeField] private Button interactButton;

    private GameObject _player;
    private PlayerMovement _playerMovement;

    public bool bKeyCollected;

    [Header("GameSettings")]
    [SerializeField] int FramrateCap = 60;
    [SerializeField] bool lockCursurWhenInPc = true;




    private void Awake()
    {
        ApplySettings();

        SGameManager = this;
        //_player = Instantiate(playerPrefab, playerStart,false);
        //_playerMovement = _player.GetComponent<PlayerMovement>();
        if(!_playerMovement) return;
        _playerMovement.fixedJoystick = fixedJoystick;
        _playerMovement.fixedTouchField = fixedTouchField;
        interactButton.onClick.AddListener(_playerMovement.HandleInteraction);
        
    }

    void Start()
    {
        Invoke(nameof(InitSimulation),initTime);
    }

    void InitSimulation()
    {
        Event_gameInit.Raise(this,true);
    }

    void ApplySettings()
    {
        if(FramrateCap > 0)
        {
            Application.targetFrameRate = FramrateCap;
        }

        if(lockCursurWhenInPc)
        {
            Cursor.lockState = CursorLockMode.Locked;
        }
    }
}
