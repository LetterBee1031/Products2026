using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(ParticleSystem))]
public class ExtinguishingAgent : MonoBehaviour
{
    private ParticleSystem particleSystem;
    public XRInputReader xrInputReader;
    private bool initialized = false;

    // Triggerに入ったParticleを保存
    private readonly List<ParticleSystem.Particle> enterParticles
        = new List<ParticleSystem.Particle>();

    private void Start()
    {
        //xrInputReader = new XRInputReader();
        particleSystem = GetComponent<ParticleSystem>();

        // 最初はParticleを停止
        particleSystem.Stop(
            true,
            ParticleSystemStopBehavior.StopEmittingAndClear
        );


        // XRInputReaderがAwakeで取得した右トリガーActionにイベントを登録
        RegisterInputEvents();

        initialized = true;
    }

    private void OnEnable()
    {
        // 初回のOnEnableはStartより先に呼ばれるため何もしない
        // 一度Startした後に再度有効化された場合だけイベントを再登録
        if (initialized)
        {
            RegisterInputEvents();
        }
    }

    private void OnDisable()
    {
        // Start前に無効化された場合を考慮
        if (initialized)
        {
            UnregisterInputEvents();
        }
    }

    

    private void OnParticleTrigger()
    {
        // Triggerに入ったParticleと、そのParticleが触れたCollider情報を取得
        int count = particleSystem.GetTriggerParticles(
            ParticleSystemTriggerEventType.Enter,
            enterParticles,
            out ParticleSystem.ColliderData colliderData
        );

        for (int i = 0; i < count; i++)
        {
            // このParticleが接触したCollider数を取得
            int colliderCount = colliderData.GetColliderCount(i);

            for (int j = 0; j < colliderCount; j++)
            {
                // Particleが接触したColliderを取得
                Component hitCollider = colliderData.GetCollider(i, j);

                if (hitCollider == null)
                {
                    continue;
                }

                // Colliderが属している炎のHitBoxを取得
                FireHitBox fireHitBox =
                    hitCollider.GetComponentInParent<FireHitBox>();

                if (fireHitBox == null)
                {
                    continue;
                }

                // この炎に消火剤が1粒当たったことを通知
                fireHitBox.HitExtinguishingAgent(1);
            }
        }
    }
    private void RegisterInputEvents()
    {
        xrInputReader.buttonTriggerRight.performed += OnTriggerPressed;
        xrInputReader.buttonTriggerRight.canceled += OnTriggerReleased;
    }

    // 右トリガーのイベント登録を解除
    private void UnregisterInputEvents()
    {
        xrInputReader.buttonTriggerRight.performed -= OnTriggerPressed;
        xrInputReader.buttonTriggerRight.canceled -= OnTriggerReleased;
    }
    private void OnTriggerPressed(InputAction.CallbackContext context)
    {
        particleSystem.Play();
    }

    // トリガーを離したら噴射停止
    private void OnTriggerReleased(InputAction.CallbackContext context)
    {
        // すでに出ているParticleは残して、新規放出だけ止める
        particleSystem.Stop(
            true,
            ParticleSystemStopBehavior.StopEmitting
        );
    }
}