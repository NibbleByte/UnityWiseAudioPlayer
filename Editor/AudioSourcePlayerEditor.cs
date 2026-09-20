using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace DevLocker.Audio.Editor
{
	[CustomPropertyDrawer(typeof(AudioSourcePlayer.RepeatOptions))]
	public class RepeatOptionsDrawer : PropertyDrawer
	{
		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			var patternProp = property.FindPropertyRelative(nameof(AudioSourcePlayer.RepeatOptions.Pattern));
			var patternType = (AudioSourcePlayer.RepeatPatternType)patternProp.intValue;

			if (patternType != AudioSourcePlayer.RepeatPatternType.RepeatInterval) {
				return EditorGUIUtility.singleLineHeight;
			}

			return EditorGUIUtility.singleLineHeight * 2 + EditorGUIUtility.standardVerticalSpacing;
		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			label = EditorGUI.BeginProperty(position, label, property);

			position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

			var patternProp = property.FindPropertyRelative(nameof(AudioSourcePlayer.RepeatOptions.Pattern));
			var patternType = (AudioSourcePlayer.RepeatPatternType)patternProp.intValue;

			var patternRect = position;
			patternRect.height = EditorGUIUtility.singleLineHeight;
			EditorGUI.PropertyField(patternRect, patternProp, GUIContent.none);

			if (patternType == AudioSourcePlayer.RepeatPatternType.RepeatInterval) {
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
				EditorGUI.PropertyField(minValue, property.FindPropertyRelative(nameof(AudioSourcePlayer.RepeatOptions.MinSeconds)), new GUIContent("Min"));
				EditorGUI.PropertyField(maxValue, property.FindPropertyRelative(nameof(AudioSourcePlayer.RepeatOptions.MaxSeconds)), new GUIContent("Max"));
				EditorGUIUtility.labelWidth = oldLabelWidth;
			}

			EditorGUI.EndProperty();
		}
	}

	[CustomPropertyDrawer(typeof(AudioSourcePlayer.AudioReferenceProperty))]
	public class AudioReferencePropertyDrawer : PropertyDrawer
	{
		private bool m_UseAudioAsset = true;

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			return EditorGUIUtility.singleLineHeight;
		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			label = EditorGUI.BeginProperty(position, label, property);

			position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

			var audioAssetProp = property.FindPropertyRelative("m_AudioAsset");
			var audioResourceProp = property.FindPropertyRelative("m_AudioResource");

			// Calculate rect for configuration button
			var buttonRect = position;
			var popupStyle = new GUIStyle("PaneOptions") { imagePosition = ImagePosition.ImageOnly };
			buttonRect.yMin += popupStyle.margin.top + 1f;
			buttonRect.width = popupStyle.fixedWidth + popupStyle.margin.right;
			buttonRect.height = EditorGUIUtility.singleLineHeight;
			position.xMin = buttonRect.xMax;

			if (audioAssetProp.objectReferenceValue || audioResourceProp.objectReferenceValue) {
				m_UseAudioAsset = audioResourceProp.objectReferenceValue == null;
			}

			using (var check = new EditorGUI.ChangeCheckScope()) {
				var newPopupIndex = EditorGUI.Popup(buttonRect, new GUIContent(""), m_UseAudioAsset ? 0 : 1, new [] { new GUIContent("Use Audio Asset"), new GUIContent("Use Audio Resource") }, popupStyle);
				if (check.changed) {
					m_UseAudioAsset = newPopupIndex == 0;

					// Clear both in case of multi-selection.
					audioAssetProp.objectReferenceValue = null;
					audioResourceProp.objectReferenceValue = null;
				}
			}

			if (m_UseAudioAsset) {
				EditorGUI.BeginChangeCheck();

				EditorGUI.PropertyField(position, audioAssetProp, GUIContent.none);

				// Needed for multi-selection with different reference types used
				if (EditorGUI.EndChangeCheck()) {
					audioResourceProp.objectReferenceValue = null;
					m_UseAudioAsset = true;
				}

			} else {
				EditorGUI.BeginChangeCheck();

				EditorGUI.PropertyField(position, audioResourceProp, GUIContent.none);

				// Needed for multi-selection with different reference types used
				if (EditorGUI.EndChangeCheck()) {
					audioAssetProp.objectReferenceValue = null;
					m_UseAudioAsset = false;
				}
			}

			EditorGUI.EndProperty();
		}
	}

	[CustomEditor(typeof(AudioSourcePlayer), true)]
	[CanEditMultipleObjects]
	public class AudioSourcePlayerEditor : UnityEditor.Editor
	{
		private Vector2 m_ContextScrollPos;
		private bool m_ContextFolded = false;

		protected void DrawScriptProperty()
		{
			EditorGUI.BeginDisabledGroup(true);
			EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
			EditorGUI.EndDisabledGroup();
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			DrawScriptProperty();

			EditorGUI.BeginChangeCheck();

			var audioAssetProp = serializedObject
				.FindProperty("m_" + nameof(AudioSourcePlayer.AudioReference))
				.FindPropertyRelative("m_" + nameof(AudioSourcePlayer.AudioReferenceProperty.AudioAsset))
				;

			// Will draw any child properties without [HideInInspector] attribute.
			if (audioAssetProp.objectReferenceValue == null) {
				DrawPropertiesExcluding(serializedObject, "m_Script");
			} else {
				DrawPropertiesExcluding(serializedObject, "m_Script",
					"m_" + nameof(AudioSourcePlayer.RepeatPattern),
					"m_" + nameof(AudioSourcePlayer.Output),
					"m_" + nameof(AudioSourcePlayer.Template)
					);
			}

			if (EditorGUI.EndChangeCheck()) {
				serializedObject.ApplyModifiedProperties();
			}

			EditorGUILayout.BeginHorizontal();

			var player = serializedObject.targetObject as AudioSourcePlayer;

			Color prevColor = GUI.color;
			string playingHint = "Not Playing";

			bool isPlaying = player?.IsPlaying ?? false;
			if (isPlaying) {
				GUI.color = Color.green;
				playingHint = "Playing";
			}
			bool isPaused = player?.IsPaused ?? false;
			if (isPaused) {
				GUI.color = Color.yellow;
				playingHint = "Paused";
			}

			EditorGUILayout.LabelField(" ", playingHint, EditorStyles.helpBox, GUILayout.Width(63f));
			GUI.color = prevColor;

			if (GUILayout.Button("Open Audio Monitor", GUILayout.ExpandWidth(false))) {
				AudioSourcePlayerMonitorWindow.ShowMonitor();
			}

			EditorGUILayout.EndHorizontal();

			if (Application.isPlaying && player.ConductorsFilterContext != null) {
				if (player.ConductorsFilterContext is IEnumerable<KeyValuePair<string, object>> enumerableContext) {

					m_ContextFolded = EditorGUILayout.Foldout(m_ContextFolded, "Context Values", toggleOnLabelClick: true);
					if (m_ContextFolded) {
						EditorGUI.indentLevel++;

						m_ContextScrollPos = EditorGUILayout.BeginScrollView(m_ContextScrollPos, EditorStyles.helpBox);
						foreach (var pair in enumerableContext) {
							DrawPair(pair.Key, pair.Value);
						}
						EditorGUILayout.EndScrollView();

						EditorGUI.indentLevel--;
					}
				}
			}
		}

		private static void DrawPair(string key, object value)
		{
			if (value is int) {
				EditorGUILayout.IntField(key, (int)value);
			}
			if (value is float || value is double) {
				EditorGUILayout.FloatField(key, (float)value);
			}
			if (value is bool) {
				EditorGUILayout.Toggle(key, (bool)value);
			}
			if (value is string) {
				EditorGUILayout.TextField(key, (string)value);
			}
			if (value is Object) {
				EditorGUILayout.ObjectField(key, (Object)value, value.GetType(), allowSceneObjects: false);
			}
		}
	}

}
