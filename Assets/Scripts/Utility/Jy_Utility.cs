using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Jy_Util{
public class Jy_Utility : MonoBehaviour
{
    public void NullFun()
    {

    }



    #region Multiplayer Custome Prop


    public static string _Designation = "Designation";
    public static string _PlayerColor = "PlayerColor";
    public static string _Ready = "Ready";

    #endregion
}
    public enum PlayerRole
    {
        Supervisor,
        Worker
    }

    [System.Serializable]
    public class InventoryItem
    {
        public E_Inventory_Item_Type item_type;
        public int amount;

        public InventoryItem(E_Inventory_Item_Type item_type, int amount)
        {
            this.item_type = item_type;
            this.amount = amount;
        }
    }

    


    public delegate void NoArgumentFun();


#region ENUMS
public enum E_Inventory_Item_Type
    {
        Screw = 0,
        Magnet = 1,
        Iron = 2,
        Nail = 3,
        Hammer = 4,
        ToolBox =5,
        Gloves = 6,
        Screwdriver = 7,
    }


public enum E_Task_Completion_Status
{
    Pending = 0,
    Completed = 1,
    OnGoing = 2,
    faild = 3,        
}
#endregion



#region STRUCT
#endregion


#region CLass

#region LOGS
[System.Serializable]
public class ConversationLog
{
    public string sender_user_id;
    public string receiver_user_id;
    public string message;
    public string timestamp;
    public string msg_type;
}

[System.Serializable]
public class ConversationLogsRoot
{
    public Dictionary<string, ConversationSession> sessions;
}

[System.Serializable]
public class ConversationSession
{
    public Dictionary<string, object> conversations;
    public List<ConversationUser> users;
}

[System.Serializable]
public class ConversationUser
{
    public string user_id;
    public string name;
    public string role;
    public string avatar;
}


[System.Serializable]
public class LogRequestBody
{
    public string performed_activity_id;
    public string message;
    public string recipient_id;
    public string msg_type; // "msg" or "task"
}
#endregion

#region  Question

[System.Serializable]
public class QuestionApiResponse
{
    public string message;
    public string performed_activity_id;
    public List<QuestionResultBlock> results;
}

[System.Serializable]
public class QuestionResultBlock
{
    public string performed_activity_id;
    public string prompt_activity_unique_id;
    public string occupation_title;
    public int questions_count;
    public PointSummary point_summary;
    public List<QuestionData> questions;
}

[System.Serializable]
public class PointSummary
{
    public int total_correct_points;
    public int total_incorrect_points;
    public int question_count;
}

[System.Serializable]
public class QuestionData
{
    public string id;
    public string question;
    public QuestionOptions options;
    public string correct_option; // "A", "B", "C", "D"
    public string difficulty;
    public string skill;
    public int correct_points;
    public int incorrect_points;
}

[System.Serializable]
public class QuestionOptions
{
    public string A;
    public string B;
    public string C;
    public string D;
}



#endregion

[System.Serializable]
public class SkillLevelRequest 
{
    public string object_id;
    public string skill_name;
    public string new_level;
}

[System.Serializable]
public class SkillLevelResponse
{
    public string message;
    public string object_id;
    public string skill_name;
    public string new_level;
    public int updated_count;
    public int found_occurrences;
    public string updated_at;
}

[System.Serializable]
public class TaskInfoDS
{
    public string taskDesc;
    public int availableTime;
    public string performerName;
    public E_Task_Completion_Status taskStatus;        
}

#endregion
}