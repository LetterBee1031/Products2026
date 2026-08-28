using UnityEngine;

public class MoveTests : MonoBehaviour
{
    public GameObject panelN_back;
    public GameObject panelStroop;
    public GameObject panelMentalArith;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        panelN_back.SetActive(true);
        panelStroop.SetActive(false);
        panelMentalArith.SetActive(false);
    }

    public void SetPanelN_back()
    {
        panelN_back.SetActive(true);
        panelStroop.SetActive(false);
        panelMentalArith.SetActive(false);
    }

    public void SetPanelStroop()
    {
        panelN_back.SetActive(false);
        panelStroop.SetActive(true);
        panelMentalArith.SetActive(false);
    }

    public void SetPanelMentalArith()
    {
        panelN_back.SetActive(false);
        panelStroop.SetActive(false);
        panelMentalArith.SetActive(true);
    }
}
