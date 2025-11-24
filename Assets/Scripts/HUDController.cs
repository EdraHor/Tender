using TMPro;
using UnityEngine;

public class HUDController : MonoBehaviour
{
    public TextMeshProUGUI InteractDescText;

    private void Awake()
    {
        G.HUDController = this;
    }

    public void ShowInteractionDesc(string desc)
    {
        if (InteractDescText)
        {
            InteractDescText.text = desc;
        }
    }

    public void HideInteractionDesc()
    {
        if (InteractDescText)
        {
            InteractDescText.text = "";
        }
    }
}
