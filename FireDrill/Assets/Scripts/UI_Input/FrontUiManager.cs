using TMPro;
using UnityEngine;

public class FrontUiManager : MonoBehaviour
{
    [SerializeField] private TMP_Text frontUiTitle; 
    [SerializeField] private TMP_Text frontUiMainText; 
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        frontUiTitle.text = "消火器を使おう";
        frontUiMainText.text = "1. ピンを抜く\n2. ホースをつかむ\n3.レバーを握る";
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void changeFrontUiTitle(string text)
    {
        if(text.Length < 7)
        {
            frontUiTitle.text = text;
        } else
        {
            Debug.Log("Too long Title");
        }
        
    }
    public void changeFrontUiMainText(string text)
    {
        frontUiMainText.text = text;
    }
}
