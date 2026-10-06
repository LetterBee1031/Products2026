using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DoorOpen : MonoBehaviour
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
        Debug.Log("スタート");
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void Open()
    {
        Debug.Log("押したよ");
        //エレベーター側のドアが開いているか
        if ((ElevatorDoorRightAnimator.GetBool("Open") == false) && (ElevatorDoorLeftAnimator.GetBool("Open") == false))
        {
            //建物側のドアが開いているか
            if ((BuildingDoorRightAnimator.GetBool("Open") == false) && (BuildingDoorLeftAnimator.GetBool("Open") == false))
            {
                //ドアを開ける
                ElevatorDoorRightAnimator.SetBool("Open", true);
                ElevatorDoorLeftAnimator.SetBool("Open", true);
                BuildingDoorRightAnimator.SetBool("Open", true);
                BuildingDoorLeftAnimator.SetBool("Open", true);

            }


        }
    }
}

