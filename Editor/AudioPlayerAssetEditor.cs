using DevLocker.Utils;
using UnityEditor;
using UnityEngine;

namespace DevLocker.Audio.Editor
{
	[CustomEditor(typeof(AudioPlayerAsset))]
	[CanEditMultipleObjects]
	public class AudioPlayerAssetEditor : UnityEditor.Editor
	{
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

			var loopRepeatProperty = serializedObject.FindProperty(nameof(AudioPlayerAsset.LoopRepeat));

			// Will draw any child properties without [HideInInspector] attribute.
			if (loopRepeatProperty.boolValue) {
				DrawPropertiesExcluding(serializedObject, "m_Script");
			} else {
				DrawPropertiesExcluding(serializedObject, "m_Script", nameof(AudioPlayerAsset.RepeatIntervalRange));
			}

			if (EditorGUI.EndChangeCheck()) {
				serializedObject.ApplyModifiedProperties();
			}
		}
	}

	[CustomPropertyDrawer(typeof(AudioPlayerAsset.AudioPredicate))]
	public class AudioPredicateDrawer : SerializeReferenceCreatorDrawer
	{
	}

	[CustomPropertyDrawer(typeof(AudioPlayerAsset.AudioConductor))]
	public class AudioConductorDrawer : SerializeReferenceCreatorDrawer
	{
	}

	[CustomPropertyDrawer(typeof(AudioPlayerAsset.ResourceWithVolume))]
	[CustomPropertyDrawer(typeof(AudioPlayerAsset.ClipWithVolume))]
	[CustomPropertyDrawer(typeof(AudioPlayerAsset.ClipWithVolumePitch))]
	internal class AudioWithVolumePropertyDrawer : PropertyDrawer
	{
		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			bool supportsPitching = property.type == nameof(AudioPlayerAsset.ClipWithVolumePitch);

			if (!supportsPitching)
				return EditorGUIUtility.singleLineHeight;

			var pitchesProperty = property.FindPropertyRelative(nameof(AudioPlayerAsset.ClipWithVolumePitch.Pitches));

			return EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing + (pitchesProperty.arraySize > 0 ? EditorGUI.GetPropertyHeight(pitchesProperty) : 0f);
		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			EditorGUI.BeginProperty(position, label, property);

			bool isClip = property.type.StartsWith("Clip");	// Matches both types with clips (above).
			bool supportsPitching = property.type == nameof(AudioPlayerAsset.ClipWithVolumePitch);

			var resourceProperty = property.FindPropertyRelative(isClip ? nameof(AudioPlayerAsset.ClipWithVolume.Clip) : nameof(AudioPlayerAsset.ResourceWithVolume.Resource));
			var volumeProperty = property.FindPropertyRelative(nameof(AudioPlayerAsset.ResourceWithVolume.VolumeDB));

			const float volumeWidth = 74f;
			const float volumePadding = 4f;

			float pitchButtonWidth = supportsPitching ? 18f : 0f;
			const float pitchPadding = 4f;

			var mainLineRect = position;
			mainLineRect.height = EditorGUIUtility.singleLineHeight;

			var resourceRect = mainLineRect;
			resourceRect.width -= volumeWidth + volumePadding;
			resourceRect.width -= pitchPadding + pitchButtonWidth;

			var volumeRect = mainLineRect;
			volumeRect.x += mainLineRect.width - volumeWidth - pitchPadding - pitchButtonWidth;
			volumeRect.width = volumeWidth;

			var pitchButtonRect = mainLineRect;
			pitchButtonRect.x += mainLineRect.width - pitchButtonWidth;
			pitchButtonRect.width = pitchButtonWidth;

			EditorGUI.PropertyField(resourceRect, resourceProperty, new GUIContent(""), true);

			int prevIndent = EditorGUI.indentLevel;
			EditorGUI.indentLevel = 0;
			EditorGUI.PropertyField(volumeRect, volumeProperty, new GUIContent(""), true);
			EditorGUI.indentLevel = prevIndent;

			if (supportsPitching) {
				var pitchesProperty = property.FindPropertyRelative(nameof(AudioPlayerAsset.ClipWithVolumePitch.Pitches));

				Color prevBackgroundColor = GUI.backgroundColor;
				if (pitchesProperty.arraySize > 0) {
					GUI.backgroundColor = Color.yellow;
				}

				bool togglePitches = GUI.Button(pitchButtonRect, new GUIContent("P", "Toggle pitch randomization."), EditorStyles.miniButton);

				GUI.backgroundColor = prevBackgroundColor;

				if (togglePitches) {
					if (pitchesProperty.arraySize == 0) {
						pitchesProperty.arraySize = 1;
						pitchesProperty.GetArrayElementAtIndex(0).intValue = 100;
					} else {
						pitchesProperty.ClearArray();
					}
				}

				if (pitchesProperty.arraySize > 0) {
					var pitchesRect = position;
					pitchesRect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
					pitchesRect.height -= EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
					EditorGUI.PropertyField(pitchesRect, pitchesProperty, true);
				}
			}
		}
	}


	/// <summary>
	/// Draw "P" play button next to the reference.
	/// </summary>
	[CustomPropertyDrawer(typeof(AudioPlayerAsset))]
	internal class AudioPlayerAssetPropertyDrawer : PropertyDrawer
	{
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			if (property.objectReferenceValue == null) {
				EditorGUI.PropertyField(position, property);
				return;
			}

			const float PLAY_BTN_WIDTH = 20.0f;
			const float PADDING = 4.0f;

			var refRect = new Rect(position.position, new Vector2(position.width - PLAY_BTN_WIDTH - PADDING, EditorGUIUtility.singleLineHeight));
			var playBtnRect = new Rect(position.position, new Vector2(PLAY_BTN_WIDTH, EditorGUIUtility.singleLineHeight));
			playBtnRect.x += refRect.width + PADDING;

			EditorGUI.PropertyField(refRect, property);

			if (AudioEditorUtils.IsPreviewClipPlaying()) {
				if (GUI.Button(playBtnRect, AudioEditorUtils.StopIconContent, AudioEditorUtils.PlayStopButtonStyle)) {
					AudioEditorUtils.StopAllPreviewClips();
				}

				// Force repaint till sound stops playing.
				foreach (var editor in ActiveEditorTracker.sharedTracker.activeEditors) {
					if (editor.serializedObject.targetObject == property.serializedObject.targetObject) {
						editor.Repaint();
					}
				}

			} else {

				if (GUI.Button(playBtnRect, AudioEditorUtils.PlayIconContent, AudioEditorUtils.PlayStopButtonStyle)) {

					AudioClip clip = null;

					var assetSO = new SerializedObject(property.objectReferenceValue);
					var conductorBindsProperty = assetSO.FindProperty(nameof(AudioPlayerAsset.Conductors));
					if (conductorBindsProperty.arraySize == 0)
						return;

					var conductorProperty = conductorBindsProperty.GetArrayElementAtIndex(0).FindPropertyRelative(nameof(AudioPlayerAsset.AudioConductorBind.Conductor));

					// Try to guess the conductor's name. It depends on the implementation.
					SerializedProperty audioClipProperty =
						conductorProperty.FindPropertyRelative(nameof(Conductors.PlayAudioConductor.AudioClip)) ??
						conductorProperty.FindPropertyRelative(nameof(Conductors.PlayCollectionAudioConductor.AudioClips)) ??
						conductorProperty.FindPropertyRelative(nameof(Conductors.IntroThenLoopConductor.Looped)) ??
						conductorProperty.FindPropertyRelative(nameof(Conductors.LoopSequenceOverlappingConductor.Clips)) ??
						conductorProperty.FindPropertyRelative("Resource"); // In case of direct Unity AudioResource reference.
						conductorProperty.FindPropertyRelative("Asset"); // In any case?

					if (audioClipProperty.isArray) {
						if (audioClipProperty.arraySize == 0)
							return;

						audioClipProperty = audioClipProperty.GetArrayElementAtIndex(0);
					}

					if (audioClipProperty.propertyType == SerializedPropertyType.ObjectReference) {
						clip = audioClipProperty.objectReferenceValue as AudioClip;
					} else {
						clip = audioClipProperty.FindPropertyRelative(nameof(AudioPlayerAsset.ClipWithVolume.Clip))?.objectReferenceValue as AudioClip ??
							   audioClipProperty.FindPropertyRelative(nameof(AudioPlayerAsset.ResourceWithVolume.Resource))?.objectReferenceValue as AudioClip;
					}

					if (clip == null)
						return;

#if UNITY_2023_2_OR_NEWER
					if (clip is UnityEngine.Audio.AudioResource resource) {
						AudioEditorUtils.PlayPreviewClip(resource);
					}
#else
					if (clip is AudioClip clip) {
						AudioEditorUtils.PlayPreviewClip(clip);
					}
#endif
				}
			}
		}
	}
}
