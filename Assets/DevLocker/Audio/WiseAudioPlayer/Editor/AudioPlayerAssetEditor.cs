using DevLocker.Audio.AudioPlayerUtils;
using UnityEditor;
using UnityEngine;

namespace DevLocker.Audio.Editor
{
	[CustomPropertyDrawer(typeof(AudioPlayerAsset.AudioPredicate))]
	public class AudioPredicateDrawer : WiseSerializeReferenceBasePropertyDrawerCOPY
	{
	}

	[CustomPropertyDrawer(typeof(AudioPlayerAsset.AudioConductor))]
	public class AudioConductorDrawer : WiseSerializeReferenceBasePropertyDrawerCOPY
	{
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
