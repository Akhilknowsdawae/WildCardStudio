using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ScoreManager))]
public class ScoreManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawPropertiesExcluding(
            serializedObject,
            "playedCardsParent",
            "playPositionReference",
            "playPosition",
            "fanSpread",
            "cardSpacing",
            "verticalSpacing",
            "updatePlayedCardPositionsContinuously");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Card Layout", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("playedCardsParent"));
        SerializedProperty playPositionReference = serializedObject.FindProperty("playPositionReference");
        SerializedProperty playPosition = serializedObject.FindProperty("playPosition");
        if (playPositionReference != null)
        {
            EditorGUILayout.PropertyField(playPositionReference);
        }

        if (playPosition != null)
        {
            bool hasReference = playPositionReference != null && playPositionReference.objectReferenceValue != null;
            using (new EditorGUI.DisabledScope(hasReference))
            {
                EditorGUILayout.PropertyField(playPosition);
            }
        }
        EditorGUILayout.PropertyField(serializedObject.FindProperty("fanSpread"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("cardSpacing"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("verticalSpacing"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("updatePlayedCardPositionsContinuously"));

        serializedObject.ApplyModifiedProperties();
    }
}
