using UnityEngine;

namespace PAP.EarnoutBSG
{
    [CreateAssetMenu(fileName = "-TaskProperties", menuName = "ScriptableObjects/Task Properties")]
    public class TaskProperties : ScriptableObject
    {
        public string TaskCode;
        public int ThingsPercentage;
        public int DataPercentage;
        public int PeoplePercentage;
        [TextArea]
        public string TaskShortDescription;
        [TextArea]
        public string Things;
        [TextArea]
        public string Data;
        [TextArea]
        public string People;
    }
}