using Jy_Util;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyPlayerContainer : MonoBehaviour
{
    [SerializeField] Image avaterBG;
    [SerializeField] TMP_Text avaterText;
    [SerializeField] UnityEngine.UI.Outline outline;

    [SerializeField] TMP_Text playerNameText;
    [SerializeField] TMP_Text playerDesignationText;
    [SerializeField] Image readyStatusImg;

    public Player myPlayer;

    public void Setup(Player player)
    {
        myPlayer = player;
        playerNameText.text = player.NickName;
        avaterText.text = playerNameText.text.Substring(0,1);

        
        if(player.IsMasterClient) readyStatusImg.sprite = GameAsstes.Instance.readySprite;
        if (player.CustomProperties.TryGetValue(Jy_Utility._PlayerColor, out object value)
    && value is int colorIndex
    && colorIndex >= 0
    && colorIndex < GameAsstes.Instance.possibleColorTint.Count)
    {
        Debug.Log("Got Color index: " + colorIndex);

        Color tint = GameAsstes.Instance.possibleColorTint[colorIndex];
        avaterBG.color = tint;
        outline.effectColor = tint;
    }
    else
    {
        Debug.LogWarning("Invalid or missing player color index"+player.CustomProperties.TryGetValue(Jy_Utility._PlayerColor,out var data));
    }


        if (player.CustomProperties.TryGetValue("Designation", out object role))
        {
            playerDesignationText.text = role.ToString();
        }
        else
        {
            playerDesignationText.text = "Worker";
        }

    }

   

    

    public void RefreshProp(ExitGames.Client.Photon.Hashtable changedProps)
    {
        //color
        if (changedProps.ContainsKey(Jy_Utility._PlayerColor) && myPlayer.CustomProperties.TryGetValue(Jy_Utility._PlayerColor, out object value)
            && value is int colorIndex
            && colorIndex >= 0
            && colorIndex < GameAsstes.Instance.possibleColorTint.Count)
        {
            Color tint = GameAsstes.Instance.possibleColorTint[colorIndex];
            avaterBG.color = tint;
            outline.effectColor = tint;
        }

        if (changedProps.ContainsKey(Jy_Utility._Ready) && myPlayer.CustomProperties.TryGetValue(Jy_Utility._Ready, out object ready) && ready is bool)
        {
            readyStatusImg.sprite =((bool)ready)?GameAsstes.Instance.readySprite:GameAsstes.Instance.waitingSprite;
        }
    }

}
