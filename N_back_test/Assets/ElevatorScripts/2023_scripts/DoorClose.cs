using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DoorClose : MonoBehaviour
{
    [SerializeField]
    [Tooltip("エレベータードア(右)のアニメーター")]
    private Animator ElevatorDoorRightAnimator;

    [SerializeField]
    [Tooltip("エレベータードア(左)のアニメーター")]
    private Animator ElevatorDoorLeftAnimator;

    [SerializeField]
    [Tooltip("建物のドア(右)のアニメーター")]
    private Animator BuildingDoorRightAnimator;

    [SerializeField]
    [Tooltip("建物のドア(右)のアニメーター")]
    private Animator BuildingDoorLeftAnimator;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void Close()
    {
        Debug.Log("押したよ");
        if ((ElevatorDoorRightAnimator.GetBool("Open") == true) && (ElevatorDoorLeftAnimator.GetBool("Open") == true))
        {
            if ((BuildingDoorRightAnimator.GetBool("Open") == true) && (BuildingDoorLeftAnimator.GetBool("Open") == true))
            {
                ElevatorDoorRightAnimator.SetBool("Open", false);
                ElevatorDoorLeftAnimator.SetBool("Open", false);
                BuildingDoorRightAnimator.SetBool("Open", false);
                BuildingDoorLeftAnimator.SetBool("Open", false);
            }
        }
    }
}

