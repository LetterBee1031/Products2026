using UnityEngine;
using UnityEngine.InputSystem;

public class XRInputReader : MonoBehaviour
{
    [SerializeField] private InputActionAsset inputActions;

    public InputAction buttonA;
    public InputAction buttonB;
    public InputAction buttonX;
    public InputAction buttonY;
    public InputAction buttonTriggerRight;
    public InputAction buttonTriggerLeft;
    public InputAction buttonGripRight;
    public InputAction buttonGripLeft;
    public InputAction stickRight;
    public InputAction stickLeft;

    private void Awake()
    {
        var map = inputActions.FindActionMap("XRControllerInput");

        buttonA = map.FindAction("Button_A");
        buttonB = map.FindAction("Button_B");
        buttonX = map.FindAction("Button_X");
        buttonY = map.FindAction("Button_Y");
        buttonTriggerRight = map.FindAction("Button_Trigger_Right");
        buttonTriggerLeft = map.FindAction("Button_Trigger_Left");
        buttonGripRight = map.FindAction("Button_Grip_Right");
        buttonGripLeft = map.FindAction("Button_Grip_Left");
        stickRight = map.FindAction("Stick_Right");
        stickLeft = map.FindAction("Stick_Left");
    }

    private void OnEnable()
    {
        buttonA.Enable();
        buttonB.Enable();
        buttonX.Enable();
        buttonY.Enable();
        buttonTriggerRight.Enable();
        buttonTriggerLeft.Enable(); 
        buttonGripRight.Enable();
        buttonGripLeft.Enable();
        stickRight.Enable();
        stickLeft.Enable();
    }

    private void OnDisable()
    {
        buttonA.Disable();
        buttonB.Disable();
        buttonX.Disable();
        buttonY.Disable();
        buttonTriggerRight.Disable();
        buttonTriggerLeft.Disable();
        buttonGripRight.Disable();
        buttonGripLeft.Disable();
        stickRight.Disable();
        stickLeft.Disable();
    }

    private void Update()
    {
        
    }
}