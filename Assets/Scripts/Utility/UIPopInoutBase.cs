using UnityEngine;
using DG.Tweening;
using UnityEngine.UI;


public class UIPopInoutBase : MonoBehaviour
{
    public  RectTransform rectTransform; 
    [SerializeField] private float popInScale = 1f;
    [SerializeField] private float popOutScale = 0f;
    [SerializeField] private float animDuration = 0.25f;
    public bool isVisible = false;



    protected void PopIn()
    {
        isVisible = true;
        rectTransform.DOKill();
        OnCompleteAnim();
        rectTransform.DOScale(popInScale, animDuration).SetEase(Ease.OutBack);
    }

    protected void PopOut()
    {
        isVisible = false;
        rectTransform.DOKill();
        rectTransform.DOScale(popOutScale, animDuration).SetEase(Ease.InBack).OnComplete(OnCompleteAnim);
    }

    protected virtual void  OnCompleteAnim()
    {
        rectTransform.gameObject.SetActive(isVisible);
    }

    public void TogglePop(bool visible)
    {
        if(visible)
        {
            PopIn();
        }
        else
        {
            PopOut();
        }
    }
}
