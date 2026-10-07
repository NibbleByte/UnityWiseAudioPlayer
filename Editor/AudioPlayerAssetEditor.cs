using UnityEditor;
using UnityEngine;

namespace DevLocker.Audio.Utils.Editor
{
	[CustomPropertyDrawer(typeof(AudioPlayerAsset.AudioConductorFilter))]
	public class AudioConductorFilterDrawer : WiseSerializeReferenceBasePropertyDrawerCOPY
	{
	}

	[CustomPropertyDrawer(typeof(AudioPlayerAsset.AudioConductor))]
	public class AudioConductorDrawer : WiseSerializeReferenceBasePropertyDrawerCOPY
	{
	}

	/// <summary>
	/// Draws the <see cref="AudioPlayerAsset.Settings"/> members inline, as if they were members of the asset itself (no foldout).
	/// </summary>
	[CustomEditor(typeof(AudioPlayerAsset), true)]
	[CanEditMultipleObjects]
	internal class AudioPlayerAssetEditor : UnityEditor.Editor
	{
		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			SerializedProperty property = serializedObject.GetIterator();
			bool enterChildren = true;
			while (property.NextVisible(enterChildren)) {
				enterChildren = false;

				if (property.propertyPath == "m_Script") {
					using (new EditorGUI.DisabledScope(true)) {
						EditorGUILayout.PropertyField(property);
					}
					continue;
				}

				if (property.propertyPath == nameof(AudioPlayerAsset.Settings)) {
					DrawChildrenInline(property);
					continue;
				}

				EditorGUILayout.PropertyField(property, true);
			}

			serializedObject.ApplyModifiedProperties();
		}

		private static void DrawChildrenInline(SerializedProperty parent)
		{
			SerializedProperty child = parent.Copy();
			SerializedProperty end = parent.GetEndProperty();

			if (!child.NextVisible(true))
				return;

			while (!SerializedProperty.EqualContents(child, end)) {
				EditorGUILayout.PropertyField(child, true);

				if (!child.NextVisible(false))
					break;
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
				EditorGUI.PropertyField(position, property, label);
				return;
			}

			const float PLAY_BTN_WIDTH = 20.0f;
			const float PADDING = 4.0f;

			var refRect = new Rect(position.position, new Vector2(position.width - PLAY_BTN_WIDTH - PADDING, EditorGUIUtility.singleLineHeight));
			var playBtnRect = new Rect(position.position, new Vector2(PLAY_BTN_WIDTH, EditorGUIUtility.singleLineHeight));
			playBtnRect.x += refRect.width + PADDING;

			EditorGUI.PropertyField(refRect, property, label);

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
					var filteredConductorsProperty = assetSO.FindProperty(nameof(AudioPlayerAsset.Conductors));
					if (filteredConductorsProperty.arraySize == 0)
						return;

					var conductorProperty = filteredConductorsProperty.GetArrayElementAtIndex(0).FindPropertyRelative(nameof(AudioPlayerAsset.FilteredConductor.Conductor));

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
						clip = audioClipProperty.FindPropertyRelative(nameof(ClipWithVolume.Clip))?.objectReferenceValue as AudioClip ??
							   audioClipProperty.FindPropertyRelative(nameof(ResourceWithVolume.Resource))?.objectReferenceValue as AudioClip;
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
