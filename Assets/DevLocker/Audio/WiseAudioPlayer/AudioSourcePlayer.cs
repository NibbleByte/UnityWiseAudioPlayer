using DevLocker.Audio.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Audio;

namespace DevLocker.Audio
{
	/// <summary>
	/// Wraps around the AudioSource offering API improvements and simpler interface.
	/// Also supports fading in and out sounds when interrupted (Stop, Pause, UnPause).
	///
	/// Can play multiple sounds at the same time which results in multiple AudioSources used.
	/// </summary>
	[AddComponentMenu("Audio/Audio Source Player")]
	public class AudioSourcePlayer : MonoBehaviour
	{
		public enum AudioSourcesPoolMode
		{
			PlayerObjectPool,
			PlayerChildPool,
			GlobalPool,
		}

		public enum RepeatPatternType
		{
			Once = 0,
			Loop = 1,

			RepeatInterval = 4,
		}

		[Serializable]
		public struct RepeatOptions
		{
			public RepeatPatternType Pattern;

			[Tooltip("How much seconds to wait AFTER audio finished playing so it can start again. Will select random value within range.")]
			public float MinSeconds;
			[Tooltip("How much seconds to wait AFTER audio finished playing so it can start again. Will select random value within range.")]
			public float MaxSeconds;

			public float NextIntervalValue() => Pattern == RepeatPatternType.RepeatInterval ? UnityEngine.Random.Range(MinSeconds, MaxSeconds) : 0f;

			public bool IsLooping => Pattern != RepeatPatternType.Once;

			public bool IsPatternOnce => Pattern == RepeatPatternType.Once;
			public bool IsPatternLoop => Pattern == RepeatPatternType.Loop;
			public bool IsPatternInterval => Pattern == RepeatPatternType.RepeatInterval;

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

		/// <summary>
		/// Holds <see cref="AudioResource"/> and <see cref="AudioPlayerAsset"/> together so user can select any type of audioReference.
		/// Only one member should have a valid reference at all times.
		/// </summary>
		[Serializable]
		public struct AudioReferenceProperty
		{
			[SerializeField] private AudioResource m_AudioResource;
			[SerializeField] private AudioPlayerAsset m_AudioAsset;

			public AudioReferenceProperty(AudioResource resource) { m_AudioResource = resource; m_AudioAsset = null; }
			public AudioReferenceProperty(AudioPlayerAsset asset) { m_AudioAsset = asset; m_AudioResource = null; }

			public AudioResource AudioResource {
				get => m_AudioResource;
				set { m_AudioResource = value; m_AudioAsset = null; }
			}
			public AudioPlayerAsset AudioAsset {
				get => m_AudioAsset;
				set { m_AudioAsset = value; m_AudioAsset = null; }
			}

			public bool HasValidReference => m_AudioResource != null || m_AudioAsset != null;

			// Officially only one reference should be filled, but editor and serialization may bypass this logic.
			// In that case prefer using the AudioResource as that is the one being displayed in the editor.
			public bool IsAmbiguous => m_AudioAsset && m_AudioResource;

			public bool OnValidate(object context)
			{
				if (IsAmbiguous) {
					Debug.LogWarning($"[Audio] {nameof(AudioReferenceProperty)} reference has both references set: \"{m_AudioResource}\" and \"{m_AudioAsset}\" for \"{context}\"", context as UnityEngine.Object);
					return false;
				}

				return true;
			}
		}

		/// <summary>
		/// Represents audio playback in progress.
		/// </summary>
		public class PlaybackState
		{
			public readonly AudioSourcesPoolMode SourcesPoolMode;
			public AudioSourcePlayer Player { get; internal set; }
			public AudioSource AudioSource { get; internal set; }
			public AudioPlayerAsset AudioPlayerAsset { get; internal set; }
			public bool ConductorFinished { get; internal set; }
			public readonly float StartTime;

			public bool IsPlaying => AudioSource != null && (!ConductorFinished || (AudioSource.isPlaying || IsPaused) || (!IsUsingConductors && RepeatPattern.IsPatternInterval));
			public bool IsPaused => Player && Player.IsPaused;				// When AudioSource is paused, isPlaying returns false. No isPaused property, so we have to track this ourselves :(
			public bool IsUsingConductors { get; internal set; }    // Because using an audioReference or playing standalone conductor.

			public RepeatOptions RepeatPattern { get; internal set; }
			public AudioMixerGroup Output => AudioSource ? AudioSource.outputAudioMixerGroup : null;
			public AudioSource Template { get; internal set; }

			internal Coroutine ConductorCoroutine;
			internal Coroutine StopFadeCoroutine;
			internal Coroutine PauseFadeCoroutine;
			internal float PauseInitialVolume;

			internal float NextPlayTime;					// For RepeatInterval pattern.
			internal bool LastIsPlayingForRepeatInterval;   // For RepeatInterval pattern.

			internal bool Cancelled;

			public PlaybackState(AudioSourcePlayer player, AudioSource audioSource, AudioSourcesPoolMode poolMode, RepeatOptions repeatPattern, float startTime)
			{
				SourcesPoolMode = poolMode;
				AudioSource = audioSource;
				Player = player;
				RepeatPattern = repeatPattern;
				StartTime = startTime;
			}

			#region Direct Play for Conductors

			/// <summary>
			/// Used by <see cref="AudioPlayerAsset.AudioConductor"/> to play sound without changing the player properties.
			/// This way, the <see cref="Editor.AudioSourcePlayerMonitorWindow"/> will show the correct sound.
			/// </summary>
			public void PlayDirectClip(AudioClip clip, float volume = 1.0f, float pitch = 1.0f)
			{
				if (clip == null)
					throw new ArgumentNullException();

				AudioSource.clip = clip;
				AudioSource.pitch = pitch;

				AudioSource.volume = volume * Player.Volume;
				AudioSource.Play();
			}

			/// <summary>
			/// Used by <see cref="AudioPlayerAsset.AudioConductor"/> when PlayOneShot() needs to be used.
			/// </summary>
			public void PlayDirectClipOneShot(AudioClip clip, float volume = 1.0f)
			{
				if (clip == null)
					throw new ArgumentNullException();

				// So it shows up correctly in the audio monitor window.
				AudioSource.clip = clip;
				AudioSource.volume = volume * Player.Volume;

				AudioSource.PlayOneShot(clip);
			}

			/// <summary>
			/// Used by <see cref="AudioPlayerAsset.AudioConductor"/> to play sound without changing the player properties.
			/// This way, the <see cref="Editor.AudioSourcePlayerMonitorWindow"/> will show the correct sound.
			/// </summary>
			public void PlayDirectClip(ClipWithVolume clipPair, float pitch = 1.0f, int volumeOffsetDB = 0)
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
			/// Used by <see cref="AudioPlayerAsset.AudioConductor"/> to play sound without changing the player properties.
			/// This way, the <see cref="Editor.AudioSourcePlayerMonitorWindow"/> will show the correct sound.
			/// </summary>
			public void PlayDirectClip(ClipWithVolumePitch clipPair, int volumeOffsetDB = 0, int pitchOffsetCents = 0)
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
			/// Used by <see cref="AudioPlayerAsset.AudioConductor"/> to play sound without changing the player properties.
			/// This way, the <see cref="Editor.AudioSourcePlayerMonitorWindow"/> will show the correct sound.
			/// </summary>
			public void PlayDirectResource(AudioResource resource, float pitch = 1.0f)
			{
				if (resource == null)
					throw new ArgumentNullException();

				AudioSource.resource = resource;
				AudioSource.pitch = pitch;

				AudioSource.Play();
			}

			/// <summary>
			/// Used by <see cref="AudioPlayerAsset.AudioConductor"/> to play sound without changing the player properties.
			/// This way, the <see cref="Editor.AudioSourcePlayerMonitorWindow"/> will show the correct sound.
			/// </summary>
			public void PlayDirectResource(ResourceWithVolume resourcePair, float pitch = 1.0f, int volumeOffsetDB = 0)
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
				float startTime = Time.time;
				float startVolume = fadeIn ? 0f : workingVolume;
				float endVolume = fadeIn ? workingVolume : 0f;

				while (Time.time - startTime < fadeSeconds) {
					AudioSource.volume = Mathf.Lerp(startVolume, endVolume, (Time.time - startTime) / fadeSeconds);
					yield return null;
				}

				callbackOnFinish?.Invoke();
			}

			#endregion
		}

		public delegate void PlaybackEventHandler(PlaybackState playback);
		public static event PlaybackEventHandler PlaybackStarted;
		public static event PlaybackEventHandler PlaybackPaused;
		public static event PlaybackEventHandler PlaybackUnpaused;
		public static event PlaybackEventHandler PlaybackStopped;

		/// <summary>
		/// One player can manage many audio sources as it would create new a one every time a new sound is played while the old source is still playing.
		///
		/// Where should AudioSource components be created? If selected location is this object or a child one the sound would stop immediately on destroying the player.
		/// </summary>
		public AudioSourcesPoolMode SourcesPoolMode { get => m_SourcesPoolMode; set => m_SourcesPoolMode = value; }

		/// <summary>
		/// Gets or sets the used audio reference.
		/// If <see cref="AudioPlayerAsset"/> is used, it will override some of the player fields like repeat, output mixer, etc.
		///
		/// NOTE: Don't use from conductors!!! Use <see cref="PlaybackState.PlayDirectResource(AudioResource, float)"/> instead.
		/// </summary>
		public AudioReferenceProperty AudioReference {
			get => m_AudioReference;
			set {
				value.OnValidate(this);

				m_AudioReference = value;
			}
		}

		/// <summary>
		/// Object used by <see cref="AudioPlayerAsset"/> filters as context.
		/// Works great with <see cref="Conductors.DictionaryContext"/>, but you can have your custom implementation of <see cref="Conductors.IValuesContainer"/>.
		/// </summary>
		public object ConductorsFilterContext;

		/// <summary>
		/// Used by conductors to persist state per player between usages. For example: don't repeat last asset.
		/// Try to use unique key names.
		/// </summary>
		public Dictionary<string, object> ConductorsStateStorage = new Dictionary<string, object>();

		/// <summary>
		/// Output mixer to use. Will be overriden by <see cref="AudioPlayerAsset"/>.
		/// </summary>
		public AudioMixerGroup Output { get => m_Output; set => m_Output = value; }

		/// <summary>
		/// Since <see cref="AudioPlayerAsset"/> can override the output mixer, call this to get the effective output mixer to be used.
		/// </summary>
		public AudioMixerGroup GetEffectiveOutput(AudioPlayerAsset asset = null)
		{
			if (asset == null) {
				if (m_Output)
					return m_Output;

				if (m_Template)
					return m_Template.outputAudioMixerGroup;

			} else {

				if (asset.OutputMixer)
					return asset.OutputMixer;

				if (asset.Template)
					return asset.Template.outputAudioMixerGroup;
			}

			return null;
		}

		/// <summary>
		/// Template prefab to use. Will be overriden by <see cref="AudioPlayerAsset"/>.
		/// </summary>
		public AudioSource Template { get => m_Template; set => m_Template = value; }

		/// <summary>
		/// Since <see cref="AudioPlayerAsset"/> can override the template, call this to get the effective template to be used.
		/// </summary>
		public AudioSource GetEffectiveTemplate(AudioPlayerAsset asset = null) => asset == null ? m_Template : asset.Template;

		public bool Mute {
			get => m_Mute;
			set {
				m_Mute = value;

				foreach(var playback in m_ActivePlaybacks) {
					if (playback.AudioSource) {
						playback.AudioSource.mute = value;
					}
				}
			}
		}

		public bool PlayOnEnable { get => m_PlayOnEnable; set => m_PlayOnEnable = value; /* Handled by us. */ }

		/// <summary>
		/// Short-cut to see if audio is looping.
		/// </summary>
		public bool Loop {
			get => RepeatPattern.IsLooping;
			set {
				var repeatPattern = RepeatPattern;
				repeatPattern.Pattern = value ? RepeatPatternType.Loop : RepeatPatternType.Once;
				RepeatPattern = repeatPattern;
			}
		}

		/// <summary>
		/// Repeat pattern to use. Will be overriden by <see cref="AudioPlayerAsset"/>.
		/// </summary>
		public RepeatOptions RepeatPattern { get => m_RepeatPattern; set => m_RepeatPattern = value; }

		/// <summary>
		/// Since <see cref="AudioPlayerAsset"/> overrides the repeat pattern, use this to get the actually used repeat pattern.
		/// </summary>
		public RepeatOptions GetEffectiveRepeatPattern(AudioPlayerAsset asset = null) => asset == null ? m_RepeatPattern : asset.RepeatPattern;

		/// <summary>
		/// Volume used when playing new sounds - will not affect already playing sounds.
		/// </summary>
		public float Volume { get => m_Volume; set => m_Volume = value; }

		/// <summary>
		/// Index of the gamepad output to use. -1 means normal speakers output instead of gamepad. Valid slots are 0-3.
		/// This is used only for platforms that support gamepad audio output (like Playstation).
		/// For more info check the <see cref="AudioSource.PlayOnGamepad(int)"/> and <see cref="AudioSource.DisableGamepadOutput()"/>
		/// </summary>
		[NonSerialized]
		public int GamepadOutputIndex = -1;

		public float LastPlayTime { get; private set; }

		public IReadOnlyList<PlaybackState> ActivePlaybacks => m_ActivePlaybacks.AsReadOnly();

		public static IReadOnlyList<AudioSourcePlayer> ActivePlayersRegister => m_ActivePlayersRegister.AsReadOnly();

		[SerializeField]
		[Tooltip("One player can manage many audio sources as it would create a new one every time a new sound is started while the old source is still playing.\n\n" +
			"Where should AudioSource components be created?\n" +
			"- " + nameof(AudioSourcesPoolMode.PlayerObjectPool) + " - this object\n" +
			"- " + nameof(AudioSourcesPoolMode.PlayerChildPool) + " - as child of this object\n" +
			"- " + nameof(AudioSourcesPoolMode.GlobalPool) + " - global pool away from this object\n\n" +
			"When source is attached to this or child object the playing sound would stop immediately on destroying the object. Use global pool to avoid this.")]
		private AudioSourcesPoolMode m_SourcesPoolMode;

		[SerializeField]
		[Tooltip("Audio reference to play - standard Unity audio asset or customizable Audio Player Asset.")]
		private AudioReferenceProperty m_AudioReference;


		[SerializeField]
		[Tooltip("Audio mixer to use.\n\nWill be overriden by the audio asset's mixer.\nIf left empty, it will copy the one of the template")]
		private AudioMixerGroup m_Output;

		[SerializeField]
		[Tooltip("Prefab (or scene object) to be used as template when initializing the AudioSource properties.\n\nWill be overriden by the audio asset's template.")]
		private AudioSource m_Template;

		[SerializeField]
		[Tooltip("Mute the sound")]
		private bool m_Mute = false;

		[SerializeField]
		[Tooltip("Play automatically every time this component is enabled?")]
		private bool m_PlayOnEnable = true;

		[SerializeField]
		[Tooltip("How sound should be repeated. Repeat interval allows you to specify seconds of silence every time after audio finished playing.\n\nWill be overriden when audio asset is used.")]
		private RepeatOptions m_RepeatPattern;

		[Tooltip("Fade duration when sound is interrupted (Stop, Pause, Unpause)")]
		public float InterruptionFadeDuration = 0.2f;

		[Range(0f, 1f)]
		[SerializeField]
		[Tooltip("Volume of the sound")]
		private float m_Volume = 1f;

		private static readonly List<AudioSourcePlayer> m_ActivePlayersRegister = new List<AudioSourcePlayer>();

		private List<PlaybackState> m_ActivePlaybacks = new List<PlaybackState>();
		private Queue<AudioSource> m_AudioSourcesPool = new Queue<AudioSource>();
		private const string ChildPoolContainerName = "__AudioSourcePlayerPool__";

		// Can't have global 3D object and reuse it as moving it will affect all the currently played sounds as well.
		public static AudioSourcePlayer Quick2DPlayer;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ClearStaticsCache()
		{
			Quick2DPlayer = null;
		}

		protected virtual void OnEnable()
		{
			m_ActivePlayersRegister.Add(this);

			if (PlayOnEnable && AudioReference.HasValidReference) {
				Play();
			}
		}

		protected virtual void OnDisable()
		{
			// Also called on destroy, which works fine for us.

			m_ActivePlayersRegister.Remove(this);

			while(m_ActivePlaybacks.Count > 0) {
				// If we are disabled because we are being destroyed, keep playing the source
				// in case it is in the global pool, allowing it to finish.
				// If source is child of this object it will stop automatically anyway.
				// If source is looping - always stop, or it may play forever if only player is disabled.
				var playback = m_ActivePlaybacks.Last();
				ReleaseAudioPlayback(playback, stopSource: playback.AudioSource && playback.AudioSource.loop);
			}
		}

		protected virtual void OnDestroy()
		{
			// In case player is destroyed (but not the object) clean up the local sources pool.
			foreach(var source in m_AudioSourcesPool) {
				if (source && source.gameObject == gameObject) {
					GameObject.Destroy(source);
				}
			}
			
			var childContainer = transform.Find(ChildPoolContainerName);
			if (childContainer) {
				GameObject.Destroy(childContainer.gameObject);
			}
			
			m_AudioSourcesPool.Clear();
		}

		protected virtual void OnValidate()
		{
#if UNITY_EDITOR
			AudioReference.OnValidate(this);

			if (InterruptionFadeDuration < 0f) {
				InterruptionFadeDuration = 0f;
				UnityEditor.EditorUtility.SetDirty(this);
			}

			m_RepeatPattern.OnValidate(this);

			if (Application.isPlaying) {
				foreach(var playback in m_ActivePlaybacks) {
					if (playback.AudioSource) {
						if (playback.AudioSource.volume != m_Volume) {
							playback.AudioSource.volume = m_Volume;
						}
						if (playback.AudioSource.mute != m_Mute) {
							playback.AudioSource.mute = m_Mute;
						}
					}
				}
			}
#endif
		}

		public bool IsPlaying => m_ActivePlaybacks.Count > 0;
		public bool IsPaused { get; private set; }

		/// <summary>
		/// Easy way to wait for player to finish playing in your coroutines.
		/// </summary>
		public IEnumerator WaitToFinishPlaying()
		{
			while (IsPlaying)
				yield return null;
		}

		[ContextMenu("Play")]
		public void Play()
		{
			// Void return so Unity UI events can call this method.
			PlayImpl(m_AudioReference, 0f);
		}

		public void PlayDelayed(float delaySeconds)
		{
			// Void return so Unity UI events can call this method.
			PlayImpl(m_AudioReference, delaySeconds);
		}

		/// <summary>
		/// Can be used for animation event to play given <see cref="AudioReferenceProperty"/>.
		/// </summary>
		public void PlayClip(AudioResource clip)
		{
			// Void return so Unity UI events can call this method.
			PlayImpl(new AudioReferenceProperty(clip), 0f);
		}

		/// <summary>
		/// Can be used for animation event to play given <see cref="AudioReferenceProperty"/>.
		/// </summary>
		public void PlayAsset(AudioPlayerAsset asset)
		{
			// Void return so Unity UI events can call this method.
			PlayImpl(new AudioReferenceProperty(asset), 0f);
		}


		public PlaybackState PlayAudioReference(AudioReferenceProperty audioReference, float delay = 0f)
		{
			audioReference.OnValidate(this);

			return PlayImpl(audioReference, delay);
		}

		protected virtual PlaybackState PlayImpl(AudioReferenceProperty audioReference, float delay)
		{
			if (IsPaused) {
				UnPause();
			}

			var playback = AcquireAudioPlayback();
			
			// Something went wrong, abort. Probably editor is quitting.
			if (playback == null)
				return null;

			if (audioReference.AudioAsset != null) {

				var conductorCoroutine = StartCoroutine(StartAssetPlayback(audioReference.AudioAsset, delay, playback));

				// We can get the coroutine after running it initially, but it may already have finished.
				if (playback.IsPlaying) {
					playback.ConductorCoroutine = conductorCoroutine;
				}

			} else {
				StartResourcePlayback(audioReference.AudioResource, delay, playback);
			}

			// If AudioSource is missing playback got released immediately, which means it didn't actually play.
			if (!playback.Cancelled && playback.AudioSource != null) {
				LastPlayTime = Time.time;
				PlaybackStarted?.Invoke(playback);
			}

			return playback;
		}

		/// <summary>
		/// Stop all currently playing sounds. If <see cref="InterruptionFadeDuration"/> is non-zero value, will fade the sound first, then stop it.
		/// </summary>
		[ContextMenu("Stop")]
		public void Stop()
		{
			Stop(InterruptionFadeDuration);
		}

		/// <summary>
		/// Stop all currently playing sounds. If <see cref="interruptionFadeDuration"/> is non-zero value, will fade the sound first, then stop it.
		/// </summary>
		public virtual void Stop(float interruptionFadeDuration)
		{
			IsPaused = false;

			if (interruptionFadeDuration > 0f) {

				foreach(var playback in m_ActivePlaybacks.ToList()) {
					if (playback.IsPlaying && playback.StopFadeCoroutine == null) {
						playback.StopConductorCoroutine();
						playback.ConductorFinished = true;

						// If fading to stop replaces fading to pause.
						playback.StopPauseFadeCoroutine();

						// If still playing, try fading it out.
						if (playback.IsPlaying) {
							var callbackState = playback;    // Closure capture for the callback.
							playback.StopFadeCoroutine = StartCoroutine(playback.FadeVolumeCrt(interruptionFadeDuration, playback.AudioSource.volume, fadeIn: false, () => {
								ReleaseAudioPlayback(callbackState);
							}));

							PlaybackStopped?.Invoke(playback);
						}
						// Else - it wasn't playing and it will be released up on update.

					}
				}

			} else {

				while (m_ActivePlaybacks.Count > 0) {

					// Before actually releasing the playback, so it can be used by the event.
					PlaybackStopped?.Invoke(m_ActivePlaybacks.Last());

					ReleaseAudioPlayback(m_ActivePlaybacks.Last());
				}
			}
		}

		/// <summary>
		/// Stop specific playback. If <see cref="InterruptionFadeDuration"/> is non-zero value, will fade the sound first, then stop it.
		/// </summary>
		public virtual void Stop(PlaybackState playback) => Stop(playback, InterruptionFadeDuration);

		/// <summary>
		/// Stop specific playback. If <see cref="interruptionFadeDuration"/> is non-zero value, will fade the sound first, then stop it.
		/// </summary>
		public virtual void Stop(PlaybackState playback, float interruptionFadeDuration)
		{
			// Not one of ours or it already finished.
			if (!m_ActivePlaybacks.Contains(playback))
				return;

			if (interruptionFadeDuration > 0f && !IsPaused) {

				if (playback.IsPlaying && playback.StopFadeCoroutine == null) {
					playback.StopConductorCoroutine();
					playback.ConductorFinished = true;

					// If fading to stop replaces fading to pause.
					playback.StopPauseFadeCoroutine();

					// If still playing, try fading it out.
					if (playback.IsPlaying) {
						var callbackState = playback;    // Closure capture for the callback.
						playback.StopFadeCoroutine = StartCoroutine(playback.FadeVolumeCrt(interruptionFadeDuration, playback.AudioSource.volume, fadeIn: false, () => {
							ReleaseAudioPlayback(callbackState);
						}));

						PlaybackStopped?.Invoke(playback);
					}
					// Else - it wasn't playing and it will be released up on update.

				}

			} else {

				// Before actually releasing the playback, so it can be used by the event.
				PlaybackStopped?.Invoke(playback);

				ReleaseAudioPlayback(playback);
			}
		}

		/// <summary>
		/// Instantly stop all active playbacks except the one provided.
		/// </summary>
		public virtual void StopAllPlaybacksExcept(PlaybackState keptPlayback)
		{
			for (int i = m_ActivePlaybacks.Count - 1; i >= 0; i--) {
				var playback = m_ActivePlaybacks[i];
				if (playback != keptPlayback) {

					// Before actually releasing the playback, so it can be used by the event.
					PlaybackStopped?.Invoke(playback);

					ReleaseAudioPlayback(playback);
				}
			}
		}

		/// <summary>
		/// Instantly stop all active playbacks that use the given asset.
		/// </summary>
		public virtual void StopAllPlaybacksExcept(AudioPlayerAsset keptAsset, PlaybackState excludePlayback = null)
		{
			for (int i = m_ActivePlaybacks.Count - 1; i >= 0; i--) {
				var playback = m_ActivePlaybacks[i];
				if (playback.AudioPlayerAsset == keptAsset && excludePlayback != playback) {

					// Before actually releasing the playback, so it can be used by the event.
					PlaybackStopped?.Invoke(playback);

					ReleaseAudioPlayback(playback);
				}
			}
		}

		[ContextMenu("Pause")]
		public virtual void Pause()
		{
			if (IsPaused)
				return;

			IsPaused = true;

			if (InterruptionFadeDuration > 0f) {

				foreach (var playback in m_ActivePlaybacks) {
					if (playback.IsPlaying && playback.StopFadeCoroutine == null) {

						// Stop any previous pause sequence.
						playback.StopPauseFadeCoroutine();

						var callbackState = playback;    // Closure capture for the callback.
						playback.PauseInitialVolume = playback.PauseInitialVolume > 0f ? playback.PauseInitialVolume : playback.AudioSource.volume;
						playback.PauseFadeCoroutine = StartCoroutine(playback.FadeVolumeCrt(InterruptionFadeDuration, playback.PauseInitialVolume, fadeIn: false, () => {
							callbackState.AudioSource.Pause();
						}));

						PlaybackPaused?.Invoke(playback);
					}
				}

			} else {

				// Audio assets keep going and wait for the player to get unpaused.
				foreach (var playback in m_ActivePlaybacks) {
					if (playback.AudioSource) {
						playback.AudioSource.Pause();

						PlaybackPaused?.Invoke(playback);
					}
				}
			}
		}

		[ContextMenu("UnPause")]
		public virtual void UnPause()
		{
			if (!IsPaused)
				return;

			IsPaused = false;

			if (InterruptionFadeDuration > 0f) {
				foreach (var playback in m_ActivePlaybacks) {
					if (playback.AudioSource && playback.StopFadeCoroutine == null) {

						// Stop any previous pause sequence.
						playback.StopPauseFadeCoroutine();

						playback.AudioSource.UnPause();
						float workingVolume = playback.PauseInitialVolume > 0f ? playback.PauseInitialVolume : m_Volume;
						playback.PauseInitialVolume = 0f;
						playback.PauseFadeCoroutine = StartCoroutine(playback.FadeVolumeCrt(InterruptionFadeDuration, workingVolume, fadeIn: true));

						PlaybackUnpaused?.Invoke(playback);
					}
				}

			} else {

				// Audio assets keep updating while paused.
				foreach (var playback in m_ActivePlaybacks) {
					if (playback.AudioSource) {
						playback.AudioSource.UnPause();

						PlaybackUnpaused?.Invoke(playback);
					}
				}
			}
		}

		/// <summary>
		/// Destroy the component + audio source OR the whole game object.
		/// If <see cref="InterruptionFadeDuration"/> is non-zero value, will fade the sound first, then destroy it.
		/// </summary>
		public virtual void DestroyPlayer(bool destroyGameObject)
		{
			Action destroyAction = () => {
				if (destroyGameObject) {
					GameObject.Destroy(gameObject);
				} else {
					GameObject.Destroy(this);
				}
			};

			if (InterruptionFadeDuration > 0f) {

				bool anyFades = false;
				
				foreach (var playback in m_ActivePlaybacks) {
					
					if (playback.IsPlaying) {
						playback.StopConductorCoroutine();
						playback.ConductorFinished = true;

						// If fading to stop replaces fading to pause.
						playback.StopPauseFadeCoroutine();

						bool wasStopping = playback.StopFadeCoroutine != null;
						// Overrides any Stop() fading out.
						playback.StopStopFadeCoroutine();

						// If still playing, try fading it out.
						if (playback.IsPlaying && !playback.IsPaused) {
							var callbackState = playback;    // Closure capture for the callback.
							playback.StopFadeCoroutine = StartCoroutine(playback.FadeVolumeCrt(InterruptionFadeDuration, playback.AudioSource.volume, fadeIn: false, () => {
								ReleaseAudioPlayback(callbackState);

								// Last one released, destroy the player. Child sources will be destroyed automatically, but global pool sources will be returned.
								if (m_ActivePlaybacks.Count == 0) {
									destroyAction();
								}
							}));

							anyFades = true;
							if (!wasStopping) {
								PlaybackStopped?.Invoke(playback);
							}

						}

					}
				}
				
				if (!anyFades) {
					destroyAction();
				}
				
			} else {

				while (m_ActivePlaybacks.Count > 0) {
					PlaybackStopped?.Invoke(m_ActivePlaybacks.Last());

					ReleaseAudioPlayback(m_ActivePlaybacks.Last());
				}

				destroyAction();
			}
		}

		public void DestroyPlayerWhenFinishedPlaying(bool destroyGameObject)
		{
			IEnumerator DestroyWhenFinishedPlaying()
			{
				while (IsPlaying)
					yield return null;

				if (destroyGameObject) {
					GameObject.Destroy(gameObject);
				} else {
					GameObject.Destroy(this);
				}
			}

			StartCoroutine(DestroyWhenFinishedPlaying());
		}

		#region Quick Play Static Helpers

		/// <summary>
		/// Play audio quickly as 2D sound from code on specified object (will automatically create player on it).
		/// If no game object is specified a default one will be used.
		/// If you want to control the player, set the player yourself.
		/// </summary>
		public static PlaybackState Play2DAudio(AudioClip clip, GameObject gameObject = null)
			=> Play2DAudio(new AudioReferenceProperty(clip), gameObject);

		/// <summary>
		/// Play audio quickly as 2D sound from code on specified object (will automatically create player on it).
		/// If no game object is specified a default one will be used.
		/// If you want to control the player, set the player yourself.
		/// </summary>
		public static PlaybackState Play2DAudio(AudioPlayerAsset asset, GameObject gameObject = null)
			=> Play2DAudio(new AudioReferenceProperty(asset), gameObject);

		/// <summary>
		/// Play audio quickly as 2D sound from code on specified object (will automatically create player on it).
		/// If no game object is specified a default one will be used.
		/// If you want to control the player, set the player yourself.
		/// </summary>
		public static PlaybackState Play2DAudio(AudioReferenceProperty audioReference, GameObject gameObject = null)
		{
			AudioSourcePlayer player;
			bool setAsDefaultPlayer = false;

			if (gameObject == null) {
				if (Quick2DPlayer == null) {
					gameObject = new GameObject("2D Audio Player");
					setAsDefaultPlayer = true;
				}

				player = Quick2DPlayer;
			} else {
				player = gameObject.GetComponent<AudioSourcePlayer>();
			}

			if (player == null) {
				player = gameObject.AddComponent<AudioSourcePlayer>();
				player.PlayOnEnable = false;

				// This is not needed as audio sources by default are 2D.
				//var templateSource = player.gameObject.AddComponent<AudioSource>();
				//templateSource.spatialBlend = 0;
				//
				//player.Template = templateSource;

				if (setAsDefaultPlayer) {
					Quick2DPlayer = player;
				}
			}

			return player.PlayAudioReference(audioReference);
		}

		/// <summary>
		/// Play audio quickly as 3D sound from code on specified object (will automatically create player on it).
		/// Provide template to specify the spatial blend curve (or leave empty for the defaults).
		/// If you want to control the player, set the player yourself.
		/// </summary>
		public static PlaybackState Play3DAudio(AudioClip clip, GameObject gameObject, AudioSource template = null)
			=> Play3DAudio(new AudioReferenceProperty(clip), gameObject, template);

		/// <summary>
		/// Play audio quickly as 3D sound from code on specified object (will automatically create player on it).
		/// Provide template to specify the spatial blend curve (or leave empty for the defaults).
		/// If you want to control the player, set the player yourself.
		/// </summary>
		public static PlaybackState Play3DAudio(AudioPlayerAsset asset, GameObject gameObject, AudioSource template = null)
			=> Play3DAudio(new AudioReferenceProperty(asset), gameObject, template);

		/// <summary>
		/// Play audio reference quickly as 3D sound from code on specified object (will automatically create player on it).
		/// Provide template to specify the spatial blend curve (or leave empty for the defaults).
		/// If you want to control the player, set the player yourself.
		/// </summary>
		public static PlaybackState Play3DAudio(AudioReferenceProperty audioReference, GameObject gameObject, AudioSource template = null)
		{
			AudioSourcePlayer player = gameObject.GetComponent<AudioSourcePlayer>();

			if (player == null) {
				player = gameObject.AddComponent<AudioSourcePlayer>();
				player.PlayOnEnable = false;

				if (template == null) {
					var templateSource = player.gameObject.AddComponent<AudioSource>();
					templateSource.spatialBlend = 1;

					player.Template = templateSource;
				} else {
					player.Template = template;
				}
			}

			return player.PlayAudioReference(audioReference);
		}

		/// <summary>
		/// Play audio quickly as 3D sound from code on specified position (will create a temporary player on that position).
		/// Provide template to specify the spatial blend curve (or leave empty for the defaults).
		/// If you want to control the player, set the player yourself.
		/// </summary>
		public static PlaybackState Play3DAudio(AudioClip clip, Vector3 position, AudioSource template = null)
			=> Play3DAudio(new AudioReferenceProperty(clip), position, template);

		/// <summary>
		/// Play audio quickly as 3D sound from code on specified position (will create a temporary player on that position).
		/// Provide template to specify the spatial blend curve (or leave empty for the defaults).
		/// If you want to control the player, set the player yourself.
		/// </summary>
		public static PlaybackState Play3DAudio(AudioPlayerAsset asset, Vector3 position, AudioSource template = null)
			=> Play3DAudio(new AudioReferenceProperty(asset), position, template);

		/// <summary>
		/// Play audio quickly as 3D sound from code on specified position (will create a temporary player on that position).
		/// Provide template to specify the spatial blend curve (or leave empty for the defaults).
		/// If you want to control the player, set the player yourself.
		/// </summary>
		public static PlaybackState Play3DAudio(AudioReferenceProperty audioReference, Vector3 position, AudioSource template = null)
		{
			// Can't have global 3D object and reuse it as moving it will affect all the currently played sounds as well.
			AudioSourcePlayer player = new GameObject("One Shot 3D Audio").AddComponent<AudioSourcePlayer>();
			player.gameObject.hideFlags = HideFlags.HideInHierarchy;
			player.transform.position = position;
			player.PlayOnEnable = false;

			if (template == null) {
				var templateSource = player.gameObject.AddComponent<AudioSource>();
				templateSource.spatialBlend = 1;

				player.Template = templateSource;
			} else {
				player.Template = template;
			}

			var playback = player.PlayAudioReference(audioReference);

			player.DestroyPlayerWhenFinishedPlaying(destroyGameObject: true);

			return playback;
		}

		#endregion

		private IEnumerator StartAssetPlayback(AudioPlayerAsset asset, float delay, PlaybackState playback)
		{
			playback.IsUsingConductors = true;
			playback.AudioSource.outputAudioMixerGroup = GetEffectiveOutput(asset);
			playback.AudioSource.loop = GetEffectiveRepeatPattern(asset).IsPatternLoop;
			playback.RepeatPattern = GetEffectiveRepeatPattern(asset);

			playback.Template = GetEffectiveTemplate(asset);
			if (playback.Template) {
				CopyAudioSourceDetails(playback.AudioSource, playback.Template);
			} else {
				ResetAudioSourceDetails(playback.AudioSource);
			}

			playback.AudioPlayerAsset = asset;
			
			delay += asset.Delay;

			if (delay > 0f) {
				float waitTime = 0f;
				while (waitTime < delay) {
					yield return null;

					if (!IsPaused) {
						waitTime += Time.deltaTime;
					}
				}
			}

			yield return asset.Play(playback, ConductorsFilterContext);

			// Signal that conductor finished playing (which doesn't mean the audio finished).
			playback.ConductorFinished = true;
			if (!playback.IsPlaying) {
				ReleaseAudioPlayback(playback);
			}
		}

		private void StartResourcePlayback(AudioResource resource, float delay, PlaybackState playback)
		{
			playback.IsUsingConductors = false;
			playback.AudioSource.outputAudioMixerGroup = GetEffectiveOutput();
			playback.AudioSource.loop = GetEffectiveRepeatPattern().IsPatternLoop;
			playback.RepeatPattern = GetEffectiveRepeatPattern();

			playback.Template = GetEffectiveTemplate();
			if (playback.Template) {
				CopyAudioSourceDetails(playback.AudioSource, playback.Template);
			} else {
				ResetAudioSourceDetails(playback.AudioSource);
			}

			playback.AudioSource.resource = resource;

			if (delay <= 0f) {
				playback.AudioSource.Play();
			} else {
				playback.AudioSource.PlayDelayed(delay);
			}

			playback.ConductorFinished = true; // No conductor for normal audio resource.
		}

		protected virtual void Update()
		{
			if (IsPaused)
				return;

			for (int i = 0; i < m_ActivePlaybacks.Count; i++) {
				PlaybackState playback = m_ActivePlaybacks[i];

				// Will be handled by the coroutine itself.
				if (playback.StopFadeCoroutine != null)
					continue;

				if (!playback.IsPlaying) {
					// Will remove state from list.
					ReleaseAudioPlayback(playback);
					--i;
					continue;
				}

				// Global pool sources are not children of this object, so we need to update their position manually.
				if (playback.SourcesPoolMode == AudioSourcesPoolMode.GlobalPool) {
					playback.AudioSource.transform.position = transform.position;
				}

				bool isRepeatIntervalPattern = !playback.IsUsingConductors && playback.RepeatPattern.IsPatternInterval;
				if (isRepeatIntervalPattern) {

					if (playback.AudioSource.isPlaying != playback.LastIsPlayingForRepeatInterval) {
						if (!playback.AudioSource.isPlaying) {
							playback.NextPlayTime = Time.time + playback.RepeatPattern.NextIntervalValue();
						}
						playback.LastIsPlayingForRepeatInterval = playback.AudioSource.isPlaying;
					}

					if (!playback.AudioSource.isPlaying && Time.time >= playback.NextPlayTime) {
						playback.AudioSource.Play();
					}
				}
			}
		}

		private PlaybackState AcquireAudioPlayback()
		{
			PlaybackState playback;

			if (m_AudioSourcesPool.Count > 0 && m_SourcesPoolMode != AudioSourcesPoolMode.GlobalPool) {
				var source = m_AudioSourcesPool.Dequeue();
				source.playOnAwake = false;
				source.resource = null;
				source.outputAudioMixerGroup = null;
				source.loop = false;
				source.volume = m_Volume;
				source.mute = m_Mute;
				// Details will be set by the user template.

#if UNITY_EDITOR || UNITY_PS4 || UNITY_PS5
				if (GamepadOutputIndex >= 0) {
					source.PlayOnGamepad(GamepadOutputIndex);
				} else {
					source.DisableGamepadOutput();
				}
#endif

				playback = new PlaybackState(this, source, m_SourcesPoolMode, m_RepeatPattern, Time.time);
				m_ActivePlaybacks.Add(playback);

				return playback;
			}

			switch (m_SourcesPoolMode) {
				case AudioSourcesPoolMode.PlayerObjectPool:
					playback = new PlaybackState(this, gameObject.AddComponent<AudioSource>(), m_SourcesPoolMode, m_RepeatPattern, Time.time);
					break;

				case AudioSourcesPoolMode.PlayerChildPool:
					var childContainer = transform.Find(ChildPoolContainerName);
					if (childContainer == null) {
						childContainer = new GameObject(ChildPoolContainerName).transform;
						childContainer.SetParent(transform, worldPositionStays: false);
					}

					playback = new PlaybackState(this, childContainer.gameObject.AddComponent<AudioSource>(), m_SourcesPoolMode, m_RepeatPattern, Time.time);
					break;

				case AudioSourcesPoolMode.GlobalPool:
					// Editor is quitting, pool is gone.
					if (AudioSourcesGlobalPool.Instance == null)
						return null;
					
					playback = new PlaybackState(this, AudioSourcesGlobalPool.Instance.AcquireAudioSource(), m_SourcesPoolMode, m_RepeatPattern, Time.time);
					playback.AudioSource.transform.position = transform.position;   // Global pool sources are not children of this object, so we need to set their position manually.
					break;

				default: throw new NotSupportedException("Unsupported location type " + m_SourcesPoolMode);
			}

			playback.AudioSource.playOnAwake = false;
			playback.AudioSource.volume = m_Volume;
			playback.AudioSource.mute = m_Mute;

#if UNITY_EDITOR || UNITY_PS4 || UNITY_PS5
			if (GamepadOutputIndex >= 0) {
				playback.AudioSource.PlayOnGamepad(GamepadOutputIndex);
			} else {
				playback.AudioSource.DisableGamepadOutput();
			}
#endif

			m_ActivePlaybacks.Add(playback);

			return playback;
		}

		private void ReleaseAudioPlayback(PlaybackState playback, bool stopSource = true)
		{
			if (playback.AudioSource == null) {
				m_ActivePlaybacks.Remove(playback);
				return;
			}

			if (stopSource) {
				playback.AudioSource.Stop();
			}

			playback.StopConductorCoroutine();
			playback.StopPauseFadeCoroutine();
			playback.StopStopFadeCoroutine();

			switch (playback.SourcesPoolMode) {
				case AudioSourcesPoolMode.PlayerObjectPool:
				case AudioSourcesPoolMode.PlayerChildPool:
					m_AudioSourcesPool.Enqueue(playback.AudioSource);
					break;
				case AudioSourcesPoolMode.GlobalPool:
					AudioSourcesGlobalPool.Instance?.ReleaseAudioSource(playback.AudioSource);
					break;
			}

			m_ActivePlaybacks.Remove(playback);
			
			// Prevent users from using this playback.
			playback.Player = null;
			playback.AudioSource = null;
		}

		public static void CopyAudioSource(AudioSource destination, AudioSource source)
		{
			destination.playOnAwake = source.playOnAwake;
			destination.resource = source.resource;
			destination.outputAudioMixerGroup = source.outputAudioMixerGroup;
			destination.loop = source.loop;
			destination.volume = source.volume;

			CopyAudioSourceDetails(destination, source);
		}

		public static void CopyAudioSourceDetails(AudioSource destination, AudioSource source)
		{
			destination.bypassEffects = source.bypassEffects;
			destination.bypassListenerEffects = source.bypassListenerEffects;
			destination.bypassReverbZones = source.bypassReverbZones;
			destination.priority = source.priority;
			destination.pitch = source.pitch;
			destination.panStereo = source.panStereo;
			//destination.spatialBlend = source.spatialBlend;		// Represents curve with 1 point, set below.
			destination.spatializePostEffects = source.spatializePostEffects;
			//destination.reverbZoneMix = source.reverbZoneMix;		// Represents curve with 1 point, set below.

			destination.dopplerLevel = source.dopplerLevel;
			//destination.spread = source.spread;					// Represents curve with 1 point, set below.
			destination.minDistance = source.minDistance;
			destination.maxDistance = source.maxDistance;
			destination.SetCustomCurve(AudioSourceCurveType.CustomRolloff, source.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
			destination.SetCustomCurve(AudioSourceCurveType.SpatialBlend, source.GetCustomCurve(AudioSourceCurveType.SpatialBlend));
			destination.SetCustomCurve(AudioSourceCurveType.ReverbZoneMix, source.GetCustomCurve(AudioSourceCurveType.ReverbZoneMix));
			destination.SetCustomCurve(AudioSourceCurveType.Spread, source.GetCustomCurve(AudioSourceCurveType.Spread));
			destination.rolloffMode = source.rolloffMode; // Because changing the curve changes this property to custom.
		}

		/// <summary>
		/// Reset audio source to the default values as if it was just created in the Inspector editor.
		/// </summary>
		public static void ResetAudioSource(AudioSource source)
		{
			source.playOnAwake = false;
			source.resource = null;
			source.outputAudioMixerGroup = null;
			source.loop = false;
			source.volume = 1f;
			source.mute = false;

			ResetAudioSourceDetails(source);
		}

		/// <summary>
		/// Reset audio source to the default values as if it was just created in the Inspector editor.
		/// </summary>
		public static void ResetAudioSourceDetails(AudioSource source)
		{
			// These were reverse-engineered from the default values of a new AudioSource component in the Inspector editor (in debug mode).

			source.bypassEffects = false;
			source.bypassListenerEffects = false;
			source.bypassReverbZones = false;
			source.priority = 128;
			source.pitch = 1f;
			source.panStereo = 0f;
			//source.spatialBlend = 0f;		// Represents curve with 1 point, set below.
			source.spatializePostEffects = false;
			//source.reverbZoneMix = 1f;	// Represents curve with 1 point, set below.

			source.dopplerLevel = 1f;
			//source.spread = 0f;			// Represents curve with 1 point, set below.
			source.minDistance = 1f;
			source.maxDistance = 500f;
			source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, new AnimationCurve(new Keyframe(0, 1, 0, 0), new Keyframe(1, 0, 0, 0)));
			source.SetCustomCurve(AudioSourceCurveType.SpatialBlend, new AnimationCurve(new Keyframe(0, 0)));
			source.SetCustomCurve(AudioSourceCurveType.ReverbZoneMix, new AnimationCurve(new Keyframe(0, 1)));
			source.SetCustomCurve(AudioSourceCurveType.Spread, new AnimationCurve(new Keyframe(0, 0)));

			source.rolloffMode = AudioRolloffMode.Logarithmic;  // Because changing the curve changes this property to custom.
																// Overrides the CustomRolloff curve, but the curve is still stored.
		}
	}
}
