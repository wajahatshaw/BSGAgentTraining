using UnityEngine;
using TMPro;

public class SetChildTMPText : MonoBehaviour
{
    [SerializeField] private int octaveOffset;

    void Start()
    {
        SetTextOnAllChildren();
    }

    private void SetTextOnAllChildren()
    {
        TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true); // true = include inactive
        AudioSource[] audioSources = GetComponentsInChildren<AudioSource>(true);

        foreach (TMP_Text tmp in texts)
        {
            tmp.text = octaveOffset.ToString();
        }

        foreach(AudioSource temp in audioSources)
        {
            temp.pitch = Mathf.Pow(2f, octaveOffset);
        }
    }

    // Optional: call this if you want to update at runtime
    public void UpdateText(string newText)
    {
        octaveOffset = int.Parse(newText);
        SetTextOnAllChildren();
    }
    
}
