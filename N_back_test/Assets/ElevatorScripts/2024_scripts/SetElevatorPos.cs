using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;

// エレベーター位置設定のスクリプト
public class SetElevatorPos : MonoBehaviour
{
    [SerializeField] private InputActionAsset inputActions;

    private InputAction buttonA;
    private InputAction buttonB;
    private InputAction buttonX;
    private InputAction buttonY;
    private InputAction stickR;
    private InputAction stickL;



    bool isSetPos = false; // エレベーターの位置が確定したか
    // Start is called before the first frame update

    private void Awake()
    {
        var map = inputActions.FindActionMap("XRControllerInput");

        buttonA = map.FindAction("Button_A");
        buttonB = map.FindAction("Button_B");
        buttonX = map.FindAction("Button_X");
        buttonY = map.FindAction("Button_Y");

        stickR = map.FindAction("Stick_Right");
        stickL = map.FindAction("Stick_Left");

    }

    private void OnEnable()
    {
        buttonA.Enable();
        buttonB.Enable();
        buttonX.Enable();
        buttonY.Enable();
        stickR.Enable();
        stickL.Enable();
    }

    private void OnDisable()
    {
        buttonA.Disable();
        buttonB.Disable();
        buttonX.Disable();
        buttonY.Disable();
        stickR.Disable();
        stickL.Disable();
    }

    void Start()
    {
        
    }

    private void FixedUpdate() {
        // // 左手のアナログスティックの向きを取得
        // Vector2 stickL = OVRInput.Get(OVRInput.RawAxis2D.LThumbstick);
        // // 右手のアナログスティックの向きを取得
        // Vector2 stickR = OVRInput.Get(OVRInput.RawAxis2D.RThumbstick);
        Vector2 stickVec_R = stickR.ReadValue<Vector2>();
        Vector2 stickVec_L = stickL.ReadValue<Vector2>();


        // Bボタンが押されたら，エレベーターの位置確定
        if (buttonB.WasPressedThisFrame())
        {
            Debug.Log("Bボタンを押した");
            isSetPos = true;
        }
        if (isSetPos != true)
        {
            // エレベータの位置変更　右コントローラー
            if (stickVec_R.x != 0 || stickVec_R.y != 0)
            {
                var direction = new Vector3(stickVec_R.x, 0, stickVec_R.y);
                this.transform.Translate(5f * direction * Time.deltaTime);
            }
            // エレベータの向き変更　左コントローラー
            if (stickVec_L.x != 0)
            {
                this.transform.Rotate(0f, 40f * stickVec_L.x * Time.deltaTime, 0f);
            }
        }
    }
}
