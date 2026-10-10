using DevLocker.Audio.Utils;
using UnityEditor;
using UnityEngine;

namespace DevLocker.Audio.Editor
{
	[CustomPropertyDrawer(typeof(AudioPlaybackSettings.RepeatSettings))]
	public class RepeatOptionsDrawer : PropertyDrawer
	{
		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			var modeProp = property.FindPropertyRelative(nameof(AudioPlaybackSettings.RepeatSettings.Mode));
			var modeType = (AudioPlaybackSettings.RepeatMode)modeProp.intValue;

			if (modeType != AudioPlaybackSettings.RepeatMode.LoopWithInterval) {
				return EditorGUIUtility.singleLineHeight;
			}

			return EditorGUIUtility.singleLineHeight * 2 + EditorGUIUtility.standardVerticalSpacing;
		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			label = EditorGUI.BeginProperty(position, label, property);

			position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

			var modeProp = property.FindPropertyRelative(nameof(AudioPlaybackSettings.RepeatSettings.Mode));
			var modeType = (AudioPlaybackSettings.RepeatMode)modeProp.intValue;

			var patternRect = position;
			patternRect.height = EditorGUIUtility.singleLineHeight;
			EditorGUI.PropertyField(patternRect, modeProp, GUIContent.none);

			if (modeType == AudioPlaybackSettings.RepeatMode.LoopWithInterval) {
				var intervalLineRect = position;
				intervalLineRect.y = position.y + position.height - EditorGUIUtility.singleLineHeight;
				intervalLineRect.height = EditorGUIUtility.singleLineHeight;

				float padding = 6f;
				Rect minValue = intervalLineRect;
				minValue.width = intervalLineRect.width / 2 - padding;

				Rect maxValue = intervalLineRect;
				maxValue.width = intervalLineRect.width / 2;
				maxValue.x += intervalLineRect.width / 2;
				float oldLabelWidth = EditorGUIUtility.labelWidth;
				EditorGUIUtility.labelWidth = 30f;
				EditorGUI.PropertyField(minValue, property.FindPropertyRelative(nameof(AudioPlaybackSettings.RepeatSettings.MinSeconds)), new GUIContent("Min"));
				EditorGUI.PropertyField(maxValue, property.FindPropertyRelative(nameof(AudioPlaybackSettings.RepeatSettings.MaxSeconds)), new GUIContent("Max"));
				EditorGUIUtility.labelWidth = oldLabelWidth;
			}

			EditorGUI.EndProperty();
		}
	}

	[CustomPropertyDrawer(typeof(AudioCondition))]
	public class AudioConditionDrawer : WiseSerializeReferenceBasePropertyDrawerCOPY
	{
	}

	[CustomPropertyDrawer(typeof(AudioConductor))]
	public class AudioConductorDrawer : WiseSerializeReferenceBasePropertyDrawerCOPY
	{
	}
}
