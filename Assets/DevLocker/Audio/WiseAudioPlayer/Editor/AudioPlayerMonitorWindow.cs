using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace DevLocker.Audio.Editor
{
	/// <summary>
	/// Display any plays/stops/pauses/unpauses done by a <see cref="AudioPlayer"/>
	/// </summary>
	public class AudioPlayerMonitorWindow : EditorWindow
	{
		private enum ActionType
		{
			Play = 1 << 0,
			Stop = 1 << 1,
			Pause = 1 << 2,
			UnPause = 1 << 3,
		}

		[Serializable]
		private struct ActionEntry
		{
			public ActionType Type;
			public float Time;

			public MonoBehaviour Player;
			public AudioResource Resource;
			public AudioPlayerAsset Asset;
			public AudioMixerGroup MixerGroup;
			public AudioSource Template;

			public bool Mute;
			public bool PlayOnEnable;
			public AudioPlayer.RepeatMode RepeatMode;
			public float Volume;
			public float Pitch;
			public float SpatialBlend;

			public float ListenerDistance;
		}

		private bool m_ListenForEvents = true;
		private bool m_ClearOnPlay = true;
		private bool m_LogActions = false;
		private int m_EntriesLimit = 100;
		private bool m_ShowDetails = false;

		private ActionType m_ActionDisplayFilter = (ActionType)~0;

		private List<ActionEntry> m_Actions = new List<ActionEntry>();

		[NonSerialized] private GUIStyle HeaderStyle;
		[NonSerialized] private static GUIStyle HeaderUrlStyle;

		private Vector2 m_ScrollView;

		private AudioListener m_AudioListener;

		[MenuItem("Window/Audio/Wise Audio Monitor")]
		public static void ShowMonitor()
		{
			var window = GetWindow<AudioPlayerMonitorWindow>(false, "Audio Monitor");
			window.minSize = new Vector2(400f, 200f);
		}

		void OnEnable()
		{
			AudioPlayer.PlaybackStarted += OnPlayStarted;
			AudioPlayer.PlaybackStopped += OnPlayStopped;
			AudioPlayer.PlaybackPaused += OnPlayPaused;
			AudioPlayer.PlaybackUnpaused += OnPlayUnpaused;

			EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
		}

		void OnDisable()
		{
			AudioPlayer.PlaybackStarted -= OnPlayStarted;
			AudioPlayer.PlaybackStopped -= OnPlayStopped;
			AudioPlayer.PlaybackPaused -= OnPlayPaused;
			AudioPlayer.PlaybackUnpaused -= OnPlayUnpaused;
		}

		private void OnPlayModeStateChanged(PlayModeStateChange stateChange)
		{
			if (m_ClearOnPlay && stateChange == PlayModeStateChange.ExitingEditMode) {
				m_Actions.Clear();
			}
		}

		private void OnPlayStarted(AudioPlayer.AudioPlayback playback) => AddAction(ActionType.Play, playback);

		private void OnPlayStopped(AudioPlayer.AudioPlayback playback) => AddAction(ActionType.Stop, playback);

		private void OnPlayPaused(AudioPlayer.AudioPlayback playback) => AddAction(ActionType.Pause, playback);

		private void OnPlayUnpaused(AudioPlayer.AudioPlayback playback) => AddAction(ActionType.UnPause, playback);

		private void AddAction(ActionType actionType, AudioPlayer.AudioPlayback playback)
		{
			if (!m_ListenForEvents)
				return;

			if (m_AudioListener == null || !m_AudioListener.isActiveAndEnabled) {
				m_AudioListener = GameObject.FindAnyObjectByType<AudioListener>();
			}

			var action = new ActionEntry() {
				Type = actionType,
				Time = Time.time,

				Player = playback.Player,
				Resource = playback.AudioSource.resource,
				Asset = playback.AudioPlayerAsset,
				MixerGroup = playback.OutputMixer,
				Template = playback.Template,

				Mute = playback.Player.Mute,
				PlayOnEnable = playback.Player.PlayOnEnable,

				RepeatMode = playback.Repeat.Mode,
				Volume = playback.AudioSource.volume,	// Conductors may change audio source volume directly.
				Pitch = playback.AudioSource.pitch,
				SpatialBlend = playback.AudioSource.spatialBlend,

				ListenerDistance = m_AudioListener ? Vector3.Distance(m_AudioListener.transform.position, playback.Player.transform.position) : -1f,
			};

			m_Actions.Add(action);

			if (m_LogActions) {
				LogAction(action);
			}

			if (m_Actions.Count > m_EntriesLimit) {
				m_Actions.RemoveAt(0);
			}

			Repaint();
		}

		private void InitStyles()
		{
			HeaderStyle = new GUIStyle(EditorStyles.toolbar);
			HeaderStyle.alignment = TextAnchor.LowerCenter;

			HeaderUrlStyle = new GUIStyle(EditorStyles.toolbar);
			HeaderUrlStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(1.00f, 0.65f, 0.00f) : Color.blue;
			HeaderUrlStyle.hover.textColor = HeaderUrlStyle.normal.textColor;
			HeaderUrlStyle.active.textColor = Color.red;
			HeaderUrlStyle.alignment = TextAnchor.LowerCenter;

		}

		void OnGUI()
		{
			if (HeaderStyle == null) {
				InitStyles();
			}

			EditorGUILayout.BeginHorizontal();
			{
				Color prevBackgroundColor = GUI.backgroundColor;
				GUI.backgroundColor = m_ListenForEvents ? Color.green : prevBackgroundColor;

				if (GUILayout.Button("Listen", GUILayout.ExpandWidth(false))) {
					m_ListenForEvents = !m_ListenForEvents;
				}

				GUI.backgroundColor = prevBackgroundColor;

				GUI.backgroundColor = m_ShowDetails ? Color.green : prevBackgroundColor;
				if (GUILayout.Button("Details", GUILayout.ExpandWidth(false))) {
					m_ShowDetails = !m_ShowDetails;
				}
				GUI.backgroundColor = prevBackgroundColor;

				float prevLabelWidth = EditorGUIUtility.labelWidth;
				EditorGUIUtility.labelWidth = 80f;
				m_EntriesLimit = Mathf.Max(0, EditorGUILayout.IntField("Limit Entries", m_EntriesLimit, GUILayout.Width(112f)));
				EditorGUIUtility.labelWidth = prevLabelWidth;

				GUILayout.FlexibleSpace();

				EditorGUIUtility.labelWidth = 25f;
				m_LogActions = EditorGUILayout.Toggle(new GUIContent("Log", "Print all incoming events in the Unity Console Log.\nUseful way to find out who is starting a sound by checking the callstack."), m_LogActions);
				EditorGUIUtility.labelWidth = prevLabelWidth;

				EditorGUIUtility.labelWidth = 80f;
				m_ClearOnPlay = EditorGUILayout.Toggle("Clear on play", m_ClearOnPlay);
				EditorGUIUtility.labelWidth = prevLabelWidth;

				if (GUILayout.Button("Clear")) {
					m_Actions.Clear();
				}
			}
			EditorGUILayout.EndHorizontal();

			if (m_ShowDetails) {
				DrawDetailedView();
			} else {
				DrawSimpleView();
			}
		}

		private void DrawSimpleView()
		{
			const float enumColumnWidth = 50f;
			const float timeColumnWidth = 80f;

			// Table Header
			EditorGUILayout.BeginHorizontal();
			{
				if (GUILayout.Button("Action", HeaderUrlStyle, GUILayout.Width(enumColumnWidth))) PopActionFilterMenu();
				GUILayout.Label("Time", HeaderStyle, GUILayout.MaxWidth(timeColumnWidth));

				GUILayout.Label("Player", HeaderStyle, GUILayout.ExpandWidth(true));
				GUILayout.Label("Resource", HeaderStyle, GUILayout.ExpandWidth(true));
				GUILayout.Label("Asset", HeaderStyle, GUILayout.ExpandWidth(true));
				GUILayout.Label("Distance", HeaderStyle, GUILayout.MaxWidth(timeColumnWidth));
			}
			EditorGUILayout.EndHorizontal();

			m_ScrollView = GUILayout.BeginScrollView(m_ScrollView);
			
			// Table Content
			for (int i = m_Actions.Count - 1; i >= 0; i--) {
				var action = m_Actions[i];

				if (!m_ActionDisplayFilter.HasFlag(action.Type))
					continue;

				EditorGUILayout.BeginHorizontal();
				{
					GUILayout.Label(action.Type.ToString(), EditorStyles.boldLabel, GUILayout.Width(enumColumnWidth));
					EditorGUILayout.FloatField(action.Time, GUILayout.MaxWidth(timeColumnWidth));
					EditorGUILayout.ObjectField(action.Player, action.Player?.GetType(), true, GUILayout.ExpandWidth(true));
					EditorGUILayout.ObjectField(action.Resource, typeof(AudioResource), true, GUILayout.ExpandWidth(true));
					EditorGUILayout.ObjectField(action.Asset, typeof(AudioPlayerAsset), false, GUILayout.ExpandWidth(true));
					EditorGUILayout.FloatField(action.ListenerDistance, GUILayout.MaxWidth(timeColumnWidth));

				}
				EditorGUILayout.EndHorizontal();

			}

			EditorGUILayout.EndScrollView();
		}

		private void DrawDetailedView()
		{
			// NOTE: Tried doing it smarter, but ObjectField and Toggles don't accept styles, so no way to specify margins and format columns under header.
			//		 Changing the EditorStyles didn't work out too. So I have to fake the margins manually. :(
			//		 Other option is to use the Unity TreeView.
			const float enumColumnWidth = 50f;
			const float boolColumnWidth = 35f;
			const float timeColumnWidth = 80f;
			const float floatColumnWidth = 30f;
			const float scrollViewMarginFix = 35f;
			const int objectColumnsCount = 5;
			float objectFlexibleWidth = (position.width - timeColumnWidth - 2 * enumColumnWidth - 2 * boolColumnWidth - 3 * floatColumnWidth - scrollViewMarginFix) / objectColumnsCount;

			m_ScrollView = GUILayout.BeginScrollView(m_ScrollView);

			// Table Header
			EditorGUILayout.BeginHorizontal();
			{
				if (GUILayout.Button("Action", HeaderUrlStyle, GUILayout.Width(enumColumnWidth))) PopActionFilterMenu();
				GUILayout.Label("Time", HeaderStyle, GUILayout.Width(timeColumnWidth));

				GUILayout.Label("Player", HeaderStyle, GUILayout.Width(objectFlexibleWidth));
				GUILayout.Label("Resource", HeaderStyle, GUILayout.Width(objectFlexibleWidth));
				GUILayout.Label("Mixer Group", HeaderStyle, GUILayout.Width(objectFlexibleWidth));
				GUILayout.Label("Template", HeaderStyle, GUILayout.Width(objectFlexibleWidth));
				GUILayout.Label("Asset", HeaderStyle, GUILayout.Width(objectFlexibleWidth));

				GUILayout.Label("Mute", HeaderStyle, GUILayout.Width(boolColumnWidth));
				GUILayout.Label("Auto", HeaderStyle, GUILayout.Width(boolColumnWidth));

				GUILayout.Label("Repeat", HeaderStyle, GUILayout.Width(enumColumnWidth + 4f));

				GUILayout.Label("Vol", HeaderStyle, GUILayout.Width(floatColumnWidth + 8f));
				GUILayout.Label("Pitch", HeaderStyle, GUILayout.Width(floatColumnWidth - 6f));
				GUILayout.Label("Spatial", HeaderStyle, GUILayout.Width(floatColumnWidth + scrollViewMarginFix));
			}
			EditorGUILayout.EndHorizontal();

			// Table Content
			for (int i = m_Actions.Count - 1; i >= 0; i--) {
				var action = m_Actions[i];

				if (!m_ActionDisplayFilter.HasFlag(action.Type))
					continue;

				EditorGUILayout.BeginHorizontal();
				{
					GUILayout.Label(action.Type.ToString(), EditorStyles.boldLabel, GUILayout.Width(enumColumnWidth));
					EditorGUILayout.FloatField(action.Time, GUILayout.Width(timeColumnWidth));

					float objectMarginFix = 5;
					EditorGUILayout.ObjectField(action.Player, action.Player?.GetType(), true, GUILayout.Width(objectFlexibleWidth - objectMarginFix));
					EditorGUILayout.ObjectField(action.Resource, typeof(AudioResource), true, GUILayout.Width(objectFlexibleWidth - objectMarginFix));
					EditorGUILayout.ObjectField(action.MixerGroup, typeof(AudioMixerGroup), true, GUILayout.Width(objectFlexibleWidth - objectMarginFix));
					EditorGUILayout.ObjectField(action.Template, typeof(AudioSource), true, GUILayout.Width(objectFlexibleWidth - objectMarginFix));
					EditorGUILayout.ObjectField(action.Asset, typeof(AudioPlayerAsset), false, GUILayout.Width(objectFlexibleWidth - objectMarginFix));

					float toggleMargin = 8f;
					GUILayout.Space(toggleMargin);
					EditorGUILayout.Toggle(action.Mute, GUILayout.Width(boolColumnWidth - toggleMargin));
					GUILayout.Space(toggleMargin);
					EditorGUILayout.Toggle(action.PlayOnEnable, GUILayout.Width(boolColumnWidth - toggleMargin));

					GUILayout.Label(action.RepeatMode.ToString().Replace(nameof(AudioPlayer.RepeatMode.LoopWithInterval), "Interval"), EditorStyles.boldLabel, GUILayout.Width(enumColumnWidth));

					EditorGUILayout.FloatField(action.Volume, GUILayout.Width(floatColumnWidth));
					EditorGUILayout.FloatField(action.Pitch, GUILayout.Width(floatColumnWidth));
					EditorGUILayout.FloatField(action.SpatialBlend, GUILayout.Width(floatColumnWidth));

				}
				EditorGUILayout.EndHorizontal();

			}

			EditorGUILayout.EndScrollView();
		}

		private void PopActionFilterMenu()
		{
			var menu = new GenericMenu();

			foreach(ActionType enumValue in Enum.GetValues(typeof(ActionType))) {
				menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(enumValue.ToString())), m_ActionDisplayFilter.HasFlag(enumValue), () => {
					m_ActionDisplayFilter = m_ActionDisplayFilter ^ enumValue;
				});
			}

			menu.ShowAsContext();
		}

		private void LogAction(ActionEntry action)
		{
			Debug.Log($"Audio Event: \"{action.Resource?.name}\" from \"{action.Player?.name}\" at {action.Time:0.0000} {action.Type}");
		}
	}

}
