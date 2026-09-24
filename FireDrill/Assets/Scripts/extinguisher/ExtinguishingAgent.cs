using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

//[RequireComponent(typeof(ParticleSystem))]
public class ExtinguishingAgent : MonoBehaviour
{
    public ParticleSystem particleSystemExtinguisher;
    public ParticleSystem particleSystemPowder;
    public XRInputReader xrInputReader;
    public float maxDischargingTime = 15.0f;
    public float powderAppearTime = 7.5f;
    private float countDischargingTime = 0.0f;

    private bool isInitialized = false;
    private bool isPowderAppeared = false;
    private bool isDischarging = false;
    private bool isDischargeEnd = false;


    // Triggerに入ったParticleを保存
    private readonly List<ParticleSystem.Particle> enterParticles = new List<ParticleSystem.Particle>();

    // ヒットボックス内に残っている粒子を取得するための再利用リスト。
    private readonly List<ParticleSystem.Particle> insideParticles = new List<ParticleSystem.Particle>();

    private void Start()
    {
        //xrInputReader = new XRInputReader();
        //particleSystemExtinguisher = GetComponent<ParticleSystem>();

        // 進入時の消火に加え、内部に残る粒子でも炎の成長を止める。
        var trigger = particleSystemExtinguisher.trigger;
        trigger.inside = ParticleSystemOverlapAction.Callback;
        // 複数のヒットボックスに重なった場合も、接触先をすべて取得できるようにする。
        trigger.colliderQueryMode = ParticleSystemColliderQueryMode.All;

        // 最初はParticleを停止
        particleSystemExtinguisher.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        particleSystemPowder.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);


        // XRInputReaderがAwakeで取得した右トリガーActionにイベントを登録
        RegisterInputEvents();

        isInitialized = true;

        isPowderAppeared = false;
        isDischarging = false;
        isDischargeEnd = false;
        
        countDischargingTime = 0.0f;
    }

    private void Update()
    {
        if (countDischargingTime > maxDischargingTime)
        {
            isDischargeEnd = true;
            // すでに出ているParticleは残して、新規放出だけ止める
            particleSystemExtinguisher.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        else if (isDischarging)
        {
            countDischargingTime += Time.deltaTime;

            if((countDischargingTime > powderAppearTime) && !isPowderAppeared)
            {
                particleSystemPowder.Play();
                isPowderAppeared = true;

            }
        }
    }

    private void OnEnable()
    {
        // 初回のOnEnableはStartより先に呼ばれるため何もしない
        // 一度Startした後に再度有効化された場合だけイベントを再登録
        if (isInitialized)
        {
            RegisterInputEvents();
        }
    }

    private void OnDisable()
    {
        // Start前に無効化された場合を考慮
        if (isInitialized)
        {
            UnregisterInputEvents();
        }
    }



    private void OnParticleTrigger()
    {
        // 進入後も内部に残っている粒子と、その接触先を取得する。
        int insideCount = particleSystemExtinguisher.GetTriggerParticles(
            ParticleSystemTriggerEventType.Inside,
            insideParticles,
            out ParticleSystem.ColliderData insideColliderData
        );

        // 各粒子が触れているすべての炎に、現在も消火剤が接触中であることを通知する。
        for (int i = 0; i < insideCount; i++)
        {
            for (int j = 0; j < insideColliderData.GetColliderCount(i); j++)
            {
                Component hitCollider = insideColliderData.GetCollider(i, j);
                if (hitCollider == null)
                {
                    continue;
                }

                FireHitBox fireHitBox = hitCollider.GetComponentInParent<FireHitBox>();
                if (fireHitBox != null)
                {
                    // 滞在中は成長停止だけを通知し、進入時の消火量を重複して加算しない。
                    fireHitBox.NotifyExtinguishingAgentContact();
                }
            }
        }

        // Triggerに入ったParticleと、そのParticleが触れたCollider情報を取得
        int count = particleSystemExtinguisher.GetTriggerParticles(
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
                FireHitBox fireHitBox = hitCollider.GetComponentInParent<FireHitBox>();

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
        if (!isDischargeEnd)
        {
            particleSystemExtinguisher.Play();
            isDischarging = true;
        }
    }

    // トリガーを離したら噴射停止
    private void OnTriggerReleased(InputAction.CallbackContext context)
    {
        // すでに出ているParticleは残して、新規放出だけ止める
        particleSystemExtinguisher.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        isDischarging = false;
    }
}
