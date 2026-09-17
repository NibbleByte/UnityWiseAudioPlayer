using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DevLocker.Audio.Editor
{
	// Basically the same functionality.
	[CustomEditor(typeof(UIAudioTemplate), true)]
	[CanEditMultipleObjects]
	public class UIAudioTemplateEditor : UIAudioEffectsEditor
	{
	}

	[CustomEditor(typeof(UIAudioEffects), true)]
	[CanEditMultipleObjects]
	public class UIAudioEffectsEditor : UnityEditor.Editor
	{
		string[] s_AudioPropNames = new string[] {
			nameof(UIAudioEffects.SubmitAudio),
			nameof(UIAudioEffects.PointerClickAudio),
			nameof(UIAudioEffects.PointerDownAudio),
			nameof(UIAudioEffects.PointerUpAudio),
			nameof(UIAudioEffects.PointerEnterAudio),
			nameof(UIAudioEffects.PointerExitAudio),
			nameof(UIAudioEffects.SelectAudio),
			nameof(UIAudioEffects.DeselectAudio),
		};

		private List<string> m_PendingPropNames = new();

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

			var audioProps = s_AudioPropNames.Select(name => serializedObject.FindProperty(name)).ToList();
			var excludedPropNames = new List<string> { "m_Script" };

			foreach(var prop in audioProps) {
				var resourceProp = prop.FindPropertyRelative("m_" + nameof(AudioSourcePlayer.AudioReferenceProperty.AudioResource));
				var assetProp = prop.FindPropertyRelative("m_" + nameof(AudioSourcePlayer.AudioReferenceProperty.AudioAsset));

				if (resourceProp.objectReferenceValue == null && assetProp.objectReferenceValue == null && !m_PendingPropNames.Contains(prop.name)) {
					excludedPropNames.Add(prop.name);
				}
			}

			DrawPropertiesExcluding(serializedObject, excludedPropNames.ToArray());

			EditorGUILayout.Space();

			if (GUILayout.Button("Add Event Sound")) {
				var menu = new GenericMenu();

				// Skip "m_Script"
				foreach(var excludedPropName in excludedPropNames.Skip(1)) {

					var pendingName = excludedPropName;
					menu.AddItem(new GUIContent(excludedPropName), false, () => {
						m_PendingPropNames.Add(pendingName);
					});
				}

				menu.ShowAsContext();
			}

			serializedObject.ApplyModifiedProperties();
		}
	}

}
