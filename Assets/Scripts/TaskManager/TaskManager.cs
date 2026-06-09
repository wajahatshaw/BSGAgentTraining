using NaughtyAttributes;
using UnityEngine;

public class TaskManager : TaskManagerBase
{
    [Space]
    [SerializeField] bool autoInit = false;
    [ShowIf(nameof(autoInit))] [SerializeField] float initDelay = 5f;


    void Start()
    {
        base.Start();
        RagPhysicalAgentLocalMode.TryApplyEarly();
        if (RagPhysicalAgentLocalMode.ShouldUseRagPhysicalAgentMode())
        {
            enabled = false;
            return;
        }

        if(autoInit)Invoke(nameof(TaskManagerInit),initDelay);
    }
}
