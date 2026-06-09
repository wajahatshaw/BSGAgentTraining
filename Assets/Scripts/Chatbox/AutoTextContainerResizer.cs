using UnityEngine;
using TMPro;

public class AutoTextContainerResizer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] TMP_Text tmpText;
    [SerializeField] RectTransform container; 

    [Header("Settings")]
    [SerializeField] int charactersPerLine = 35;
    [SerializeField] float lineHeight = 50f;       // height per line
    [SerializeField] float verticalPadding = 20f;  // top + bottom padding

    /// <summary>
    /// Resize container height based on text length and settings.
    /// Call this whenever text changes.
    /// </summary>
    public void ResizeToFit()
    {
        if (tmpText == null || container == null)
        {
            Debug.LogWarning("AutoTextContainerResizer: Missing references!");
            return;
        }

        string text = tmpText.text;

        if (string.IsNullOrEmpty(text)) 
        {
            container.sizeDelta = new Vector2(
                container.sizeDelta.x,
                lineHeight + verticalPadding
            );
            return;
        }

        // total characters
        int totalChars = text.Length;

        // number of lines needed (ceiling)
        int lines = Mathf.CeilToInt((float)totalChars / charactersPerLine);

        // height = (lines * lineHeight) + padding
        float finalHeight = (lines * lineHeight) + verticalPadding;

        // apply height but keep width same
        container.sizeDelta = new Vector2(container.sizeDelta.x, finalHeight);
    }
}
