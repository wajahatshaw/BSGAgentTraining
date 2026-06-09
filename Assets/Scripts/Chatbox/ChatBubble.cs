using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;

public class ChatBubble : MonoBehaviour
{
    [Header("References (assign in inspector)")]
    [SerializeField] private TMP_Text messageText;            // child of bubble
    [SerializeField] private TMP_Text timeText;
    [SerializeField] private RectTransform bubbleRect;        // the bubble (child of container)
    [SerializeField] private RectTransform bubbleContainer;   // parent container (fixed width; will change height)
    [SerializeField] private Image bubbleImage;

    [Header("Settings")]
    [SerializeField] private float bubblePaddingX = 40f;      // total horizontal padding (left+right)
    [SerializeField] private float bubblePaddingY = 30f;      // total vertical padding (top+bottom)
    [SerializeField] private float horizontalInset = 16f;     // distance from container edge (when anchored left/right)
    [SerializeField] int letterCountInOneLine = 40;





    // cached initial widths so we only change height at runtime
    private float initialContainerWidth;
    private float initialBubbleWidth;

    private void Awake()
    {
        if (bubbleContainer != null)
            initialContainerWidth = bubbleContainer.sizeDelta.x;
        if (bubbleRect != null)
            initialBubbleWidth = bubbleRect.sizeDelta.x;
    }


    void OnEnable()
    {
        StartCoroutine(RebuildChatbuble());
    }
    /// <summary>
    /// Set chat text and alignment. Only heights are changed; widths are preserved from prefab.
    /// iaAi = true -> aligned to left; false -> bubble aligned to right.
    /// </summary>
    public void SetChat(string text,string timestamp, bool isAi)
    {
        if (messageText == null || bubbleRect == null || bubbleContainer == null || bubbleImage == null || timeText == null)
        {
            Debug.LogWarning("ChatBubble: missing references.");
            return;
        }
        

        // set color (example sources)
        bubbleImage.sprite = isAi ?  GameAsstes.Instance.aiChatSprite : GameAsstes.Instance.userChatSprite ;

        // assign text and force TMP to update
        messageText.text = text;
        timeText.text = timestamp;
        textToSet = text;
        this.isAi = isAi;

        if(gameObject.activeSelf)
            RebuildChatbuble();
        
    }

    IEnumerator RebuildChatbuble()
    {
        yield return null;
        messageText.ForceMeshUpdate();

        // compute inner width available for text: bubble width minus horizontal padding
        float innerWidth = Mathf.Max(1f, initialBubbleWidth - bubblePaddingX);

        // get preferred values with wrapping using inner width
        Vector2 preferred = messageText.GetPreferredValues(textToSet, innerWidth, Mathf.Infinity);

        // clamp preferred.x just in case
        preferred.x = Mathf.Min(preferred.x, innerWidth);

        if (textToSet.Length > letterCountInOneLine)
        {
            // full auto height
            Debug.Log("Seetting size:"+messageText.rectTransform.sizeDelta.x+" and "+ preferred.y);
            messageText.rectTransform.sizeDelta = new Vector2(messageText.rectTransform.sizeDelta.x, preferred.y);
        }

        // compute bubble height = text height + vertical padding
        float bubbleHeight = preferred.y + bubblePaddingY;

        // apply heights (preserve widths)
        bubbleRect.sizeDelta = new Vector2(initialBubbleWidth, bubbleHeight);
        bubbleContainer.sizeDelta = new Vector2(initialContainerWidth, bubbleHeight);

        // position the message text inside bubble with padding (anchor top-left inside bubble)
        // assume messageText pivot is (0,1) or centered; placing via anchoredPosition using half padding
        float leftPadding = bubblePaddingX * 0.5f;
        float topPadding = bubblePaddingY * 0.5f;
        //messageText.rectTransform.anchoredPosition = new Vector2(leftPadding, -topPadding);

        // ALIGN bubble left or right inside the container (do not change container anchors)
        if (isAi)
        {
            bubbleRect.anchorMin = new Vector2(1f, 1f);
            bubbleRect.anchorMax = new Vector2(1f, 1f);
            bubbleRect.pivot = new Vector2(0f, 1f); // top-left pivot
            bubbleRect.anchoredPosition = new Vector2(-horizontalInset, 0f);
        }
        else
        {
            bubbleRect.anchorMin = new Vector2(0f,1f);
            bubbleRect.anchorMax = new Vector2(0f,1f);
            bubbleRect.pivot = new Vector2(1f, 1f); // top-right pivot
            bubbleRect.anchoredPosition = new Vector2(horizontalInset, 0f);
        }
    }

    [TextArea(5,10)]
    [SerializeField] string textToSet;
    [SerializeField] string defaultTimestamp;
    [SerializeField] bool isAi;

    [NaughtyAttributes.Button]
    public void SetButton()
    {
        SetChat(textToSet,defaultTimestamp,isAi);
    }
}


