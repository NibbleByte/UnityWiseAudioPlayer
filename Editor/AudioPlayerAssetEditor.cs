using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace DevLocker.Audio.Utils.Editor
{
	/// <summary>
	/// Draws the <see cref="AudioPlayerAsset.Settings"/> members inline, as if they were members of the asset itself (no foldout).
	/// If a template is assigned, its components (AudioSource and filters) are drawn at the bottom, so they can be tweaked in place.
	/// </summary>
	[CustomEditor(typeof(AudioPlayerAsset), true)]
	[CanEditMultipleObjects]
	internal class AudioPlayerAssetEditor : UnityEditor.Editor
	{
		private const string TemplateFoldoutKey = "WiseAudioPlayer.AudioPlayerAssetEditor.TemplateFoldout";

		private readonly List<UnityEditor.Editor> m_TemplateEditors = new List<UnityEditor.Editor>();
		private AudioSource m_TemplateEditorsSource;

		private void OnDisable()
		{
			ClearTemplateEditors();
		}

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

			// Draw at the bottom, as they can be quite long.
			DrawTemplateComponents(serializedObject.FindProperty(nameof(AudioPlayerAsset.Settings) + "." + nameof(AudioPlaybackSettings.Template)));

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

		private void DrawTemplateComponents(SerializedProperty templateProperty)
		{
			// Multi-selection with different templates - nothing sensible to show.
			AudioSource template = templateProperty.hasMultipleDifferentValues ? null : templateProperty.objectReferenceValue as AudioSource;
			if (template == null) {
				ClearTemplateEditors();
				return;
			}

			RefreshTemplateEditors(template);

			EditorGUILayout.Space();
			bool foldout = SessionState.GetBool(TemplateFoldoutKey, true);
			bool newFoldout = EditorGUILayout.Foldout(foldout, new GUIContent("Template Components", "Components of the template object. Changes are applied to the template itself and affect everyone using it."), toggleOnLabelClick: true);
			if (newFoldout != foldout) {
				SessionState.SetBool(TemplateFoldoutKey, newFoldout);
			}

			if (!newFoldout)
				return;

			using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
				foreach (UnityEditor.Editor editor in m_TemplateEditors) {
					if (editor == null || editor.target == null)
						continue;

					bool expanded = InternalEditorUtility.GetIsInspectorExpanded(editor.target);
					bool newExpanded = EditorGUILayout.InspectorTitlebar(expanded, editor);
					if (newExpanded != expanded) {
						InternalEditorUtility.SetIsInspectorExpanded(editor.target, newExpanded);
					}

					if (newExpanded) {
						editor.OnInspectorGUI();
						EditorGUILayout.Space(2f);
					}
				}
			}
		}

		private void RefreshTemplateEditors(AudioSource template)
		{
			var components = new List<Component>();
			foreach (Component component in template.GetComponents<Component>()) {
				// Missing scripts are null. Transform is irrelevant for audio.
				if (component == null || component is Transform)
					continue;

				components.Add(component);
			}

			bool upToDate = m_TemplateEditorsSource == template && m_TemplateEditors.Count == components.Count;
			for (int i = 0; upToDate && i < components.Count; ++i) {
				upToDate = m_TemplateEditors[i] != null && m_TemplateEditors[i].target == components[i];
			}

			if (upToDate)
				return;

			ClearTemplateEditors();

			m_TemplateEditorsSource = template;
			foreach (Component component in components) {
				m_TemplateEditors.Add(CreateEditor(component));
			}
		}

		private void ClearTemplateEditors()
		{
			foreach (UnityEditor.Editor editor in m_TemplateEditors) {
				if (editor) {
					DestroyImmediate(editor);
				}
			}

			m_TemplateEditors.Clear();
			m_TemplateEditorsSource = null;
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
					var conditionalConductorsProperty = assetSO.FindProperty(nameof(AudioPlayerAsset.Conductors));
					if (conditionalConductorsProperty.arraySize == 0)
						return;

					var conductorProperty = conditionalConductorsProperty.GetArrayElementAtIndex(0).FindPropertyRelative(nameof(ConditionalConductor.Conductor));

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
