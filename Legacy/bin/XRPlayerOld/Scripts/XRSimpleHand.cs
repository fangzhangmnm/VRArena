﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SpatialTracking;
using UnityEngine.XR;
using UnityEngine.Events;


[RequireComponent(typeof(SphereCollider))]
public class XRSimpleHand : MonoBehaviour
{
    Transform trackingSpace { get { return transform.parent; } }
    public XRNode whichHand;
    public LayerMask interactionLayer;
    public float pickUpDistance = 1f;

    [System.Serializable]public class HapticSettings
    {
        public float hoveringEnterStrength = .2f;
        public float hoveringEnterDuration = .05f;
        public float hoveringExitStrength = .1f;
        public float hoveringExitDuration = .05f;
        public float attachStrength = .2f;
        public float attachDuration = .05f;
        public float detachStrength = .5f;
        public float detachDuration = .05f;
        public float refractoryPeriod= .5f;
    }
    public HapticSettings hapticSettings;
    float hapticRefractoryCD = 0f,hapticRefractoryLastStrength=0f;

    public Behaviour[] disableWhenAttaching;


    public UnityEvent onTriggerDown, onTriggerUp, onGripDown, onGripUp;


    public static XRSimpleHand leftHand, rightHand;

    [HideInInspector] public Quaternion rotation { get { return transform.rotation; } }
    [HideInInspector] public Vector3 position { get { return transform.position; } }
    [HideInInspector] public float input_trigger, input_grip;
    [HideInInspector] public bool input_trigger_button, input_grip_button;
    [HideInInspector] public XRSimpleInteractable attached;

    bool input_trigger_lastButton, input_grip_lastButton;
    
    SphereCollider sphereCollider;

    public void DetachIfAny()
    {
        if (attached)
        {
            _Detach();
        }
    }
    public void Attach(XRSimpleInteractable interactable)
    {
        if (attached != interactable)
        {
            DetachIfAny();
            _Attach(interactable,0);
        }
    }
    public void SendHapticImpulse(float strength, float duration)
    {
        if (hapticRefractoryCD > 0 && strength > hapticRefractoryLastStrength)
            hapticRefractoryCD = 0;
        if (hapticRefractoryCD > 0) return;
        hapticRefractoryCD = hapticSettings.refractoryPeriod;
        hapticRefractoryLastStrength = strength;
        
        var device = InputDevices.GetDeviceAtXRNode(whichHand);
        if(device!=null)device.SendHapticImpulse(0, strength, duration);
    }
    private void Awake()
    {
        Debug.Assert(trackingSpace != null);
        if (whichHand == XRNode.LeftHand) leftHand = this;
        if (whichHand == XRNode.RightHand) rightHand = this;
        sphereCollider = GetComponent<SphereCollider>();
    }

    private void FixedUpdate()
    {
        UpdateNodeState(doCount:true);
    }
    private void Update()
    {
        hapticRefractoryCD -= Time.unscaledDeltaTime;
        UpdateNodeState();
        UpdateInputState();
        UpdateHoveringInteractable();

        input_trigger_lastButton = input_trigger_button;
        input_trigger_button = input_trigger > .9f;
        if (input_trigger_lastButton && !input_trigger_button) _OnTriggerUp();
        if (!input_trigger_lastButton && input_trigger_button) _OnTriggerDown(); 

        input_grip_lastButton = input_grip_button;
        input_grip_button = input_grip > .9f;
        if (input_grip_lastButton && !input_grip_button) _OnGripUp();
        if (!input_grip_lastButton && input_grip_button) _OnGripDown();
    }
    void _Attach(XRSimpleInteractable interactable, float pickUpDistance)
    {
        if (interactable.CanAttach(this, pickUpDistance, out int priority))
        {
            attached = interactable;//Be careful with the order
            foreach (var b in disableWhenAttaching) b.enabled = false;
            SendHapticImpulse(hapticSettings.attachStrength, hapticSettings.attachDuration);
            interactable.OnAttach(this, pickUpDistance);//Be careful with the order
        }
    }
    void _Detach()
    {
        Debug.Assert(attached);
        SendHapticImpulse(hapticSettings.detachStrength, hapticSettings.detachDuration);
        attached.OnDetach(this);//Be careful with the order
        foreach (var b in disableWhenAttaching) b.enabled = true;
        attached = null;//Be careful with the order

    }

    void _OnGripDown()
    {
        if (!attached)
        {
            var maxP = int.MinValue; HoveringDesc maxG = new HoveringDesc() { interactable = null };

            foreach (var desc in hoveringInteractables)
            {
                if (desc.interactable.CanAttach(this,desc.pickUpDistance, out int priority))
                    if (priority > maxP)
                    {
                        maxP = priority; maxG = desc;
                    }
                //Debug.Log(g);
            }
            if (maxG.interactable)
            {
                //Debug.Log($"chose grabe {maxG} with priority {maxP}");
                _Attach(maxG.interactable,maxG.pickUpDistance);
            }
        }
        attached?.OnGripDown(this);
        onGripDown.Invoke();
    }
    void _OnGripUp()
    {
        attached?.OnGripUp(this);
        onGripUp.Invoke();
    }
    void _OnTriggerDown()
    {
        attached?.OnTriggerDown(this);
        onTriggerDown.Invoke();
    }
    void _OnTriggerUp()
    {
        attached?.OnTriggerUp(this);
        onTriggerUp.Invoke();
    }

    private void OnEnable()
    {
        Application.onBeforeRender += OnBeforeRender;
    }
    private void OnDisable()
    {
        DetachIfAny();
        Application.onBeforeRender -= OnBeforeRender;
    }
    void OnBeforeRender()
    {
        UpdateNodeState();
    }
    #region Hovering
    struct HoveringDesc
    {
        public XRSimpleInteractable interactable;
        public float pickUpDistance;
    }
    readonly List<HoveringDesc> hoveringInteractables = new List<HoveringDesc>();
    void UpdateHoveringInteractable()
    {
        var newHovering= new List<HoveringDesc>();
        var colliders = Physics.OverlapSphere(transform.TransformPoint(sphereCollider.center), transform.lossyScale.x * sphereCollider.radius, interactionLayer);
        foreach(var c in colliders)
        {
            var interactable = c.attachedRigidbody ? c.attachedRigidbody.GetComponent<XRSimpleInteractable>() : c.GetComponent<XRSimpleInteractable>();
            if (interactable && !newHovering.Exists(x => x.interactable == interactable))
                newHovering.Add(new HoveringDesc() { interactable = interactable, pickUpDistance = 0 });
        }
        if (newHovering.Count==0)
        {
            if(Physics.SphereCast(transform.position, transform.lossyScale.x * sphereCollider.radius, transform.forward, out RaycastHit hitInfo, transform.lossyScale.x * (pickUpDistance-sphereCollider.radius), interactionLayer))
            {
                var interactable = hitInfo.rigidbody ? hitInfo.rigidbody.GetComponent<XRSimpleInteractable>() : hitInfo.collider.GetComponent<XRSimpleInteractable>();
                if (interactable && !newHovering.Exists(x => x.interactable == interactable))
                {
                    float dist = Mathf.Max(0, hitInfo.distance );
                    newHovering.Add(new HoveringDesc() { interactable = interactable, pickUpDistance = dist });
                }
            }
        }
        foreach (var desc in hoveringInteractables)
            if (!newHovering.Exists(x => x.interactable == desc.interactable))
            {
                desc.interactable._OnHoveringExitHand(this);
                if(!attached)
                    SendHapticImpulse(hapticSettings.hoveringExitStrength, hapticSettings.hoveringExitDuration);
            }
        foreach (var desc in newHovering)
            if (!hoveringInteractables.Exists(x => x.interactable == desc.interactable))
            {
                desc.interactable._OnHoveringEnterHand(this);
                if (!attached)
                    SendHapticImpulse(hapticSettings.hoveringEnterStrength, hapticSettings.hoveringEnterDuration);
            }
        hoveringInteractables.Clear();
        foreach(var interactable in newHovering)
            hoveringInteractables.Add(interactable);
    }
    #endregion


    #region XRDevices
    readonly List<XRNodeState> xrNodeStates = new List<XRNodeState>();
    public void UpdateNodeState(bool doCount = false)
    {
        InputTracking.GetNodeStates(xrNodeStates);
        foreach (var s in xrNodeStates)
        {
            if (s.nodeType == whichHand)
            {
                if (s.TryGetPosition(out Vector3 pos)) transform.position = trackingSpace.TransformPoint(pos);
                if (s.TryGetRotation(out Quaternion rot)) transform.rotation = trackingSpace.rotation * rot;
            }
        }
    }
    public void UpdateInputState()
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(whichHand);
        if (device != null)
        {
            if (!device.TryGetFeatureValue(CommonUsages.trigger, out input_trigger)) input_trigger = 0;
            if (!device.TryGetFeatureValue(CommonUsages.grip, out input_grip)) input_grip = 0;
        }
    }
    #endregion




    #region DEBUG
    public bool IsInLayerMask(GameObject obj, LayerMask layerMask)=>((layerMask.value & (1 << obj.layer)) > 0);

    void ShowVector(Vector3 vector)
    {
        var vectorRenderer = GetComponent<LineRenderer>();
        if (!vectorRenderer) vectorRenderer = gameObject.AddComponent<LineRenderer>();
        vectorRenderer.enabled = true;
        vectorRenderer.positionCount = 2;
        vectorRenderer.startWidth = vectorRenderer.endWidth = .01f;
        vectorRenderer.useWorldSpace = true;
        vectorRenderer.SetPosition(0, transform.position);
        vectorRenderer.SetPosition(1, transform.position + vector);
    }
    public override string ToString() => whichHand.ToString();
    #endregion
}
public abstract class XRSimpleInteractable : MonoBehaviour
{
    public abstract bool CanAttach(XRSimpleHand emptyHand, float pickUpDistance, out int priority);
    public abstract void OnAttach(XRSimpleHand handAttachedMe, float pickUpDistance);
    public abstract void OnDetach(XRSimpleHand handAttachedMe);

    public virtual void OnGripDown(XRSimpleHand hand) { }
    public virtual void OnGripUp(XRSimpleHand hand) { }
    public virtual void OnTriggerDown(XRSimpleHand hand) { }
    public virtual void OnTriggerUp(XRSimpleHand hand) { }

    public virtual void OnHoverEnter() { }
    public virtual void OnHoverExit() { }
    public virtual void OnValidate()
    {
        if (gameObject.layer == 0)
            Debug.LogWarning("Did you forgot to set the layer for this interactable?");
    }
    public virtual bool IsOverrideControllerInput() => false;
    #region HoveringDetails
    private List<XRSimpleHand> _hoveringHands = new List<XRSimpleHand>();
    public void _OnHoveringEnterHand(XRSimpleHand hand)
    {
        if (_hoveringHands.Count == 0)
            OnHoverEnter();
        _hoveringHands.Add(hand);
    }
    public void _OnHoveringExitHand(XRSimpleHand hand)
    {
        if (_hoveringHands.Count > 0)
        {
            _hoveringHands.RemoveAll(h => h == hand);
            if (_hoveringHands.Count == 0)
                OnHoverExit();
        }
    }
    #endregion
}