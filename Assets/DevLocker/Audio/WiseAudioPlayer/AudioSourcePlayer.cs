using System;
using System.Collections;
using System.Collections.Generic;
using DevLocker.Audio.Utils;
using UnityEngine;
using UnityEngine.Audio;

namespace DevLocker.Audio
{
	/// <summary>
	/// Wraps around the AudioSource offering API improvements and simpler interface.
	/// Also supports fading in and out sounds when interrupted (Stop, Pause, UnPause).
	/// </summary>
	public class AudioSourcePlayer : MonoBehaviour
	{
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
		/// Holds <see cref="AudioResource"/> and <see cref="AudioPlayerAsset"/> together so user can select any type of asset.
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

		public delegate void PlayerEventHandler(AudioSourcePlayer player);
		public static event PlayerEventHandler PlayStarted;
		public static event PlayerEventHandler PlayPaused;
		public static event PlayerEventHandler PlayUnpaused;
		public static event PlayerEventHandler PlayStopped;

		/// <summary>
		/// Gets or sets the used audio reference.
		/// If <see cref="AudioPlayerAsset"/> is used, it will override some of the player fields like repeat, output mixer, etc.
		///
		/// NOTE: Don't use from conductors!!! Use <see cref="PlayDirectResource(AudioResource)"/> instead.
		/// </summary>
		public AudioReferenceProperty AudioReference {
			get => m_AudioReference;
			set {
				value.OnValidate(this);

				if (m_AudioReference.AudioAsset != value.AudioAsset) {
					// Audio asset may change the template, which may confuse StartAudioAsset().
					// Stop any running coroutines now to prevent this.
					StopConductorCrt();
				}

				m_AudioReference = value;
				if (m_AudioSource) {
					m_AudioSource.resource = m_AudioReference.AudioResource;
					m_AudioSource.loop = m_RepeatPattern.IsPatternLoop;
					SetupAudioSource();
				}
			}
		}

		/// <summary>
		/// Object used by <see cref="AudioPlayerAsset"/> filters as context.
		/// Works great with <see cref="Conductors.DictionaryContext"/>, but you can have your custom implementation of <see cref="Conductors.IValuesContainer"/>.
		/// </summary>
		public object ConductorsFilterContext;

		/// <summary>
		/// Used by conductors to persist state per player between usages. For example: don't repeat last clip.
		/// Try to use unique key names.
		/// </summary>
		public Dictionary<string, object> ConductorsStateStorage = new Dictionary<string, object>();

		/// <summary>
		/// Output mixer to use. Will be overriden by <see cref="AudioPlayerAsset"/>.
		/// </summary>
		public AudioMixerGroup Output {
			get => m_Output;
			set {
				m_Output = value;
				if (m_AudioSource) m_AudioSource.outputAudioMixerGroup = EffectiveOutput;
			}
		}

		/// <summary>
		/// Since <see cref="AudioPlayerAsset"/> overrides the output mixer, use this to get the actually used output mixer.
		/// </summary>
		public AudioMixerGroup EffectiveOutput {
			get {
				var asset = AudioReference.AudioAsset;

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
		}

		/// <summary>
		/// Template prefab to use. Will be overriden by <see cref="AudioPlayerAsset"/>.
		/// </summary>
		public AudioSource Template {
			get => m_Template;
			set {
				m_Template = value;
				if (m_AudioSource) {
					SetupAudioSource();
				}
			}
		}

		/// <summary>
		/// Since <see cref="AudioPlayerAsset"/> overrides the template, use this to get the actually used template.
		/// </summary>
		public AudioSource EffectiveTemplate => AudioReference.AudioAsset == null ? m_Template : AudioReference.AudioAsset.Template;

		public bool Mute {
			get => m_Mute;
			set {
				m_Mute = value;
				if (m_AudioSource) m_AudioSource.mute = value;
			}
		}

		public bool PlayOnEnable {
			get => m_PlayOnEnable;
			set {
				m_PlayOnEnable = value;
				// Handled by us.
			}
		}

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
		public RepeatOptions RepeatPattern {
			get => m_RepeatPattern;
			set {
				m_RepeatPattern = value;

				if (m_AudioSource && AudioReference.AudioAsset == null) m_AudioSource.loop = value.Pattern == RepeatPatternType.Loop;

				if (value.Pattern == RepeatPatternType.RepeatInterval) {
					m_NextPlayTime = Time.time;
					m_LastIsPlayingForRepeatInterval = m_ConductorCoroutine != null || (m_AudioSource?.isPlaying ?? false);
				}
			}
		}

		/// <summary>
		/// Since <see cref="AudioPlayerAsset"/> overrides the repeat pattern, use this to get the actually used repeat pattern.
		/// </summary>
		public RepeatOptions EffectiveRepeatPattern => AudioReference.AudioAsset == null ? m_RepeatPattern : AudioReference.AudioAsset.RepeatPattern;

		public float Volume {
			get => m_Volume;
			set {
				m_Volume = value;
				if (m_AudioSource) m_AudioSource.volume = value;
			}
		}

		public float Pitch => AudioSource?.pitch ?? 0f;

		public float SpatialBlend => AudioSource?.spatialBlend ?? 0f;

		public float LastPlayTime { get; private set; }

		public AudioSource AudioSource {
			get {
				if (m_AudioSource == null) {
					SetupAudioSource();
				}

				return m_AudioSource;
			}
		}

		public static IReadOnlyList<AudioSourcePlayer> ActivePlayersRegister => m_ActivePlayersRegister.AsReadOnly();

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

		private AudioSource m_AudioSource;
		private AudioSource m_AppliedTemplate;

		private Coroutine m_VolumeCoroutine;
		private Coroutine m_ConductorCoroutine;

		private float m_NextPlayTime;
		private bool m_LastIsPlayingForRepeatInterval;
		private bool m_ShouldPlayRepeating;

		public static AudioSourcePlayer Quick2DPlayer;
		public static AudioSourcePlayer Quick3DPlayer;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ClearStaticsCache()
		{
			Quick2DPlayer = null;
			Quick3DPlayer = null;
		}

		protected virtual void OnEnable()
		{
			m_ActivePlayersRegister.Add(this);

			if (m_AudioSource) {
				m_AudioSource.enabled = true;

				// Restore in case it was changed by audio asset and coroutine was stopped from OnDisable().
				m_AudioSource.outputAudioMixerGroup = EffectiveOutput;
			}

			if (PlayOnEnable && AudioReference.HasValidReference) {
				Play();
			}
		}

		protected virtual void OnDisable()
		{
			m_ActivePlayersRegister.Remove(this);

			if (m_AudioSource) {
				m_AudioSource.enabled = false;
			}

			m_ConductorCoroutine = null;
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

			if (Application.isPlaying && m_AudioSource) {
				if (m_AudioSource.mute != m_Mute) {
					m_AudioSource.mute = m_Mute;
				}
				if (m_AudioSource.loop != (EffectiveRepeatPattern.Pattern == RepeatPatternType.Loop)) {
					m_AudioSource.loop = EffectiveRepeatPattern.Pattern == RepeatPatternType.Loop;
				}
				if (m_AudioSource.volume != m_Volume) {
					m_AudioSource.volume = m_Volume;
				}
			}
#endif
		}

		public bool IsPlaying => m_AudioSource && (m_AudioSource.isPlaying || (m_ShouldPlayRepeating && m_RepeatPattern.IsPatternInterval && m_AudioReference.AudioAsset == null) || m_ConductorCoroutine != null);
		public bool IsPaused { get; private set; }

		// IsPlaying is false when paused.
		public bool IsPlayingOrPaused => IsPaused || IsPlaying;

		[ContextMenu("Play")]
		public virtual void Play()
		{
			PlayImpl(0f);
		}

		public virtual void PlayDelayed(float delaySeconds)
		{
			PlayImpl(delaySeconds);
		}

		/// <summary>
		/// Can be used for animation event to play given <see cref="AudioPlayerAsset"/>.
		/// This way, the <see cref="Editor.AudioSourcePlayerMonitorWindow"/> will show the correct sound.
		/// </summary>
		public virtual void PlayAudioAsset(AudioPlayerAsset asset)
		{
			if (AudioSource == null)
				return;

			AudioReference = new AudioReferenceProperty(asset);
			Play();
		}

		/// <summary>
		/// Can be used for animation event to play given <see cref="AudioReferenceProperty"/>.
		/// This way, the <see cref="Editor.AudioSourcePlayerMonitorWindow"/> will show the correct sound.
		/// </summary>
		public virtual void PlayAudioReference(AudioReferenceProperty audioReference)
		{
			if (AudioSource == null)
				return;

			audioReference.OnValidate(this);

			// Assign and play normally so it shows up in the Audio Monitor.
			AudioReference = audioReference;
			Play();
		}

		private void PlayImpl(float delay)
		{
			m_ShouldPlayRepeating = true;
			IsPaused = false;

			StopVolumeCrt();
			StopConductorCrt();

			if (m_AudioReference.AudioAsset != null) {
				m_ConductorCoroutine = StartCoroutine(StartAudioAsset(delay));
				PlayStarted?.Invoke(this);

			} else {

				SetupAudioSource();

				if (delay <= 0f) {
					AudioSource.Play();
				} else {
					AudioSource.PlayDelayed(delay);
				}

				LastPlayTime = Time.time;
				PlayStarted?.Invoke(this);
			}
		}

		public virtual void PlayOneShot(AudioClip clip, float volume = 1.0f)
		{
			StopVolumeCrt();
			StopConductorCrt();

			PlayDirectClip(clip, playAsOneShot: true, volume);

			PlayStarted?.Invoke(this);    // So it shows up on the audio monitor.
		}

		public virtual void PlayOnGamepad(int playerIndex)
		{
#if UNITY_EDITOR
			m_ShouldPlayRepeating = true;
			IsPaused = false;

			StopVolumeCrt();
			StopConductorCrt();

			AudioSource.PlayOnGamepad(playerIndex); // This is not available for every platform (e.g. PC doesn't have it).

			LastPlayTime = Time.time;
			PlayStarted?.Invoke(this);
#endif
		}

		[ContextMenu("Stop")]
		public virtual void Stop()
		{
			Stop(InterruptionFadeDuration);
		}

		public virtual void Stop(float interruptionFadeDuration)
		{
			// Prevent multiple calls as it will reset the coroutine every time.
			if (!m_ShouldPlayRepeating)
				return;

			m_ShouldPlayRepeating = false;
			IsPaused = false;

			if (interruptionFadeDuration > 0f) {
				// Just kill the coroutine and resume from where it left off.
				if (m_VolumeCoroutine != null) {
					StopCoroutine(m_VolumeCoroutine);
				}
				m_VolumeCoroutine = StartCoroutine(FadeVolumeCrt(interruptionFadeDuration, false, AudioSource.Stop));
			} else {
				StopVolumeCrt();
				StopConductorCrt();

				AudioSource.Stop();
			}

			PlayStopped?.Invoke(this);
		}

		[ContextMenu("Pause")]
		public virtual void Pause()
		{
			// Prevent multiple calls as it will reset the coroutine every time.
			if (!m_ShouldPlayRepeating)
				return;

			m_ShouldPlayRepeating = false;
			IsPaused = true;

			if (InterruptionFadeDuration > 0f) {
				// Just kill the coroutine and resume from where it left off.
				if (m_VolumeCoroutine != null) {
					StopCoroutine(m_VolumeCoroutine);
				}
				m_VolumeCoroutine = StartCoroutine(FadeVolumeCrt(InterruptionFadeDuration, false, AudioSource.Pause));

			} else {
				StopVolumeCrt();
				// Audio assets keep going and wait for the player to get unpaused.

				AudioSource.Pause();
			}

			PlayPaused?.Invoke(this);
		}

		[ContextMenu("UnPause")]
		public virtual void UnPause()
		{
			// Prevent multiple calls as it will reset the coroutine every time.
			if (m_ShouldPlayRepeating)
				return;

			m_ShouldPlayRepeating = true;
			IsPaused = false;

			if (InterruptionFadeDuration > 0f) {
				// Just kill the coroutine and resume from where it left off.
				if (m_VolumeCoroutine != null) {
					StopCoroutine(m_VolumeCoroutine);
				}
				m_VolumeCoroutine = StartCoroutine(FadeVolumeCrt(InterruptionFadeDuration, true));
				AudioSource.UnPause();

			} else {
				StopVolumeCrt();
				// Audio assets keep updating while paused.

				AudioSource.UnPause();
			}

			PlayUnpaused?.Invoke(this);
		}

		/// <summary>
		/// Destroy the component + audio source OR the whole game object.
		/// If <see cref="InterruptionFadeDuration"/> is non-zero value, will fade the sound first, then destroy it.
		/// </summary>
		public virtual void DestroyPlayer(bool destroyGameObject = false)
		{
			m_ShouldPlayRepeating = false;

			System.Action destroyAction = () => {
				if (destroyGameObject) {
					GameObject.Destroy(gameObject);
				} else {
					if (m_AudioSource) {
						GameObject.Destroy(m_AudioSource);
					}
					GameObject.Destroy(this);
				}
			};

			if (InterruptionFadeDuration > 0f) {
				// Just kill the coroutine and resume from where it left off.
				if (m_VolumeCoroutine != null) {
					StopCoroutine(m_VolumeCoroutine);
				}
				m_VolumeCoroutine = StartCoroutine(FadeVolumeCrt(InterruptionFadeDuration, false, destroyAction));

				PlayStopped?.Invoke(this);

			} else {
				StopVolumeCrt();
				StopConductorCrt();

				AudioSource.Stop();

				PlayStopped?.Invoke(this);
				destroyAction();
			}
		}

		#region Quick Play Static Helpers

		/// <summary>
		/// Play clip quickly as 2D sound from code.
		/// If you want to control the player, set the player yourself.
		/// </summary>
		public static void Play2DClip(AudioClip clip, float volume = 1.0f)
		{
			if (Quick2DPlayer == null) {
				Quick2DPlayer = new GameObject("2D Audio Player").AddComponent<AudioSourcePlayer>();
				Quick2DPlayer.PlayOnEnable = false;
				Quick2DPlayer.AudioSource.spatialBlend = 0;
			}

			Quick2DPlayer.PlayDirectClip(clip, playAsOneShot: true, volume);

			PlayStarted?.Invoke(Quick2DPlayer); // So it shows up on the audio monitor.
		}

		/// <summary>
		/// Play clip quickly as 2D sound from code.
		/// If you want to control the player, set the player yourself.
		/// </summary>
		public static void Play2DClip(AudioPlayerAsset asset)
		{
			if (Quick2DPlayer == null) {
				Quick2DPlayer = new GameObject("2D Audio Player").AddComponent<AudioSourcePlayer>();
				Quick2DPlayer.PlayOnEnable = false;
				Quick2DPlayer.AudioSource.spatialBlend = 0;
			}

			Quick2DPlayer.AudioReference = new AudioReferenceProperty(asset);
			Quick2DPlayer.Play();
		}

		/// <summary>
		/// Play clip quickly as 2D sound from code on specified object (will automatically create player on it).
		/// If you want to control the player, set the player yourself.
		/// </summary>
		public static void Play2DClip(AudioClip clip, GameObject gameObject, float volume = 1.0f)
		{
			AudioSourcePlayer player = gameObject.GetComponent<AudioSourcePlayer>();

			if (player == null) {
				player = gameObject.AddComponent<AudioSourcePlayer>();
				player.PlayOnEnable = false;
				player.AudioSource.spatialBlend = 0;
			}

			player.PlayDirectClip(clip, playAsOneShot: true, volume);

			PlayStarted?.Invoke(player);    // So it shows up on the audio monitor.
		}

		/// <summary>
		/// Play asset quickly as 2D sound from code on specified object (will automatically create player on it).
		/// If you want to control the player, set the player yourself.
		/// </summary>
		public static void Play2DClip(AudioPlayerAsset asset, GameObject gameObject)
		{
			AudioSourcePlayer player = gameObject.GetComponent<AudioSourcePlayer>();

			if (player == null) {
				player = gameObject.AddComponent<AudioSourcePlayer>();
				player.PlayOnEnable = false;
				player.AudioSource.spatialBlend = 0;
			}

			player.AudioReference = new AudioReferenceProperty(asset);
			player.Play();
		}

		/// <summary>
		/// Play clip quickly as 3D sound from code.
		/// If you want to control the player, set the player yourself.
		/// </summary>
		public static void Play3DClip(AudioClip clip, Vector3 position, float volume = 1.0f)
		{
			if (Quick3DPlayer == null) {
				Quick3DPlayer = new GameObject("3D Audio Player").AddComponent<AudioSourcePlayer>();
				Quick3DPlayer.PlayOnEnable = false;
				Quick3DPlayer.AudioSource.spatialBlend = 1;
			}

			Quick3DPlayer.transform.position = position;
			Quick3DPlayer.PlayDirectClip(clip, playAsOneShot: true, volume);

			PlayStarted?.Invoke(Quick3DPlayer); // So it shows up on the audio monitor.
		}

		/// <summary>
		/// Play clip quickly as 3D sound from code.
		/// If you want to control the player, set the player yourself.
		/// </summary>
		public static void Play3DClip(AudioPlayerAsset asset, Vector3 position)
		{
			if (Quick3DPlayer == null) {
				Quick3DPlayer = new GameObject("3D Audio Player").AddComponent<AudioSourcePlayer>();
				Quick3DPlayer.PlayOnEnable = false;
				Quick3DPlayer.AudioSource.spatialBlend = 1;
			}

			Quick3DPlayer.transform.position = position;
			Quick3DPlayer.AudioReference = new AudioReferenceProperty(asset);
			Quick3DPlayer.Play();
		}

		/// <summary>
		/// Play clip quickly as 3D sound from code on specified object (will automatically create player on it).
		/// If you want to control the player, set the player yourself.
		/// </summary>
		public static void Play3DClip(AudioClip clip, Vector3 position, GameObject gameObject, float volume = 1.0f)
		{
			AudioSourcePlayer player = gameObject.GetComponent<AudioSourcePlayer>();

			if (player == null) {
				player = gameObject.AddComponent<AudioSourcePlayer>();
				player.PlayOnEnable = false;
				player.AudioSource.spatialBlend = 1;
			}

			player.transform.position = position;
			player.PlayDirectClip(clip, playAsOneShot: true, volume);

			PlayStarted?.Invoke(player);    // So it shows up on the audio monitor.
		}

		/// <summary>
		/// Play clip quickly as 3D sound from code on specified object (will automatically create player on it).
		/// If you want to control the player, set the player yourself.
		/// </summary>
		public static void Play3DClip(AudioPlayerAsset asset, Vector3 position, GameObject gameObject)
		{
			AudioSourcePlayer player = gameObject.GetComponent<AudioSourcePlayer>();

			if (player == null) {
				player = gameObject.AddComponent<AudioSourcePlayer>();
				player.PlayOnEnable = false;
				player.AudioSource.spatialBlend = 1;
			}

			player.transform.position = position;
			player.AudioReference = new AudioReferenceProperty(asset);
			player.Play();
		}

		#endregion

		#region Direct Play for Conductors

		/// <summary>
		/// Used by <see cref="AudioPlayerAsset.AudioConductor"/> to play sound without changing this component settings.
		/// This way, the <see cref="Editor.AudioSourcePlayerMonitorWindow"/> will show the correct sound.
		/// </summary>
		public virtual void PlayDirectClip(AudioClip clip, bool playAsOneShot, float volume = 1.0f, float pitch = 1.0f)
		{
			if (AudioSource == null)
				return;
			if (clip == null)
				throw new ArgumentNullException();

			// This bypasses the AudioResource property.
			AudioSource.clip = clip;
			AudioSource.pitch = pitch;

			if (playAsOneShot) {
				AudioSource.PlayOneShot(clip, volume * m_Volume);
			} else {
				if (m_VolumeCoroutine == null) {
					AudioSource.volume = volume * m_Volume;
				}
				AudioSource.Play();
			}

			LastPlayTime = Time.time;
		}

		/// <summary>
		/// Used by <see cref="AudioPlayerAsset.AudioConductor"/> to play sound without changing this component settings.
		/// This way, the <see cref="Editor.AudioSourcePlayerMonitorWindow"/> will show the correct sound.
		/// </summary>
		public virtual void PlayDirectClip(ClipWithVolume clipPair, bool playAsOneShot, float pitch = 1.0f, int volumeOffsetDB = 0)
		{
			if (AudioSource == null)
				return;
			if (clipPair.Clip == null)
				throw new ArgumentNullException();

			// This bypasses the AudioResource property.
			AudioSource.clip = clipPair.Clip;
			AudioSource.pitch = pitch;

			// Rolls the volume randomization range (if used), so call it only once.
			float volume = clipPair.GetPlayVolume(volumeOffsetDB);

			if (playAsOneShot) {
				AudioSource.PlayOneShot(clipPair.Clip, volume * m_Volume);
			} else {
				if (m_VolumeCoroutine == null) {
					AudioSource.volume = volume * m_Volume;
				}
				AudioSource.Play();
			}

			LastPlayTime = Time.time;
		}

		/// <summary>
		/// Used by <see cref="AudioPlayerAsset.AudioConductor"/> to play sound without changing this component settings.
		/// This way, the <see cref="Editor.AudioSourcePlayerMonitorWindow"/> will show the correct sound.
		/// </summary>
		public virtual void PlayDirectClip(ClipWithVolumePitch clipPair, bool playAsOneShot, int volumeOffsetDB = 0, int pitchOffsetCents = 0)
		{
			if (AudioSource == null)
				return;
			if (clipPair.Clip == null)
				throw new ArgumentNullException();

			// This bypasses the AudioResource property.
			AudioSource.clip = clipPair.Clip;

			// Pitch range has priority over the pitches list.
			// No variation and no offset means pitch of 0 cents, i.e. 1f - resets the pitch in case it was changed by the last user.
			AudioSource.pitch = Mathf.Pow(AudioPlayerAsset.CentPitchSize, clipPair.GetRandomPitch(pitchOffsetCents));

			// Rolls the volume randomization range (if used), so call it only once.
			float volume = clipPair.GetPlayVolume(volumeOffsetDB);

			if (playAsOneShot) {
				AudioSource.PlayOneShot(clipPair.Clip, volume * m_Volume);
			} else {
				if (m_VolumeCoroutine == null) {
					AudioSource.volume = volume * m_Volume;
				}
				AudioSource.Play();
			}

			LastPlayTime = Time.time;
		}

		/// <summary>
		/// Used by <see cref="AudioPlayerAsset.AudioConductor"/> to play sound without changing this component settings.
		/// This way, the <see cref="Editor.AudioSourcePlayerMonitorWindow"/> will show the correct sound.
		/// </summary>
		public virtual void PlayDirectResource(AudioResource resource, float pitch = 1.0f)
		{
			if (AudioSource == null)
				return;
			if (resource == null)
				throw new ArgumentNullException();

			// This bypasses the AudioResource property.
			AudioSource.resource = resource;
			AudioSource.pitch = pitch;

			AudioSource.Play();

			LastPlayTime = Time.time;
		}

		/// <summary>
		/// Used by <see cref="AudioPlayerAsset.AudioConductor"/> to play sound without changing this component settings.
		/// This way, the <see cref="Editor.AudioSourcePlayerMonitorWindow"/> will show the correct sound.
		/// </summary>
		public virtual void PlayDirectResource(ResourceWithVolume resourcePair, float pitch = 1.0f, int volumeOffsetDB = 0)
		{
			if (AudioSource == null)
				return;
			if (resourcePair.Resource == null)
				throw new ArgumentNullException();

			// This bypasses the AudioResource property.
			AudioSource.resource = resourcePair.Resource;
			AudioSource.pitch = pitch;

			if (m_VolumeCoroutine == null) {
				// Rolls the volume randomization range (if used).
				AudioSource.volume = resourcePair.GetPlayVolume(volumeOffsetDB) * m_Volume;
			}
			AudioSource.Play();

			LastPlayTime = Time.time;
		}

		#endregion

		private IEnumerator StartAudioAsset(float delay)
		{
			delay += m_AudioReference.AudioAsset.Delay;

			if (delay > 0f) {
				float waitTime = 0f;
				while (waitTime < delay) {
					yield return null;

					if (!IsPaused) {
						waitTime += Time.deltaTime;
					}
				}
			}

			// Always overriden by the audio asset.
			AudioSource.outputAudioMixerGroup = EffectiveOutput;

			yield return m_AudioReference.AudioAsset.Play(this, ConductorsFilterContext);

			// Coroutine returns early, sound may still be playing - don't touch the mixer.
			if (!m_AudioSource.isPlaying) {
				AudioSource.outputAudioMixerGroup = EffectiveOutput;
				m_AudioSource.loop = EffectiveRepeatPattern.IsPatternLoop;
			}

			// Signal that conductor finished playing (which doesn't mean the audio finished).
			m_ConductorCoroutine = null;
		}

		private void StopVolumeCrt()
		{
			if (m_VolumeCoroutine != null) {
				AudioSource.volume = Volume;

				StopCoroutine(m_VolumeCoroutine);
				m_VolumeCoroutine = null;
			}
		}

		private void StopConductorCrt()
		{
			// Restore the output if we changed it. Even if the coroutine stopped playing long ago.
			if (m_AudioReference.AudioAsset && m_AudioSource) {
				m_AudioSource.outputAudioMixerGroup = EffectiveOutput;
				m_AudioSource.loop = m_RepeatPattern.IsPatternLoop;
			}

			if (m_ConductorCoroutine != null) {

				StopCoroutine(m_ConductorCoroutine);
				m_ConductorCoroutine = null;
			}
		}

		private IEnumerator FadeVolumeCrt(float fadeSeconds, bool fadeIn, System.Action callbackOnFinish = null)
		{
			float startTime = Time.time;
			float startVolume = fadeIn ? 0f : Volume;
			float endVolume = fadeIn ? Volume : 0f;

			if (m_VolumeCoroutine != null) {
				startVolume = AudioSource.volume; // Resume from where it left off.

				// Reduce the fade time to what is left.
				fadeSeconds *= Mathf.Abs(endVolume - AudioSource.volume) / Volume;
			}


			while (Time.time - startTime < fadeSeconds) {
				AudioSource.volume = Mathf.Lerp(startVolume, endVolume, (Time.time - startTime) / fadeSeconds);
				yield return null;
			}

			StopVolumeCrt();
			if (!fadeIn && !IsPaused) {
				StopConductorCrt();
			}

			callbackOnFinish?.Invoke();
		}

		protected virtual void Update()
		{
			// Check for repeating interval when playing normal audio clip, but skip when using audio asset - it handles repeating on it's own.
			if (m_ShouldPlayRepeating && m_ConductorCoroutine == null && m_RepeatPattern.IsPatternInterval && AudioReference.AudioAsset == null && m_AudioSource) {

				if (m_AudioSource.isPlaying != m_LastIsPlayingForRepeatInterval) {
					if (!m_AudioSource.isPlaying) {
						m_NextPlayTime = Time.time + m_RepeatPattern.NextIntervalValue();
					}
					m_LastIsPlayingForRepeatInterval = m_AudioSource.isPlaying;
				}

				if (!m_AudioSource.isPlaying && Time.time >= m_NextPlayTime) {
					Play();
				}

			} else if (!m_ShouldPlayRepeating) {
				m_LastIsPlayingForRepeatInterval = false;
			}
		}

		private void SetupAudioSource()
		{
			if (m_AudioSource == null) {

				m_AudioSource = gameObject.AddComponent<AudioSource>();

				m_AudioSource.playOnAwake = false; // Will be handled by us.
				m_AudioSource.resource = m_AudioReference.AudioResource;
				m_AudioSource.loop = m_RepeatPattern.IsPatternLoop;
				m_AudioSource.volume = m_Volume;
				m_AudioSource.mute = m_Mute;
			}

			m_AudioSource.outputAudioMixerGroup = EffectiveOutput;

			if (EffectiveTemplate != m_AppliedTemplate) {
				m_AppliedTemplate = EffectiveTemplate;

				if (EffectiveTemplate) {
					CopyAudioSourceDetails(m_AudioSource, m_AppliedTemplate);
				}
			}
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
			destination.spatialBlend = source.spatialBlend;
			destination.spatializePostEffects = source.spatializePostEffects;
			destination.reverbZoneMix = source.reverbZoneMix;

			destination.dopplerLevel = source.dopplerLevel;
			destination.minDistance = source.minDistance;
			destination.maxDistance = source.maxDistance;
			destination.SetCustomCurve(AudioSourceCurveType.CustomRolloff, source.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
			destination.SetCustomCurve(AudioSourceCurveType.SpatialBlend, source.GetCustomCurve(AudioSourceCurveType.SpatialBlend));
			destination.SetCustomCurve(AudioSourceCurveType.ReverbZoneMix, source.GetCustomCurve(AudioSourceCurveType.ReverbZoneMix));
			destination.SetCustomCurve(AudioSourceCurveType.Spread, source.GetCustomCurve(AudioSourceCurveType.Spread));
			destination.rolloffMode = source.rolloffMode; // Because changing the curve changes this property to custom.
		}
	}
}
