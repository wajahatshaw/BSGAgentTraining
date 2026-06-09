using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using System.Collections.Generic;
using Jy_Util;

public class PlayerNetworkManager : MonoBehaviour
{
    private PhotonView pv;
    [Header("Visual")]
    [SerializeField] TMP_Text playerNameText;
    [SerializeField] SpriteRenderer miniMapIcon;
    [SerializeField] private List<GameObject> playerBody= new List<GameObject>();


    
    void Start()
    {
        pv = GetComponent<PhotonView>();
        
        SetupMiniMapColors();

        if(!pv.IsMine)
        {
            playerNameText.text = pv.Owner.NickName;
            return;
        }

        // LOCAL PLAYER ONLY
        playerNameText.text = "";

        foreach(GameObject bodypart in playerBody) bodypart.SetActive(false);

        GameAsstes.Instance.miniMapCameraManager.Initiate(this.transform);

        
    }

    void SetupMiniMapColors()
    {
        if (pv.Owner.CustomProperties.TryGetValue(Jy_Utility._PlayerColor, out object colorIndex))
        {
            miniMapIcon.color = GameAsstes.Instance.possibleColorTint[(int)colorIndex];
        }
    }

    
}
