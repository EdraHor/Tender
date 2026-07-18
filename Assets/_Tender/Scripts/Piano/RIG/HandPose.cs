using UnityEngine;
using System;

[Serializable]
public class FingerPose
{
    public Vector3 Bone1Rotation = Vector3.zero;
    public Vector3 Bone2Rotation = Vector3.zero;
    public Vector3 Bone3Rotation = Vector3.zero;
    
    public void Apply(Transform bone1, Transform bone2, Transform bone3)
    {
        if (bone1) bone1.localEulerAngles = Bone1Rotation;
        if (bone2) bone2.localEulerAngles = Bone2Rotation;
        if (bone3) bone3.localEulerAngles = Bone3Rotation;
    }
    
    public void Capture(Transform bone1, Transform bone2, Transform bone3)
    {
        if (bone1) Bone1Rotation = bone1.localEulerAngles;
        if (bone2) Bone2Rotation = bone2.localEulerAngles;
        if (bone3) Bone3Rotation = bone3.localEulerAngles;
    }
    
    public void Lerp(FingerPose target, float t, Transform bone1, Transform bone2, Transform bone3)
    {
        if (bone1) bone1.localEulerAngles = Vector3.Lerp(bone1.localEulerAngles, target.Bone1Rotation, t);
        if (bone2) bone2.localEulerAngles = Vector3.Lerp(bone2.localEulerAngles, target.Bone2Rotation, t);
        if (bone3) bone3.localEulerAngles = Vector3.Lerp(bone3.localEulerAngles, target.Bone3Rotation, t);
    }
}

[Serializable]
public class HandPose
{
    public string PoseName = "Default";
    
    [Header("Hand Position (relative to IK target)")]
    public Vector3 HandOffset = Vector3.zero;
    
    [Header("Fingers")]
    public FingerPose Thumb = new FingerPose();
    public FingerPose Index = new FingerPose();
    public FingerPose Middle = new FingerPose();
    public FingerPose Ring = new FingerPose();
    public FingerPose Pinky = new FingerPose();
}