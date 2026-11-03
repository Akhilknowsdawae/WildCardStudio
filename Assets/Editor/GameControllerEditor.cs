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
            "playAreaPadding",
            "fanSpread",
            "cardSpacing",
            "verticalSpacing",
            "updatePlayedCardPositionsContinuously",
            "scoreCardsAlphaWhenHandDown");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Card Layout", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("playedCardsParent"));
        SerializedProperty playPositionReference = serializedObject.FindProperty("playPositionReference");
        SerializedProperty playPosition = serializedObject.FindProperty("playPosition");
        SerializedProperty playAreaPadding = serializedObject.FindProperty("playAreaPadding");
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
        if (playAreaPadding != null)
        {
            EditorGUILayout.PropertyField(playAreaPadding);
        }
        EditorGUILayout.PropertyField(serializedObject.FindProperty("fanSpread"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("cardSpacing"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("verticalSpacing"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("updatePlayedCardPositionsContinuously"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("scoreCardsAlphaWhenHandDown"));

        serializedObject.ApplyModifiedProperties();
    }
}
