using UnityEngine;

/// <summary>
/// Simple test script to manually create and test the Simple Status Board
/// </summary>
public class StatusBoardCreator : MonoBehaviour
{
    [Header("Manual Creation")]
    public bool createOnStart = true;
    public Vector3 boardPosition = new Vector3(8, 8, 0);
    
    void Start()
    {
        if (createOnStart)
        {
            CreateStatusBoardManually();
        }
    }
    
    [ContextMenu("Create Status Board")]
    void CreateStatusBoardManually()
    {
        Debug.Log("🔧 StatusBoardCreator: Creating status board manually...");
        
        // Check if already exists
        SimpleStatusBoard existing = FindObjectOfType<SimpleStatusBoard>();
        if (existing != null)
        {
            Debug.Log("🔧 StatusBoardCreator: Status board already exists, destroying old one");
            DestroyImmediate(existing.gameObject);
        }
        
        // Create SkillBasedActionSystem if it doesn't exist
        SkillBasedActionSystem skillSystem = FindObjectOfType<SkillBasedActionSystem>();
        if (skillSystem == null)
        {
            Debug.Log("🔧 StatusBoardCreator: Creating SkillBasedActionSystem");
            GameObject skillSystemGO = new GameObject("SkillBasedActionSystem");
            skillSystem = skillSystemGO.AddComponent<SkillBasedActionSystem>();
        }
        
        // Create SimpleStatusBoard
        GameObject statusBoardGO = new GameObject("SimpleStatusBoard");
        SimpleStatusBoard statusBoard = statusBoardGO.AddComponent<SimpleStatusBoard>();
        
        // Configure it
        statusBoard.boardPosition = boardPosition;
        statusBoard.fontSize = 2f;
        statusBoard.updateInterval = 1f;
        statusBoard.textColor = Color.white;
        statusBoard.backgroundColor = new Color(0, 0, 0, 0.7f);
        
        Debug.Log($"🔧 StatusBoardCreator: Created status board at position {boardPosition}");
    }
    
    [ContextMenu("Test Status Board")]
    void TestStatusBoard()
    {
        SimpleStatusBoard statusBoard = FindObjectOfType<SimpleStatusBoard>();
        if (statusBoard != null)
        {
            statusBoard.TestStatusBoard();
        }
        else
        {
            Debug.LogWarning("🔧 StatusBoardCreator: No status board found to test");
        }
    }
    
    [ContextMenu("Show Debug Info")]
    void ShowDebugInfo()
    {
        SimpleStatusBoard statusBoard = FindObjectOfType<SimpleStatusBoard>();
        if (statusBoard != null)
        {
            statusBoard.ShowDebugInfo();
        }
        else
        {
            Debug.LogWarning("🔧 StatusBoardCreator: No status board found");
        }
    }
}
