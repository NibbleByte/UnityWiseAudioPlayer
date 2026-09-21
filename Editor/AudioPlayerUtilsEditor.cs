using UnityEditor;
using UnityEngine;

namespace DevLocker.Audio.AudioPlayerUtils.Editor
{
	[CustomPropertyDrawer(typeof(ResourceWithVolume))]
	[CustomPropertyDrawer(typeof(ClipWithVolume))]
	[CustomPropertyDrawer(typeof(ClipWithVolumePitch))]
	internal class AudioWithVolumePropertyDrawer : PropertyDrawer
	{
		private const string ResourceName = nameof(ResourceWithVolume.Resource);
		private const string ClipName = nameof(ClipWithVolume.Clip);
		private const string VolumeDBName = nameof(ResourceWithVolume.VolumeDB);
		private const string UseVolumeRangeName = nameof(ResourceWithVolume.UseVolumeRange);
		private const string VolumeRangeName = nameof(ResourceWithVolume.VolumeRange);
		private const string VolumeRangeMinName = nameof(VolumeRange.MinDB);
		private const string VolumeRangeMaxName = nameof(VolumeRange.MaxDB);
		private const string VolumeRangeStepName = nameof(VolumeRange.StepDB);
		private const string PitchesName = nameof(ClipWithVolumePitch.Pitches);
		private const string UsePitchRangeName = nameof(ClipWithVolumePitch.UsePitchRange);
		private const string PitchRangeName = nameof(ClipWithVolumePitch.PitchRange);
		private const string PitchRangeMinName = nameof(PitchRange.Min);
		private const string PitchRangeMaxName = nameof(PitchRange.Max);
		private const string PitchRangeStepName = nameof(PitchRange.Step);

		private const int DefaultVolumeRangeMinDB = -6;
		private const int DefaultVolumeRangeMaxDB = 0;
		private const int DefaultPitchRangeMin = -100;
		private const int DefaultPitchRangeMax = 100;
		private const int DefaultPitchRangeStep = 100;
		private const int DefaultPitchListValue = 100;

		private const float VolumeWidth = 65f;
		private const float VolumePadding = 4f;

		/// <summary>
		/// Optional features (volume range, pitch range, pitch list) are drawn indented below the main line.
		/// </summary>
		private const float FeatureIndent = 15f;

		/// <summary>
		/// Click area of the foldout arrow, hiding or showing the used features.
		/// Unity draws the arrow itself in the padding on the left of the line, so it doesn't eat into the clip property space.
		/// </summary>
		private const float FoldoutWidth = 13f;

		private static GUIStyle s_OptionsStyle;
		private static GUIStyle OptionsStyle => s_OptionsStyle ?? (s_OptionsStyle = new GUIStyle("PaneOptions") { imagePosition = ImagePosition.ImageOnly });

		private static readonly GUIContent VolumeRangeLabel = new GUIContent("Volume Range", AudioPlayerUtils.VolumeRangeHint);
		private static readonly GUIContent PitchRangeLabel = new GUIContent("Pitch Range", AudioPlayerUtils.PitchRangeHint);

		private static bool SupportsPitching(SerializedProperty property) => property.type == nameof(ClipWithVolumePitch);

		/// <summary>
		/// Is any optional feature used - if not, no foldout is drawn as there is nothing to show.
		/// </summary>
		private static bool HasAnyFeature(SerializedProperty property, bool supportsPitching)
		{
			if (property.FindPropertyRelative(UseVolumeRangeName).boolValue)
				return true;

			if (supportsPitching) {
				if (property.FindPropertyRelative(UsePitchRangeName).boolValue)
					return true;

				if (property.FindPropertyRelative(PitchesName).arraySize > 0)
					return true;
			}

			return false;
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			float lineHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

			float height = EditorGUIUtility.singleLineHeight;

			bool supportsPitching = SupportsPitching(property);

			// Features are hidden - only the main line is drawn.
			if (!property.isExpanded || !HasAnyFeature(property, supportsPitching))
				return height;

			if (property.FindPropertyRelative(UseVolumeRangeName).boolValue) {
				height += lineHeight;
			}

			if (supportsPitching) {
				if (property.FindPropertyRelative(UsePitchRangeName).boolValue) {
					height += lineHeight;
					
				} else {
					var pitchesProperty = property.FindPropertyRelative(PitchesName);
					if (pitchesProperty.arraySize > 0) {
						height += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(pitchesProperty);
					}
				}
			}

			return height;
		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			EditorGUI.BeginProperty(position, label, property);

			bool isClip = property.type.StartsWith("Clip");	// Matches both types with clips (above).
			bool supportsPitching = SupportsPitching(property);

			var resourceProperty = property.FindPropertyRelative(isClip ? ClipName : ResourceName);
			var volumeProperty = property.FindPropertyRelative(VolumeDBName);
			var useVolumeRangeProperty = property.FindPropertyRelative(UseVolumeRangeName);

			// Apply the indentation to the rect manually and draw everything with no indent, so the foldout
			// can sit right next to the clip field. Otherwise the indent is applied by every drawn field
			// on top of our rects, leaving a gap between the foldout and the clip.
			position = EditorGUI.IndentedRect(position);

			int prevIndent = EditorGUI.indentLevel;
			EditorGUI.indentLevel = 0;

			var mainLineRect = position;
			mainLineRect.height = EditorGUIUtility.singleLineHeight;

			var optionsRect = mainLineRect;
			optionsRect.width = OptionsStyle.fixedWidth + OptionsStyle.margin.right;
			optionsRect.x = mainLineRect.xMax - optionsRect.width;
			optionsRect.y += OptionsStyle.margin.top + 1f;

			var volumeRect = mainLineRect;
			volumeRect.width = VolumeWidth;
			volumeRect.x = optionsRect.x - VolumeWidth - VolumePadding;

			bool hasAnyFeature = HasAnyFeature(property, supportsPitching);

			var foldoutRect = mainLineRect;
			foldoutRect.width = FoldoutWidth;

			var resourceRect = mainLineRect;
			resourceRect.xMax = volumeRect.x - VolumePadding;

			if (hasAnyFeature) {
				property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, GUIContent.none);
			}

			EditorGUI.PropertyField(resourceRect, resourceProperty, new GUIContent(""), true);
			EditorGUI.PropertyField(volumeRect, volumeProperty, new GUIContent(""), true);

			if (GUI.Button(optionsRect, new GUIContent("", "Randomization options."), OptionsStyle)) {
				ShowOptionsMenu(optionsRect, property, supportsPitching);
			}

			if (!hasAnyFeature || !property.isExpanded) {
				EditorGUI.indentLevel = prevIndent;
				EditorGUI.EndProperty();
				return;
			}

			var nextRect = mainLineRect;
			nextRect.xMin += FeatureIndent;

			if (useVolumeRangeProperty.boolValue) {
				nextRect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

				EditorGUI.PropertyField(nextRect, property.FindPropertyRelative(VolumeRangeName), VolumeRangeLabel);
			}

			if (supportsPitching) {

				if (property.FindPropertyRelative(UsePitchRangeName).boolValue) {
					nextRect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

					EditorGUI.PropertyField(nextRect, property.FindPropertyRelative(PitchRangeName), PitchRangeLabel);
					
				} else {
					
					var pitchesProperty = property.FindPropertyRelative(PitchesName);
					if (pitchesProperty.arraySize > 0) {
						var pitchesRect = position;
						pitchesRect.xMin += FeatureIndent;
						pitchesRect.yMin = nextRect.yMax + EditorGUIUtility.standardVerticalSpacing;
						EditorGUI.PropertyField(pitchesRect, pitchesProperty, true);
					}
				}
			}

			EditorGUI.indentLevel = prevIndent;

			EditorGUI.EndProperty();
		}

		private static void ShowOptionsMenu(Rect rect, SerializedProperty property, bool supportsPitching)
		{
			// The menu callbacks are executed later on, so don't capture the properties themselves - they may get invalidated.
			SerializedObject serializedObject = property.serializedObject;
			string propertyPath = property.propertyPath;

			var menu = new GenericMenu();

			menu.AddItem(new GUIContent("Volume Range"), property.FindPropertyRelative(UseVolumeRangeName).boolValue, () => {
				var target = serializedObject.FindProperty(propertyPath);
				var useProperty = target.FindPropertyRelative(UseVolumeRangeName);

				useProperty.boolValue = !useProperty.boolValue;

				if (useProperty.boolValue) {
					var rangeProperty = target.FindPropertyRelative(VolumeRangeName);
					var minProperty = rangeProperty.FindPropertyRelative(VolumeRangeMinName);
					var maxProperty = rangeProperty.FindPropertyRelative(VolumeRangeMaxName);

					if (minProperty.intValue == 0 && maxProperty.intValue == 0) {
						minProperty.intValue = DefaultVolumeRangeMinDB;
						maxProperty.intValue = DefaultVolumeRangeMaxDB;
					}

					// Show the feature that was just enabled.
					target.isExpanded = true;
				}

				serializedObject.ApplyModifiedProperties();
			});

			if (supportsPitching) {

				bool usePitchRange = property.FindPropertyRelative(UsePitchRangeName).boolValue;
				menu.AddItem(new GUIContent("Pitch Range"), usePitchRange, () => {
					var target = serializedObject.FindProperty(propertyPath);
					var useProperty = target.FindPropertyRelative(UsePitchRangeName);

					useProperty.boolValue = !useProperty.boolValue;

					if (useProperty.boolValue) {
						var rangeProperty = target.FindPropertyRelative(PitchRangeName);
						var minProperty = rangeProperty.FindPropertyRelative(PitchRangeMinName);
						var maxProperty = rangeProperty.FindPropertyRelative(PitchRangeMaxName);

						if (minProperty.intValue == 0 && maxProperty.intValue == 0) {
							minProperty.intValue = DefaultPitchRangeMin;
							maxProperty.intValue = DefaultPitchRangeMax;
							rangeProperty.FindPropertyRelative(PitchRangeStepName).intValue = DefaultPitchRangeStep;
						}

						// Pitch range has priority over the list - only one of both should be used.
						target.FindPropertyRelative(PitchesName).ClearArray();

						// Show the feature that was just enabled.
						target.isExpanded = true;
					}

					serializedObject.ApplyModifiedProperties();
				});

				menu.AddItem(new GUIContent("Pitch List"), !usePitchRange && property.FindPropertyRelative(PitchesName).arraySize > 0, () => {
					var target = serializedObject.FindProperty(propertyPath);
					var pitchesProperty = target.FindPropertyRelative(PitchesName);
					
					if (usePitchRange) {
						pitchesProperty.ClearArray();
					}

					if (pitchesProperty.arraySize == 0) {
						pitchesProperty.arraySize = 1;
						pitchesProperty.GetArrayElementAtIndex(0).intValue = DefaultPitchListValue;

						// Pitch range has priority over the list - only one of both should be used.
						target.FindPropertyRelative(UsePitchRangeName).boolValue = false;

						// Show the feature that was just enabled.
						target.isExpanded = true;

					} else {
						pitchesProperty.ClearArray();
					}

					serializedObject.ApplyModifiedProperties();
				});
			}

			menu.DropDown(rect);
		}

	}

	[CustomPropertyDrawer(typeof(VolumeRange))]
	internal class VolumeRangePropertyDrawer : PropertyDrawer
	{
		// Always drawn on a single line - no foldout for the children.
		public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight;

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			label = EditorGUI.BeginProperty(position, label, property);

			AudioRangeDrawerUtils.DrawMinMaxRange(
				position,
				label,
				property.FindPropertyRelative(nameof(VolumeRange.MinDB)),
				property.FindPropertyRelative(nameof(VolumeRange.MaxDB)),
				property.FindPropertyRelative(nameof(VolumeRange.StepDB)),
				AudioPlayerUtils.MinVolumeRangeDB,
				AudioPlayerUtils.MaxVolumeRangeDB
				);

			EditorGUI.EndProperty();
		}
	}

	[CustomPropertyDrawer(typeof(PitchRange))]
	internal class PitchRangePropertyDrawer : PropertyDrawer
	{
		// Always drawn on a single line - no foldout for the children.
		public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight;

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			label = EditorGUI.BeginProperty(position, label, property);

			AudioRangeDrawerUtils.DrawMinMaxRange(
				position,
				label,
				property.FindPropertyRelative(nameof(PitchRange.Min)),
				property.FindPropertyRelative(nameof(PitchRange.Max)),
				property.FindPropertyRelative(nameof(PitchRange.Step)),
				AudioPlayerUtils.MinPitchRangeCents,
				AudioPlayerUtils.MaxPitchRangeCents
				);

			EditorGUI.EndProperty();
		}
	}

	/// <summary>
	/// Draws a randomization range on a single line: label, min field, min-max slider, max field and step field.
	/// </summary>
	internal static class AudioRangeDrawerUtils
	{
		private const float RangeFieldWidth = 54f;
		private const float RangePadding = 4f;

		public static void DrawMinMaxRange(Rect position, GUIContent label, SerializedProperty minProperty, SerializedProperty maxProperty, SerializedProperty stepProperty, int limitMin, int limitMax)
		{
			position = EditorGUI.PrefixLabel(position, label);

			int prevIndent = EditorGUI.indentLevel;
			EditorGUI.indentLevel = 0;

			float fieldWidth = Mathf.Min(RangeFieldWidth, (position.width - RangePadding * 2f) / 3f);

			var minRect = new Rect(position.x, position.y, fieldWidth, EditorGUIUtility.singleLineHeight);
			var stepRect = new Rect(position.xMax - fieldWidth, position.y, fieldWidth, EditorGUIUtility.singleLineHeight);
			var maxRect = new Rect(stepRect.x - RangePadding - fieldWidth, position.y, fieldWidth, EditorGUIUtility.singleLineHeight);
			var sliderRect = new Rect(minRect.xMax + RangePadding, position.y, maxRect.x - minRect.xMax - RangePadding * 2f, EditorGUIUtility.singleLineHeight);

			// Rolled values are snapped to this step, so tiny unnoticeable offsets can be avoided.
			EditorGUI.PropertyField(stepRect, stepProperty, GUIContent.none);

			EditorGUI.BeginChangeCheck();
			EditorGUI.PropertyField(minRect, minProperty, GUIContent.none);
			bool minChanged = EditorGUI.EndChangeCheck();

			EditorGUI.BeginChangeCheck();
			EditorGUI.PropertyField(maxRect, maxProperty, GUIContent.none);
			bool maxChanged = EditorGUI.EndChangeCheck();

			// Keep min below max, without fighting the user while typing in the other field.
			if (minChanged && minProperty.intValue > maxProperty.intValue) {
				maxProperty.intValue = minProperty.intValue;
			}
			if (maxChanged && maxProperty.intValue < minProperty.intValue) {
				minProperty.intValue = maxProperty.intValue;
			}

			float minValue = minProperty.intValue;
			float maxValue = maxProperty.intValue;

			// Manually typed-in values may be outside the slider limits - don't clamp them silently.
			float sliderLimitMin = Mathf.Min(limitMin, minValue);
			float sliderLimitMax = Mathf.Max(limitMax, maxValue);

			if (sliderRect.width > 10f) {
				EditorGUI.BeginChangeCheck();
				EditorGUI.MinMaxSlider(sliderRect, ref minValue, ref maxValue, sliderLimitMin, sliderLimitMax);
				if (EditorGUI.EndChangeCheck()) {
					minProperty.intValue = Mathf.RoundToInt(minValue);
					maxProperty.intValue = Mathf.RoundToInt(maxValue);
				}
			}

			EditorGUI.indentLevel = prevIndent;
		}
	}
}
