using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(PianoFingerController))]
public class PianoFingerControllerEditor : Editor
{
    private PianoFingerController _target;

    private void OnEnable()
    {
        _target = (PianoFingerController)target;
    }

    public override void OnInspectorGUI()
    {
        // Рисуем стандартный инспектор
        DrawDefaultInspector();

        GUILayout.Space(20);
        GUILayout.Label("Setup Tools", EditorStyles.boldLabel);

        if (GUILayout.Button("Auto Find Bones (Mixamo Rig)", GUILayout.Height(30)))
        {
            Undo.RecordObject(_target, "Auto Find Bones");
            _target.FindBonesAutomatic(_target.RootBone);
            EditorUtility.SetDirty(_target);
        }

        GUILayout.Space(10);
        GUILayout.Label("Debug Controls (Play Mode Only)", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to test finger pressing.", MessageType.Info);
        }
        else
        {
            GUILayout.BeginHorizontal();
            
            // Left Hand Test
            GUILayout.BeginVertical("box");
            GUILayout.Label("Left Hand (Index)", EditorStyles.centeredGreyMiniLabel);
            if (GUILayout.Button("Press"))
            {
                _target.PressFinger(HandSide.Left, FingerType.Index);
            }
            if (GUILayout.Button("Release"))
            {
                _target.ReleaseFinger(HandSide.Left, FingerType.Index);
            }
            GUILayout.EndVertical();

            // Right Hand Test
            GUILayout.BeginVertical("box");
            GUILayout.Label("Right Hand (Index)", EditorStyles.centeredGreyMiniLabel);
            if (GUILayout.Button("Press"))
            {
                _target.PressFinger(HandSide.Right, FingerType.Index);
            }
            if (GUILayout.Button("Release"))
            {
                _target.ReleaseFinger(HandSide.Right, FingerType.Index);
            }
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            
            // Debug Info Readout
            GUILayout.Space(10);
            EditorGUILayout.LabelField("Current States:", EditorStyles.boldLabel);
            DrawFingerState("L Index", _target.LeftHand.GetFinger(FingerType.Index));
            DrawFingerState("R Index", _target.RightHand.GetFinger(FingerType.Index));
        }
    }

    private void DrawFingerState(string label, FingerChain finger)
    {
        if (finger == null) return;
        EditorGUILayout.LabelField(label, 
            $"Pressed: {finger.IsPressed} | Bend: {finger.CurrentBendValue:F2}");
        
        // Прогресс бар
        Rect r = EditorGUILayout.GetControlRect();
        EditorGUI.ProgressBar(r, finger.CurrentBendValue, "Bend Amount");
    }
}