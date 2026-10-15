using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
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

				if (property.propertyPath == nameof(AudioPlayerAsset.Comments)) {
					DrawComments(property);
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

				// Creating a template only makes sense for a single asset with no template.
				if (!templateProperty.hasMultipleDifferentValues && targets.Length == 1) {
					EditorGUILayout.Space();
					using (new EditorGUILayout.HorizontalScope()) {
						GUILayout.FlexibleSpace();
						if (GUILayout.Button(new GUIContent("Add Template", "Create a template prefab with an AudioSource next to this asset and assign it."), GUILayout.Width(120f))) {
							CreateTemplate(templateProperty);
						}
						GUILayout.FlexibleSpace();
					}
				}
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

		private void CreateTemplate(SerializedProperty templateProperty)
		{
			var asset = (AudioPlayerAsset)target;
			string assetPath = AssetDatabase.GetAssetPath(asset);
			if (string.IsNullOrEmpty(assetPath))
				return;

			string baseName = asset.name.Replace("AudioAsset", "").Trim(' ', '_', '-', '.');
			if (string.IsNullOrEmpty(baseName)) {
				baseName = "Audio";
			}

			string folder = Path.GetDirectoryName(assetPath).Replace('\\', '/');
			string prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{baseName}_Template.prefab");

			bool confirmed = EditorUtility.DisplayDialog("Add template", $"Create a new template prefab with an AudioSource and assign it to this asset?\n\n{prefabPath}", "Create", "Cancel");
			if (confirmed) {
				// Build it in a preview scene, so the open scenes don't get touched (dirtied).
				var previewScene = EditorSceneManager.NewPreviewScene();
				try {
					var go = new GameObject(Path.GetFileNameWithoutExtension(prefabPath), typeof(AudioSource));
					UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, previewScene);

					GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
					if (prefab) {
						templateProperty.objectReferenceValue = prefab.GetComponent<AudioSource>();
						serializedObject.ApplyModifiedProperties();
						EditorGUIUtility.PingObject(prefab);
					}
				}
				finally {
					EditorSceneManager.ClosePreviewScene(previewScene);
				}
			}

			// Modal dialog breaks the current GUI layout pass.
			GUIUtility.ExitGUI();
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

		#region Comments

		private const float CommentMaxHeight = 120f;
		private const string CommentTextControlName = "WiseAudioPlayer.CommentText";

		private bool m_EditingComment;
		private bool m_FocusCommentText;
		private Vector2 m_CommentScroll;
		private GUIStyle m_CommentStyle;

		private void DrawComments(SerializedProperty commentsProperty)
		{
			EditorGUILayout.Space();

			if (commentsProperty.hasMultipleDifferentValues) {
				m_EditingComment = false;
				EditorGUILayout.HelpBox("Comments differ between the selected assets.", MessageType.None);
				return;
			}

			if (m_EditingComment) {
				DrawCommentEdit(commentsProperty);
				return;
			}

			if (string.IsNullOrEmpty(commentsProperty.stringValue)) {
				using (new EditorGUILayout.HorizontalScope()) {
					GUILayout.FlexibleSpace();
					if (GUILayout.Button("Add Comment", GUILayout.Width(120f))) {
						BeginCommentEdit();
					}
					GUILayout.FlexibleSpace();
				}
				return;
			}

			DrawCommentView(commentsProperty);
		}

		private void DrawCommentView(SerializedProperty commentsProperty)
		{
			if (m_CommentStyle == null) {
				m_CommentStyle = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = false };
			}

			EditorGUILayout.LabelField(new GUIContent(commentsProperty.displayName, "Double-click the comment to edit it."), EditorStyles.boldLabel);

			var content = new GUIContent(commentsProperty.stringValue);
			// Rough estimate of the available width (inspector margins + vertical scrollbar).
			float width = EditorGUIUtility.currentViewWidth - 50f;
			float height = m_CommentStyle.CalcHeight(content, width) + 8f;

			m_CommentScroll = EditorGUILayout.BeginScrollView(m_CommentScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, EditorStyles.helpBox, GUILayout.Height(Mathf.Min(height, CommentMaxHeight)));
			GUILayout.Label(content, m_CommentStyle);
			EditorGUILayout.EndScrollView();

			// Double-click to edit an existing comment.
			Rect viewRect = GUILayoutUtility.GetLastRect();
			Event evt = Event.current;
			if (evt.type == EventType.MouseDown && evt.clickCount == 2 && viewRect.Contains(evt.mousePosition)) {
				BeginCommentEdit();
				evt.Use();
			}
		}

		private void DrawCommentEdit(SerializedProperty commentsProperty)
		{
			EditorGUILayout.LabelField(commentsProperty.displayName, EditorStyles.boldLabel);

			// No scroll view while editing - the text area grows with the content, so the inspector can follow the cursor.
			GUI.SetNextControlName(CommentTextControlName);
			// Write directly into the property while typing.
			commentsProperty.stringValue = EditorGUILayout.TextArea(commentsProperty.stringValue, new GUIStyle(EditorStyles.textArea) { wordWrap = true }, GUILayout.MinHeight(60f));

			if (m_FocusCommentText && Event.current.type == EventType.Repaint) {
				m_FocusCommentText = false;
				EditorGUI.FocusTextInControl(CommentTextControlName);
			}

			using (new EditorGUILayout.HorizontalScope()) {
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("End Editing", GUILayout.Width(120f))) {
					string trimmed = commentsProperty.stringValue?.Trim() ?? string.Empty;
					if (trimmed != commentsProperty.stringValue) {
						commentsProperty.stringValue = trimmed;
					}
					m_EditingComment = false;
					m_CommentScroll = Vector2.zero;
					GUI.FocusControl(null);
				}
			}
		}

		private void BeginCommentEdit()
		{
			m_EditingComment = true;
			m_FocusCommentText = true;
			GUI.FocusControl(null);
		}

		#endregion
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
						conductorProperty.FindPropertyRelative(nameof(Playback.PlayAudioConductor.AudioClip)) ??
						conductorProperty.FindPropertyRelative(nameof(Playback.PlayCollectionAudioConductor.AudioClips)) ??
						conductorProperty.FindPropertyRelative(nameof(Playback.IntroThenLoopConductor.Looped)) ??
						conductorProperty.FindPropertyRelative(nameof(Playback.LoopSequenceOverlappingConductor.Clips)) ??
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
