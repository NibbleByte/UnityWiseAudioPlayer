using DevLocker.Audio.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace DevLocker.Audio
{
	/// <summary>
	/// Where a conductor keeps its state between plays, such as shuffle order or last clip played.
	/// See <see cref="AudioPlayback.GetConductorStateValue{T}"/>.
	/// </summary>
	public enum ConductorStateScope
	{
		PerPlayer,
		PerAsset,
	}

	/// <summary>
	/// Settings used when playing conductors - via <see cref="AudioPlayerAsset"/> or standalone via <see cref="AudioPlayer.PlayConductor"/>.
	/// They override the <see cref="AudioPlayer"/> settings.
	/// </summary>
	[Serializable]
	public struct AudioPlaybackSettings
	{
		public enum RepeatMode
		{
			Once = 0,
			Loop = 1,

			LoopWithInterval = 4,
		}

		[Serializable]
		public struct RepeatSettings
		{
			public RepeatMode Mode;

			[Tooltip("Seconds of silence after the sound finishes before it plays again. A random value between Min and Max is picked each time.")]
			public float MinSeconds;
			[Tooltip("Seconds of silence after the sound finishes before it plays again. A random value between Min and Max is picked each time.")]
			public float MaxSeconds;

			public float RollInterval() => Mode == RepeatMode.LoopWithInterval ? UnityEngine.Random.Range(MinSeconds, MaxSeconds) : 0f;

			public bool IsRepeating => Mode != RepeatMode.Once;

			public bool IsOnce => Mode == RepeatMode.Once;
			public bool IsLoop => Mode == RepeatMode.Loop;
			public bool IsInterval => Mode == RepeatMode.LoopWithInterval;

			public void OnValidate(UnityEngine.Object context)
			{
#if UNITY_EDITOR
				if (MinSeconds < 0f) {
					MinSeconds = 0f;
					UnityEditor.EditorUtility.SetDirty(context);
				}

				if (MaxSeconds < MinSeconds) {
					MaxSeconds = MinSeconds;
					UnityEditor.EditorUtility.SetDirty(context);
				}
#endif
			}
		}

		[Tooltip("How the sound repeats. Loop With Interval adds seconds of silence after each play.\n\nSome conductors ignore this and loop on their own.\nOverrides the AudioPlayer setting.")]
		public RepeatSettings Repeat;

		[SerializeReference]
		[Tooltip("Optional. When this asset starts playing (after Delay), stops other sounds that match the chosen rule. Leave empty to not interrupt anything.")]
		public AudioInterrupter Interrupter;

		[Tooltip("Seconds to wait before the asset starts. Not repeated when looping.")]
		public float Delay;

		[Tooltip("Mixer group to output to. Overrides the AudioPlayer's mixer. If empty, uses the template's mixer.")]
		public AudioMixerGroup OutputMixer;

		[Tooltip("AudioSource (prefab or scene object) whose settings are copied to the playing source. Overrides the player's template.\nYou can have audio filter components attached too, but the player must use " + nameof(AudioPlayer.AudioSourcesPoolMode.GlobalPool) + " as pool mode!")]
		public AudioSource Template;

		[Tooltip("Tags for grouping this asset's sounds, e.g. to interrupt them by tag.")]
		public AudioTag[] Tags;

		/// <summary>
		/// The <see cref="OutputMixer"/> or the <see cref="Template"/>'s mixer if empty.
		/// </summary>
		public AudioMixerGroup GetEffectiveOutputMixer()
		{
			if (OutputMixer)
				return OutputMixer;

			if (Template)
				return Template.outputAudioMixerGroup;

			return null;
		}

		public void OnValidate(UnityEngine.Object context)
		{
			Repeat.OnValidate(context);

			if (Interrupter != null) {
				Interrupter.OnValidate(context);
			}
		}
	}

	/// <summary>
	/// Responsible for playing the desired audio.
	/// Inherit to have custom behaviour.
	///
	/// Can be played via <see cref="AudioPlayerAsset"/> or standalone via <see cref="AudioPlayer.PlayConductor"/>.
	/// Use <see cref="AudioPlayback.GetConductorStateValue{T}"/> and
	/// <see cref="AudioPlayback.SetConductorStateValue"/> to persist state.
	/// </summary>
	[Serializable]
	public abstract class AudioConductor
	{
		/// <summary>
		/// Explains the <see cref="ConductorStateScope"/> options. Append it to the tooltip of the state scope field implementations may have.
		/// </summary>
		protected const string StateScopeTooltip =
			" > Per Player - each player keeps its own state.\n" +
			" > Per Asset - all players of this asset share the state.\n\n" +
			"For example, should the pitch go up no matter which barrel you hit, or should each barrel (player) track its own pitch sequence?";

		public abstract IEnumerator Play(AudioPlayback playback);

		/// <summary>
		/// Context is the object owning this conductor.
		/// If you keep conductors in your own assets, call this from their OnValidate().
		/// </summary>
		public virtual void OnValidate(UnityEngine.Object context) { }
	}

	/// <summary>
	/// Condition that decides whether its conductor can be played. See <see cref="ConditionalConductor"/>.
	/// </summary>
	[Serializable]
	public abstract class AudioCondition
	{
		public abstract bool IsAllowed(object context, AudioPlayback playback);

		public virtual void OnValidate(UnityEngine.Object context) { }
	}

	[Serializable]
	public struct ConditionalConductor
	{
		[Tooltip("Responsible for playing the desired audio.")]
		[SerializeReference]
		public AudioConductor Conductor;

		[Tooltip("All conditions must pass for this conductor to be used. The first conductor whose conditions all pass is played.")]
		[SerializeReference]
		public AudioCondition[] Conditions;
	}

	/// <summary>
	/// Invoked when a new playback starts (after its delay) to stop other playing sounds.
	/// The same instance is shared by all playbacks of the asset, so keep implementations stateless.
	/// </summary>
	[Serializable]
	public abstract class AudioInterrupter
	{
		/// <summary>
		/// Tooltip for the fade out duration field implementations may have.
		/// </summary>
		protected const string FadeOutDurationTooltip = "Seconds to fade out the interrupted sounds. 0 stops them instantly.";

		/// <summary>
		/// Tooltip for the all players field implementations may have.
		/// </summary>
		protected const string AllPlayersTooltip = "Stop matching sounds on all active players. If off, only on the player starting this sound.";

		public abstract void Interrupt(AudioPlayback playback);

		public virtual void OnValidate(UnityEngine.Object context) { }
	}

	/// <summary>
	/// Represents audio playback in progress.
	/// </summary>
	public class AudioPlayback
	{
		public readonly AudioPlayer.AudioSourcesPoolMode SourcesPoolMode;
		public AudioPlayer Player { get; internal set; }
		public AudioSource AudioSource { get; internal set; }
		public AudioConductor Conductor { get; internal set; }
		public UnityEngine.Object ConductorAsset { get; internal set; }	// Asset the conductor is coming from (optional).
		public bool HasConductorFinished { get; internal set; }
		public readonly float StartTimeUnscaled;

		public bool IsPlaying => AudioSource != null && (!HasConductorFinished || (AudioSource.isPlaying || IsPaused) || (!IsUsingConductors && Repeat.IsInterval));
		public bool IsPaused => Player && Player.IsPaused;	// When AudioSource is paused, isPlaying returns false. No isPaused property, so we have to track this ourselves :(
		public bool IsUsingConductors { get; internal set; }    // True when playing conductors (asset or standalone), false for a plain AudioResource.

		public AudioPlaybackSettings.RepeatSettings Repeat { get; internal set; }
		public AudioMixerGroup OutputMixer => AudioSource ? AudioSource.outputAudioMixerGroup : null;
		public AudioSource Template { get; internal set; }

		public AudioTag[] Tags { get; internal set; } = Array.Empty<AudioTag>();

		internal Coroutine ConductorCoroutine;
		internal Coroutine StopFadeCoroutine;
		internal Coroutine PauseFadeCoroutine;
		internal float PauseInitialVolume;

		internal float NextPlayTime;						// For LoopWithInterval mode.
		internal bool LastIsPlayingForLoopWithInterval;		// For LoopWithInterval mode.

		internal bool WasCancelled;

		/// <summary>
		/// Used by conductors to persist state per asset between usages. For example: don't repeat last clip. Try to use unique key names.
		/// </summary>
		private static Dictionary<ConductorStateKey, object> s_ConductorsPerAssetStateStorage = new Dictionary<ConductorStateKey, object>();
		
		/// <summary>
		/// Used by conductors to persist state per playback. Will be lost after playback finishes.
		/// Useful for communication with the running conductor.
		/// </summary>
		private Dictionary<string, object> m_ConductorsPlaybackStateStorage = new Dictionary<string, object>();

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ClearStaticsCache()
		{
			s_ConductorsPerAssetStateStorage = new Dictionary<ConductorStateKey, object>();
		}

		public AudioPlayback(AudioPlayer player, AudioSource audioSource, AudioPlayer.AudioSourcesPoolMode poolMode, float startTimeUnscaled)
		{
			SourcesPoolMode = poolMode;
			Player = player;
			AudioSource = audioSource;
			StartTimeUnscaled = startTimeUnscaled;
		}

		#region Conductor State Helpers

		/// <summary>
		/// Object to use as context for logs - the <see cref="ConductorAsset"/> if available, else the <see cref="Player"/>.
		/// </summary>
		public UnityEngine.Object DebugContext => ConductorAsset != null ? ConductorAsset : Player;

		/// <summary>
		/// Get conductor state value, kept per player or per asset depending on <paramref name="scope"/>. Used by conductors.
		/// </summary>
		public T GetConductorStateValue<T>(ConductorStateScope scope, string keyName, T defaultValue)
		{
			if (Conductor == null)
				throw new InvalidOperationException($"Trying to get conductor state value for \"{keyName}\", but no valid conductor is set.");

			switch (scope) {
				case ConductorStateScope.PerPlayer:
					return Player.GetConductorStateValue(Conductor, ConductorAsset, keyName, defaultValue);

				case ConductorStateScope.PerAsset:
					return GetConductorStateValuePerAsset(Conductor, ConductorAsset, keyName, defaultValue);

				default:
					throw new ArgumentOutOfRangeException(nameof(scope), scope, null);
			}
		}

		/// <summary>
		/// Get conductor state value that is saved per asset.
		/// </summary>
		public static T GetConductorStateValuePerAsset<T>(AudioConductor conductor, UnityEngine.Object asset, string keyName, T defaultValue)
		{
			bool found = s_ConductorsPerAssetStateStorage.TryGetValue(new ConductorStateKey(conductor, asset, keyName), out object objValue);
			return found ? (T)objValue : defaultValue;
		}
		
		/// <summary>
		/// Get conductor state value that is saved on this playback. Values are lost when playback ends.
		/// Useful for communication with the running conductor.
		/// </summary>
		public T GetConductorPlaybackStateValue<T>(string keyName, T defaultValue)
		{
			bool found = m_ConductorsPlaybackStateStorage.TryGetValue(keyName, out object objValue);
			return found ? (T)objValue : defaultValue;
		}

		/// <summary>
		/// Set conductor state value, kept per player or per asset depending on <paramref name="scope"/>. Used by conductors.
		/// </summary>
		public void SetConductorStateValue(ConductorStateScope scope, string keyName, object value)
		{
			if (Conductor == null)
				throw new InvalidOperationException($"Trying to set conductor state value for \"{keyName}\", but no valid conductor is set.");

			switch (scope) {
				case ConductorStateScope.PerPlayer:
					Player.SetConductorStateValue(Conductor, ConductorAsset, keyName, value);
					break;

				case ConductorStateScope.PerAsset:
					SetConductorStateValuePerAsset(Conductor, ConductorAsset, keyName, value);
					break;

				default:
					throw new ArgumentOutOfRangeException(nameof(scope), scope, null);
			}
		}

		/// <summary>
		/// Set conductor state value that is saved per asset.
		/// </summary>
		public static void SetConductorStateValuePerAsset(AudioConductor conductor, UnityEngine.Object asset, string keyName, object value)
		{
			s_ConductorsPerAssetStateStorage[new ConductorStateKey(conductor, asset, keyName)] = value;
		}
		
		/// <summary>
		/// Set conductor state value that is saved on this playback. Values are lost when playback ends.
		/// Useful for communication with the running conductor.
		/// </summary>
		public void SetConductorPlaybackStateValue(string keyName, object value)
		{
			m_ConductorsPlaybackStateStorage[keyName] = value;
		}

		/// <summary>
		/// Set all conductor state values of given type that are saved per asset.
		/// </summary>
		public static void SetConductorStateValueForTypePerAsset(Type conductorType, UnityEngine.Object asset, string keyName, object value)
		{
			string assetName = asset ? asset.name : null;
			ulong assetId = asset ? ConductorStateKey.GetId(asset) : 0;

			var matchedKeys = new List<ConductorStateKey>();

			foreach (var conductorKey in s_ConductorsPerAssetStateStorage.Keys) {
				if (conductorKey.KeyName == keyName &&
				    conductorType.IsAssignableFrom(conductorKey.ConductorType) &&
					// No conductor id.
					conductorKey.AssetName == assetName &&
				    conductorKey.AssetId == assetId
				    ) {
					matchedKeys.Add(conductorKey);
				}
			}

			foreach (var key in matchedKeys) {
				s_ConductorsPerAssetStateStorage[key] = value;
			}
		}

		// Yes, this could have been just bunch of tokens concatenated in a string,
		// but this way I can easily process the individual tokens, instead of parsing the string every time.
		// Makes code clearer, less garbage.
		internal struct ConductorStateKey : IEquatable<ConductorStateKey>
		{
			public string KeyName;

			public Type ConductorType;
			public int ConductorId;

			public string AssetName;
			public ulong AssetId;

			public static ulong GetId(UnityEngine.Object obj)
			{
#if UNITY_6000_4_OR_NEWER
				return EntityId.ToULong(obj.GetEntityId());
#else
				return unchecked((ulong)obj.GetInstanceID());
#endif
			}

			public ConductorStateKey(AudioConductor conductor, UnityEngine.Object asset, string keyName)
			{
				KeyName = keyName;

				ConductorType = conductor.GetType();
				ConductorId = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(conductor);

				AssetName = asset ? asset.name : null;
				AssetId = asset ? GetId(asset) : 0;
			}

			public bool Equals(ConductorStateKey other)
				=> KeyName == other.KeyName &&

				   ConductorType == other.ConductorType &&
				   ConductorId == other.ConductorId &&

				   AssetName == other.AssetName &&
				   AssetId == other.AssetId
				   ;

			public override bool Equals(object obj) => obj is ConductorStateKey other && Equals(other);

			public override int GetHashCode()
				=> HashCode.Combine(KeyName, ConductorType, ConductorId, AssetName, AssetId);
		}

		#endregion

		#region Direct Play for Conductors

		/// <summary>
		/// Used by <see cref="AudioConductor"/> to play sound without changing the player properties.
		/// This way, the <see cref="Editor.AudioPlayerMonitorWindow"/> will show the correct sound.
		/// </summary>
		public void PlayClip(AudioClip clip, float volume = 1.0f, float pitch = 1.0f)
		{
			if (clip == null)
				throw new ArgumentNullException();

			AudioSource.clip = clip;
			AudioSource.pitch = pitch;

			AudioSource.volume = volume * Player.Volume;
			AudioSource.Play();
		}

		/// <summary>
		/// Used by <see cref="AudioConductor"/> when PlayOneShot() needs to be used.
		/// </summary>
		public void PlayClipOneShot(AudioClip clip, float volume = 1.0f)
		{
			if (clip == null)
				throw new ArgumentNullException();

			// So it shows up correctly in the audio monitor window.
			AudioSource.clip = clip;
			AudioSource.volume = volume * Player.Volume;

			AudioSource.PlayOneShot(clip);
		}

		/// <summary>
		/// Used by <see cref="AudioConductor"/> to play sound without changing the player properties.
		/// This way, the <see cref="Editor.AudioPlayerMonitorWindow"/> will show the correct sound.
		/// </summary>
		public void PlayClip(ClipWithVolume clipPair, float pitch = 1.0f, int volumeOffsetDB = 0)
		{
			if (clipPair.Clip == null)
				throw new ArgumentNullException();

			AudioSource.clip = clipPair.Clip;
			AudioSource.pitch = pitch;

			// Rolls the volume randomization range (if used), so call it only once.
			float volume = clipPair.GetPlayVolume(volumeOffsetDB);

			AudioSource.volume = volume * Player.Volume;
			AudioSource.Play();
		}

		/// <summary>
		/// Used by <see cref="AudioConductor"/> to play sound without changing the player properties.
		/// This way, the <see cref="Editor.AudioPlayerMonitorWindow"/> will show the correct sound.
		/// </summary>
		public void PlayClip(ClipWithVolumePitch clipPair, int volumeOffsetDB = 0, int pitchOffsetCents = 0)
		{
			if (clipPair.Clip == null)
				throw new ArgumentNullException();

			AudioSource.clip = clipPair.Clip;

			// Pitch range has priority over the pitches list.
			// No variation and no offset means pitch of 0 cents, i.e. 1f - resets the pitch in case it was changed by the last user.
			AudioSource.pitch = Mathf.Pow(AudioPlayerAsset.CentPitchSize, clipPair.GetRandomPitch(pitchOffsetCents));

			// Rolls the volume randomization range (if used), so call it only once.
			float volume = clipPair.GetPlayVolume(volumeOffsetDB);

			AudioSource.volume = volume * Player.Volume;
			AudioSource.Play();
		}

		/// <summary>
		/// Used by <see cref="AudioConductor"/> to play sound without changing the player properties.
		/// This way, the <see cref="Editor.AudioPlayerMonitorWindow"/> will show the correct sound.
		/// </summary>
		public void PlayResource(AudioResource resource, float pitch = 1.0f)
		{
			if (resource == null)
				throw new ArgumentNullException();

			AudioSource.resource = resource;
			AudioSource.pitch = pitch;

			AudioSource.Play();
		}

		/// <summary>
		/// Used by <see cref="AudioConductor"/> to play sound without changing the player properties.
		/// This way, the <see cref="Editor.AudioPlayerMonitorWindow"/> will show the correct sound.
		/// </summary>
		public void PlayResource(ResourceWithVolume resourcePair, float pitch = 1.0f, int volumeOffsetDB = 0)
		{
			if (resourcePair.Resource == null)
				throw new ArgumentNullException();

			AudioSource.resource = resourcePair.Resource;
			AudioSource.pitch = pitch;

			// Rolls the volume randomization range (if used).
			AudioSource.volume = resourcePair.GetPlayVolume(volumeOffsetDB) * Player.Volume;
			AudioSource.Play();
		}

		#endregion

		#region Coroutines

		internal void StopConductorCoroutine()
		{
			if (ConductorCoroutine != null) {
				Player.StopCoroutine(ConductorCoroutine);
				ConductorCoroutine = null;
			}
		}

		internal void StopPauseFadeCoroutine()
		{
			if (PauseFadeCoroutine != null) {
				Player.StopCoroutine(PauseFadeCoroutine);
				PauseFadeCoroutine = null;
			}
		}

		internal void StopStopFadeCoroutine()
		{
			if (StopFadeCoroutine != null) {
				Player.StopCoroutine(StopFadeCoroutine);
				StopFadeCoroutine = null;
			}
		}

		internal IEnumerator FadeVolumeCrt(float fadeSeconds, float workingVolume, bool fadeIn, Action callbackOnFinish = null)
		{
			float startTime = Time.unscaledTime;
			float startVolume = fadeIn ? 0f : workingVolume;
			float endVolume = fadeIn ? workingVolume : 0f;

			while (Time.unscaledTime - startTime < fadeSeconds) {
				AudioSource.volume = Mathf.Lerp(startVolume, endVolume, (Time.unscaledTime - startTime) / fadeSeconds);
				yield return null;
			}

			callbackOnFinish?.Invoke();
		}

		#endregion
	}
}
