using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Yarn.Unity;

public class DialogueClickArea : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private LineAdvancer lineAdvancer;
    
    // Вызывается только когда кликают именно на этот Image
    public void OnPointerClick(PointerEventData eventData)
    {
        lineAdvancer.RequestLineHurryUp();
    }
}
