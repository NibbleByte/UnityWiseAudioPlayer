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
	[AddComponentMenu("Audio/Audio Player")]
	public class AudioPlayer : MonoBehaviour
	{
		public enum AudioSourcesPoolMode
		{
			PlayerObjectPool = 0,
			PlayerChildPool = 2,
			GlobalPool = 4,
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
				set { m_AudioAsset = value; m_AudioResource = null; }
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

		public delegate void PlaybackEventHandler(AudioPlayback playback);
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
		/// NOTE: Don't use from conductors!!! Use AudioPlayback.Play methods instead.
		/// </summary>
		public AudioReferenceProperty AudioReference {
			get => m_AudioReference;
			set {
				value.OnValidate(this);

				m_AudioReference = value;
			}
		}

		/// <summary>
		/// Output mixer to use. Will be overriden by <see cref="AudioPlayerAsset"/>.
		/// </summary>
		public AudioMixerGroup OutputMixer { get => m_OutputMixer; set => m_OutputMixer = value; }

		/// <summary>
		/// Template prefab to use. Will be overriden by <see cref="AudioPlayerAsset"/>.
		/// </summary>
		public AudioSource Template { get => m_Template; set => m_Template = value; }

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
			get => Repeat.IsRepeating;
			set {
				var repeatSettings = Repeat;
				repeatSettings.Mode = value ? AudioPlaybackSettings.RepeatMode.Loop : AudioPlaybackSettings.RepeatMode.Once;
				Repeat = repeatSettings;
			}
		}

		/// <summary>
		/// Repeat settings to use. Will be overriden by <see cref="AudioPlayerAsset"/>.
		/// </summary>
		public AudioPlaybackSettings.RepeatSettings Repeat { get => m_Repeat; set => m_Repeat = value; }

		/// <summary>
		/// Volume used when playing new sounds - will not affect already playing sounds.
		/// </summary>
		public float Volume { get => m_Volume; set => m_Volume = value; }

		/// <summary>
		/// Object used by <see cref="AudioCondition"/> as context.
		/// Works great with <see cref="Conductors.DictionaryContext"/>, but you can have your custom implementation of <see cref="Conductors.IValuesContainer"/>.
		/// </summary>
		public object ConductorsConditionContext;

		#region Conductor State Helpers

		/// <summary>
		/// Used by conductors to persist state per player between usages. For example: don't repeat last clip.
		/// Try to use unique key names.
		/// </summary>
		private Dictionary<AudioPlayback.ConductorStateKey, object> m_ConductorsStateStorage = new Dictionary<AudioPlayback.ConductorStateKey, object>();

		/// <summary>
		/// Get stored conductor state value (per player).
		/// Conductors should use <see cref="AudioPlayback.GetConductorStateValue{T}"/> instead.
		/// </summary>
		public T GetConductorStateValue<T>(AudioConductor conductor, UnityEngine.Object asset, string keyName, T defaultValue)
		{
			if (m_ConductorsStateStorage.TryGetValue(new AudioPlayback.ConductorStateKey(conductor, asset, keyName), out object objValue))
				return (T) objValue;

			return defaultValue;
		}

		/// <summary>
		/// Set conductor state value (per player).
		/// Conductors should use <see cref="AudioPlayback.SetConductorStateValue"/> instead.
		/// </summary>
		public void SetConductorStateValue(AudioConductor conductor, UnityEngine.Object asset, string keyName, object value)
		{
			m_ConductorsStateStorage[new AudioPlayback.ConductorStateKey(conductor, asset, keyName)] = value;
		}

		/// <summary>
		/// Set all conductor state values of given type that are saved on this player.
		/// </summary>
		public void SetConductorStateValueForType(Type conductorType, UnityEngine.Object asset, string keyName, object value)
		{
			string assetName = asset ? asset.name : null;
			ulong assetId = asset ? AudioPlayback.ConductorStateKey.GetId(asset) : 0;

			var matchedKeys = new List<AudioPlayback.ConductorStateKey>();

			foreach (var conductorKey in m_ConductorsStateStorage.Keys) {
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
				m_ConductorsStateStorage[key] = value;
			}
		}

		#endregion

		/// <summary>
		/// Index of the gamepad output to use. -1 means normal speakers output instead of gamepad. Valid slots are 0-3.
		/// This is used only for platforms that support gamepad audio output (like Playstation).
		/// For more info check the <see cref="AudioSource.PlayOnGamepad(int)"/> and <see cref="AudioSource.DisableGamepadOutput()"/>
		/// </summary>
		[NonSerialized]
		public int GamepadOutputIndex = -1;

		public float LastPlayTimeUnscaled { get; private set; }

		public IReadOnlyList<AudioPlayback> ActivePlaybacks => m_ActivePlaybacks.AsReadOnly();

		public static IReadOnlyList<AudioPlayer> ActivePlayers => m_ActivePlayers.AsReadOnly();

		[SerializeField]
		[Tooltip("Each sound that overlaps an already playing one gets its own AudioSource. Sources are reused once they finish.\n\n" +
			"Where to create these AudioSources:\n" +
			"- " + nameof(AudioSourcesPoolMode.PlayerObjectPool) + ": on this object\n" +
			"- " + nameof(AudioSourcesPoolMode.PlayerChildPool) + ": on a child shared object\n" +
			"- " + nameof(AudioSourcesPoolMode.GlobalPool) + ": on a global pool object per source\n\n" +
			"Sounds on this object or a child stop as soon as it's destroyed. Use the global pool to let them finish (looping sounds are still stopped).\n" +
			"If your template prefab has audio filters attached you must use " + nameof(AudioSourcesPoolMode.GlobalPool) + " as it offers one object per source.")]
		private AudioSourcesPoolMode m_SourcesPoolMode;

		[SerializeField]
		[Tooltip("Audio reference to play - standard Unity audio asset or customizable Audio Player Asset.")]
		private AudioReferenceProperty m_AudioReference;


		[SerializeField]
		[Tooltip("Mixer group to output to.\n\nOverridden by the audio asset's mixer. If empty, uses the template's mixer.")]
		private AudioMixerGroup m_OutputMixer;

		[SerializeField]
		[Tooltip("AudioSource (prefab or scene object) whose settings are copied to the playing source. Overrides the player's template.\nYou can have audio filter components attached too, but the player must use " + nameof(AudioSourcesPoolMode.GlobalPool) + " as pool mode!")]
		private AudioSource m_Template;

		[SerializeField]
		[Tooltip("Mute all sounds from this player.")]
		private bool m_Mute = false;

		[SerializeField]
		[Tooltip("Play automatically every time this component is enabled?")]
		private bool m_PlayOnEnable = true;

		[SerializeField]
		[Tooltip("How the sound repeats. Loop With Interval adds seconds of silence after each play.\n\nOverridden when an audio asset is played.")]
		private AudioPlaybackSettings.RepeatSettings m_Repeat;

		[Tooltip("Fade duration in seconds when sounds are stopped, paused or unpaused. 0 means instant.")]
		public float InterruptionFadeDuration = 0.2f;

		[Range(0f, 1f)]
		[SerializeField]
		[Tooltip("Volume for newly started sounds. Doesn't affect sounds already playing.")]
		private float m_Volume = 1f;

		private static readonly List<AudioPlayer> m_ActivePlayers = new List<AudioPlayer>();

		private List<AudioPlayback> m_ActivePlaybacks = new List<AudioPlayback>();
		private Queue<AudioSource> m_AudioSourcesPool = new Queue<AudioSource>();
		private Dictionary<AudioSource, AudioSource> m_LastTemplatesUsed = new Dictionary<AudioSource, AudioSource>();  // Only for m_AudioSourcesPool sources.
		private static readonly Dictionary<Type, bool> s_IsCustomFilterType = new Dictionary<Type, bool>();
		private const string ChildPoolContainerName = "__AudioPlayerPool__";

		// Can't have global 3D object and reuse it as moving it will affect all the currently played sounds as well.
		private static AudioPlayer s_Quick2DPlayer;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ClearStaticsCache()
		{
			s_Quick2DPlayer = null;
		}

		protected virtual void OnEnable()
		{
			m_ActivePlayers.Add(this);

			if (PlayOnEnable && AudioReference.HasValidReference) {
				Play();
			}
		}

		protected virtual void OnDisable()
		{
			// Also called on destroy, which works fine for us.

			m_ActivePlayers.Remove(this);

			while(m_ActivePlaybacks.Count > 0) {
				// If we are disabled because we are being destroyed, keep playing the source
				// in case it is in the global pool, allowing it to finish.
				// If source is child of this object it will stop automatically anyway.
				// If source is looping - always stop, or it may play forever if only player is disabled.
				var playback = m_ActivePlaybacks.Last();
				ReleaseAudioPlayback(playback, stopSource: playback.AudioSource && playback.AudioSource.loop);
			}

			m_LastTemplatesUsed.Clear();
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

			m_Repeat.OnValidate(this);

			if (Application.isPlaying) {
				foreach(var playback in m_ActivePlaybacks) {
					if (playback.AudioSource) {
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
		public IEnumerator WaitUntilFinished()
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


		public AudioPlayback PlayAudioReference(AudioReferenceProperty audioReference, float delay = 0f)
		{
			audioReference.OnValidate(this);

			return PlayImpl(audioReference, delay);
		}

		/// <summary>
		/// Play a conductor directly. Providing asset is preferable but optional.
		/// Useful if you keep conductors in your own assets and pick the conductor yourself (no conditions are checked).
		/// Uses only the provided <paramref name="settings"/> - player's template, output mixer and repeat settings are NOT used.
		/// </summary>
		public AudioPlayback PlayConductor(AudioPlaybackSettings settings, AudioConductor conductor, UnityEngine.Object conductorAsset)
		{
			if (conductor == null)
				throw new ArgumentNullException(nameof(conductor));

			return PlayConductorImpl(settings, conductor, conductorAsset);
		}

		protected virtual AudioPlayback PlayImpl(AudioReferenceProperty audioReference, float delay)
		{
			if (IsPaused) {
				UnPause();
			}

			var playback = AcquireAudioPlayback();

			// Something went wrong, abort. Probably editor is quitting.
			if (playback == null)
				return null;

			if (audioReference.AudioAsset != null) {
				var asset = audioReference.AudioAsset;
				var settings = asset.Settings;
				settings.Delay += delay;

				var conductorCoroutine = StartCoroutine(StartConductorsPlayback(settings, asset.Conductors, asset, playback));

				// We can get the coroutine after running it initially, but it may already have finished.
				if (!playback.HasConductorFinished) {
					playback.ConductorCoroutine = conductorCoroutine;
				}
			} else {
				StartResourcePlayback(audioReference.AudioResource, delay, playback);
			}

			// If AudioSource is missing playback got released immediately, which means it didn't actually play.
			if (!playback.WasCancelled && playback.AudioSource != null) {
				LastPlayTimeUnscaled = Time.unscaledTime;
				PlaybackStarted?.Invoke(playback);
			}

			return playback;
		}

		protected virtual AudioPlayback PlayConductorImpl(AudioPlaybackSettings settings, AudioConductor conductor, UnityEngine.Object conductorAsset)
		{
			if (IsPaused) {
				UnPause();
			}

			var playback = AcquireAudioPlayback();

			// Something went wrong, abort. Probably editor is quitting.
			if (playback == null)
				return null;

			ConditionalConductor[] conductors = new []{
				new ConditionalConductor(){
					Conductor = conductor,
					Conditions = Array.Empty<AudioCondition>(),
				}
			};

			var conductorCoroutine = StartCoroutine(StartConductorsPlayback(settings, conductors, conductorAsset, playback));

			// We can get the coroutine after running it initially, but it may already have finished.
			if (!playback.HasConductorFinished) {
				playback.ConductorCoroutine = conductorCoroutine;
			}

			// If AudioSource is missing playback got released immediately, which means it didn't actually play.
			if (!playback.WasCancelled && playback.AudioSource != null) {
				LastPlayTimeUnscaled = Time.unscaledTime;
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
						playback.HasConductorFinished = true;

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
		public virtual void Stop(AudioPlayback playback) => Stop(playback, InterruptionFadeDuration);

		/// <summary>
		/// Stop specific playback. If <see cref="interruptionFadeDuration"/> is non-zero value, will fade the sound first, then stop it.
		/// </summary>
		public virtual void Stop(AudioPlayback playback, float interruptionFadeDuration)
		{
			// Not one of ours or it already finished.
			if (!m_ActivePlaybacks.Contains(playback))
				return;

			if (interruptionFadeDuration > 0f && !IsPaused) {

				if (playback.IsPlaying && playback.StopFadeCoroutine == null) {
					playback.StopConductorCoroutine();
					playback.HasConductorFinished = true;

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
		public virtual void StopAllExcept(AudioPlayback keptPlayback)
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
		/// Instantly stop all active playbacks that use conductor coming from given asset.
		/// </summary>
		public virtual void StopAllWithConductorAsset(UnityEngine.Object conductorAsset, AudioPlayback excludePlayback = null)
		{
			for (int i = m_ActivePlaybacks.Count - 1; i >= 0; i--) {
				var playback = m_ActivePlaybacks[i];
				if (playback.ConductorAsset == conductorAsset && excludePlayback != playback) {

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
						playback.HasConductorFinished = true;

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

		public void DestroyWhenFinished(bool destroyGameObject)
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
		public static AudioPlayback Play2DAudio(AudioClip clip, GameObject gameObject = null)
			=> Play2DAudio(new AudioReferenceProperty(clip), gameObject);

		/// <summary>
		/// Play audio quickly as 2D sound from code on specified object (will automatically create player on it).
		/// If no game object is specified a default one will be used.
		/// If you want to control the player, set the player yourself.
		/// </summary>
		public static AudioPlayback Play2DAudio(AudioPlayerAsset asset, GameObject gameObject = null)
			=> Play2DAudio(new AudioReferenceProperty(asset), gameObject);

		/// <summary>
		/// Play audio quickly as 2D sound from code on specified object (will automatically create player on it).
		/// If no game object is specified a default one will be used.
		/// If you want to control the player, set the player yourself.
		/// </summary>
		public static AudioPlayback Play2DAudio(AudioReferenceProperty audioReference, GameObject gameObject = null)
		{
			AudioPlayer player;
			bool setAsDefaultPlayer = false;

			if (gameObject == null) {
				if (s_Quick2DPlayer == null) {
					gameObject = new GameObject("2D Audio Player");
					setAsDefaultPlayer = true;
				}

				player = s_Quick2DPlayer;
			} else {
				player = gameObject.GetComponent<AudioPlayer>();
			}

			if (player == null) {
				player = gameObject.AddComponent<AudioPlayer>();
				player.PlayOnEnable = false;

				// This is not needed as audio sources by default are 2D.
				//var templateSource = player.gameObject.AddComponent<AudioSource>();
				//templateSource.spatialBlend = 0;
				//
				//player.Template = templateSource;

				if (setAsDefaultPlayer) {
					s_Quick2DPlayer = player;
				}
			}

			return player.PlayAudioReference(audioReference);
		}

		/// <summary>
		/// Play audio quickly as 3D sound from code on specified object (will automatically create player on it).
		/// Provide template to specify the spatial blend curve (or leave empty for the defaults).
		/// If you want to control the player, set the player yourself.
		/// </summary>
		public static AudioPlayback Play3DAudio(AudioClip clip, GameObject gameObject, AudioSource template = null)
			=> Play3DAudio(new AudioReferenceProperty(clip), gameObject, template);

		/// <summary>
		/// Play audio quickly as 3D sound from code on specified object (will automatically create player on it).
		/// Provide template to specify the spatial blend curve (or leave empty for the defaults).
		/// If you want to control the player, set the player yourself.
		/// </summary>
		public static AudioPlayback Play3DAudio(AudioPlayerAsset asset, GameObject gameObject, AudioSource template = null)
			=> Play3DAudio(new AudioReferenceProperty(asset), gameObject, template);

		/// <summary>
		/// Play audio reference quickly as 3D sound from code on specified object (will automatically create player on it).
		/// Provide template to specify the spatial blend curve (or leave empty for the defaults).
		/// If you want to control the player, set the player yourself.
		/// </summary>
		public static AudioPlayback Play3DAudio(AudioReferenceProperty audioReference, GameObject gameObject, AudioSource template = null)
		{
			AudioPlayer player = gameObject.GetComponent<AudioPlayer>();

			if (player == null) {
				player = gameObject.AddComponent<AudioPlayer>();
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
		public static AudioPlayback Play3DAudio(AudioClip clip, Vector3 position, AudioSource template = null)
			=> Play3DAudio(new AudioReferenceProperty(clip), position, template);

		/// <summary>
		/// Play audio quickly as 3D sound from code on specified position (will create a temporary player on that position).
		/// Provide template to specify the spatial blend curve (or leave empty for the defaults).
		/// If you want to control the player, set the player yourself.
		/// </summary>
		public static AudioPlayback Play3DAudio(AudioPlayerAsset asset, Vector3 position, AudioSource template = null)
			=> Play3DAudio(new AudioReferenceProperty(asset), position, template);

		/// <summary>
		/// Play audio quickly as 3D sound from code on specified position (will create a temporary player on that position).
		/// Provide template to specify the spatial blend curve (or leave empty for the defaults).
		/// If you want to control the player, set the player yourself.
		/// </summary>
		public static AudioPlayback Play3DAudio(AudioReferenceProperty audioReference, Vector3 position, AudioSource template = null)
		{
			// Can't have global 3D object and reuse it as moving it will affect all the currently played sounds as well.
			AudioPlayer player = new GameObject("One Shot 3D Audio").AddComponent<AudioPlayer>();
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

			player.DestroyWhenFinished(destroyGameObject: true);

			return playback;
		}

		#endregion

		/// <summary>
		/// Plays an asset (selecting a conductor by its conditions) or a standalone conductor with the given settings.
		/// </summary>
		private IEnumerator StartConductorsPlayback(AudioPlaybackSettings settings, ConditionalConductor[] conductors, UnityEngine.Object conductorAsset, AudioPlayback playback)
		{
			playback.IsUsingConductors = true;
			playback.ConductorAsset = conductorAsset;
			playback.AudioSource.outputAudioMixerGroup = settings.GetEffectiveOutputMixer();
			playback.AudioSource.loop = settings.Repeat.IsLoop;
			playback.Repeat = settings.Repeat;
			playback.ConductorStateScope = settings.StateScope;

			playback.Template = settings.Template;
			ApplyTemplate(playback, settings.Template);	// Skips copy if template is the same as last time.

			if (settings.Delay > 0f) {
				yield return WaitUnpausedDelay(settings.Delay);
			}

			AudioSource source = playback.AudioSource;

			switch (settings.InterruptMode) {
				case AudioPlaybackSettings.InterruptSoundsMode.DontInterrupt:
					// Do nothing.
					break;
				case AudioPlaybackSettings.InterruptSoundsMode.InterruptAll:
					StopAllExcept(playback);
					break;
				case AudioPlaybackSettings.InterruptSoundsMode.InterruptSameAsset:
					if (conductorAsset != null) {
						StopAllWithConductorAsset(conductorAsset, playback);
					} else {
						// Same as DontInterrupt.
					}
					break;
				default: throw new NotSupportedException(settings.InterruptMode.ToString());
			}

			bool customLoop;
			do {
				customLoop = false;

				var conductor = conductors.FirstOrDefault(entry => entry.Conditions.All(c => c?.IsAllowed(ConductorsConditionContext, playback) ?? true)).Conductor;
				if (conductor != null) {

					// If conductor has custom logic other than just playing a looped sound,
					// we implement the loop. Normal sounds should still loop via the source itself,
					// which should drop any playback gaps between loops.
					if (settings.Repeat.IsRepeating && !(conductor is Conductors.PlayAudioConductor)) {
						customLoop = true;
						source.loop = false;
					}

					playback.Conductor = conductor;
					yield return conductor.Play(playback);
					playback.Conductor = null;

				} else {

					if (settings.Repeat.IsRepeating) {
						// If no match, keep looping untill we get a match according to the loop pattern.
						yield return null;
						customLoop = true;
						continue;
					} else {
						playback.WasCancelled = true;
						break;
					}
				}

				if (customLoop) {
					do {
						yield return null;
					} while (this && (source.isPlaying || IsPaused));

					if (this == null)
						yield break;
				}

				if (settings.Repeat.IsInterval) {
					float waitTime = settings.Repeat.RollInterval();
					float passedTime = 0.0f;

					while (passedTime <= waitTime && settings.Repeat.IsRepeating) {
						yield return null;

						if (this == null)
							yield break;

						if (!IsPaused && !source.isPlaying) {
							passedTime += Time.unscaledDeltaTime;
						}
					}
				}

			} while (settings.Repeat.IsInterval || customLoop);

			FinishConductorPlayback(playback);
		}

		private IEnumerator WaitUnpausedDelay(float delay)
		{
			float waitTime = 0f;
			while (waitTime < delay) {
				yield return null;

				if (!IsPaused) {
					waitTime += Time.unscaledDeltaTime;
				}
			}
		}

		private void FinishConductorPlayback(AudioPlayback playback)
		{
			// Signal that conductor finished playing (which doesn't mean the audio finished).
			playback.HasConductorFinished = true;
			if (!playback.IsPlaying) {
				ReleaseAudioPlayback(playback);
			}
		}

		private void StartResourcePlayback(AudioResource resource, float delay, AudioPlayback playback)
		{
			playback.IsUsingConductors = false;
			playback.AudioSource.outputAudioMixerGroup = m_OutputMixer ? m_OutputMixer : (m_Template ? m_Template.outputAudioMixerGroup : null);
			playback.AudioSource.loop = m_Repeat.IsLoop;
			playback.Repeat = m_Repeat;
			playback.ConductorStateScope = AudioPlaybackSettings.ConductorsStateScope.PerPlayer;

			playback.Template = m_Template;
			ApplyTemplate(playback, playback.Template); // Skips copy if template is the same as last time.

			playback.AudioSource.resource = resource;

			if (delay <= 0f) {
				playback.AudioSource.Play();
			} else {
				playback.AudioSource.PlayDelayed(delay);
			}

			playback.HasConductorFinished = true; // No conductor for normal audio resource.
		}

		protected virtual void Update()
		{
			if (IsPaused)
				return;

			for (int i = 0; i < m_ActivePlaybacks.Count; i++) {
				AudioPlayback playback = m_ActivePlaybacks[i];

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

				bool isLoopWithIntervalMode = !playback.IsUsingConductors && playback.Repeat.IsInterval;
				if (isLoopWithIntervalMode) {

					if (playback.AudioSource.isPlaying != playback.LastIsPlayingForLoopWithInterval) {
						if (!playback.AudioSource.isPlaying) {
							playback.NextPlayTime = Time.unscaledTime + playback.Repeat.RollInterval();
						}
						playback.LastIsPlayingForLoopWithInterval = playback.AudioSource.isPlaying;
					}

					if (!playback.AudioSource.isPlaying && Time.unscaledTime >= playback.NextPlayTime) {
						playback.AudioSource.Play();
					}
				}
			}
		}

		private AudioPlayback AcquireAudioPlayback()
		{
			AudioPlayback playback;

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

				playback = new AudioPlayback(this, source, m_SourcesPoolMode, Time.unscaledTime);
				m_ActivePlaybacks.Add(playback);

				return playback;
			}

			switch (m_SourcesPoolMode) {
				case AudioSourcesPoolMode.PlayerObjectPool:
					playback = new AudioPlayback(this, gameObject.AddComponent<AudioSource>(), m_SourcesPoolMode, Time.unscaledTime);
					break;

				case AudioSourcesPoolMode.PlayerChildPool:
					var childContainer = transform.Find(ChildPoolContainerName);
					if (childContainer == null) {
						childContainer = new GameObject(ChildPoolContainerName).transform;
						childContainer.SetParent(transform, worldPositionStays: false);
					}

					playback = new AudioPlayback(this, childContainer.gameObject.AddComponent<AudioSource>(), m_SourcesPoolMode, Time.unscaledTime);
					break;

				case AudioSourcesPoolMode.GlobalPool:
					// Editor is quitting, pool is gone.
					if (AudioSourcesGlobalPool.Instance == null)
						return null;

					playback = new AudioPlayback(this, AudioSourcesGlobalPool.Instance.AcquireAudioSource(), m_SourcesPoolMode, Time.unscaledTime);
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

		private void ReleaseAudioPlayback(AudioPlayback playback, bool stopSource = true)
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

		/// <summary>
		/// Remove the used template from the cache for this playback.
		/// Call this when you change the audio source details after template was applied.
		/// </summary>
		public void ForgetUsedTemplate(AudioPlayback playback)
		{
			if (playback.SourcesPoolMode == AudioSourcesPoolMode.GlobalPool) {
				var lastTemplateComponent = playback.AudioSource.GetComponent<LastTemplateUsed>();
				if (lastTemplateComponent) {
					foreach (var addedComponent in lastTemplateComponent.AddedFilterComponents) {
						GameObject.DestroyImmediate(addedComponent);
					}
					GameObject.DestroyImmediate(lastTemplateComponent);
				}

			} else {
				m_LastTemplatesUsed.Remove(playback.AudioSource);
			}
		}

		// Applies template if not already applied (cached).
		// Properties other than pitch and volume won't be set if same template was applied, so don't change them or they will leak.
		private void ApplyTemplate(AudioPlayback playback, AudioSource template)
		{
			var destination = playback.AudioSource;
			bool doCopy = false;
			LastTemplateUsed lastTemplateComponent = null;

			if (playback.SourcesPoolMode == AudioSourcesPoolMode.GlobalPool) {

				if (playback.AudioSource.TryGetComponent(out lastTemplateComponent)) {
					if (lastTemplateComponent.TemplateUsed != template) {
						lastTemplateComponent.TemplateUsed = template;
						doCopy = true;
					}

				} else {
					lastTemplateComponent = playback.AudioSource.gameObject.AddComponent<LastTemplateUsed>();
					lastTemplateComponent.TemplateUsed = template;
					doCopy = true;
				}


			} else {

				if (!m_LastTemplatesUsed.TryGetValue(playback.AudioSource, out AudioSource lastTemplate) || lastTemplate != template) {
					m_LastTemplatesUsed[playback.AudioSource] = template;

					doCopy = true;
				}
			}

			bool isSelectedInEditor = false;
#if UNITY_EDITOR
			isSelectedInEditor = template && (UnityEditor.Selection.activeGameObject == template.gameObject || UnityEditor.Selection.activeObject is AudioPlayerAsset);
#endif

			if (doCopy || isSelectedInEditor) {

				if (template) {
					AudioCopyUtils.CopyAudioSourceDetails(destination, template);

					// Can't have filters on object with multiple audio sources, so only global pool is supporting them, as it uses one-object-per-source.
					if (playback.SourcesPoolMode == AudioSourcesPoolMode.GlobalPool) {

						foreach(var component in lastTemplateComponent.AddedFilterComponents) {
							GameObject.DestroyImmediate(component);
						}
						lastTemplateComponent.AddedFilterComponents.Clear();


						foreach (var component in template.GetComponents<Behaviour>()) {

							if (component is AudioSource)
								continue;

							if (TryCopyAudioFilter<AudioChorusFilter>(destination, component, lastTemplateComponent, AudioCopyUtils.CopyAudioFilter)) continue;
							if (TryCopyAudioFilter<AudioDistortionFilter>(destination, component, lastTemplateComponent, AudioCopyUtils.CopyAudioFilter)) continue;
							if (TryCopyAudioFilter<AudioEchoFilter>(destination, component, lastTemplateComponent, AudioCopyUtils.CopyAudioFilter)) continue;
							if (TryCopyAudioFilter<AudioHighPassFilter>(destination, component, lastTemplateComponent, AudioCopyUtils.CopyAudioFilter)) continue;
							if (TryCopyAudioFilter<AudioLowPassFilter>(destination, component, lastTemplateComponent, AudioCopyUtils.CopyAudioFilter)) continue;
							if (TryCopyAudioFilter<AudioReverbFilter>(destination, component, lastTemplateComponent, AudioCopyUtils.CopyAudioFilter)) continue;

							var componentType = component.GetType();
							if (IsCustomAudioFilter(componentType)) {
								var destinationCustomFilterComponent = (Behaviour) destination.GetComponent(componentType);
								if (destinationCustomFilterComponent == null) {
									destinationCustomFilterComponent = (Behaviour) destination.gameObject.AddComponent(componentType);
								}

								var json = JsonUtility.ToJson(component);
								JsonUtility.FromJsonOverwrite(json, destinationCustomFilterComponent);
								destinationCustomFilterComponent.enabled = component.enabled;

								lastTemplateComponent.AddedFilterComponents.Add(destinationCustomFilterComponent);
							}
						}
					}

				} else {
					// No Template

					AudioCopyUtils.ResetAudioSourceDetails(destination);

					// Can't have filters on object with multiple audio sources, so only global pool is supporting them, as it uses one-object-per-source.
					if (playback.SourcesPoolMode == AudioSourcesPoolMode.GlobalPool) {

						foreach (var component in lastTemplateComponent.AddedFilterComponents) {
							GameObject.DestroyImmediate(component);
						}
						lastTemplateComponent.AddedFilterComponents.Clear();
					}
				}
			}

			playback.AudioSource.pitch = template ? template.pitch : 1f;

			#region Helper functions

			bool TryCopyAudioFilter<FilterType>(AudioSource destination, Behaviour component, LastTemplateUsed lastTemplateComponent, Action<FilterType, FilterType> copyMethod) where FilterType : Behaviour
			{
				if (component is FilterType filterComponent) {
					var destinationFilter = destination.GetComponent<FilterType>();
					if (destinationFilter == null) {
						destinationFilter = destination.gameObject.AddComponent<FilterType>();
					}

					lastTemplateComponent.AddedFilterComponents.Add(destinationFilter);
					copyMethod(destinationFilter, filterComponent);
					destinationFilter.enabled = filterComponent.enabled;

					return true;
				}

				return false;
			}

			bool IsCustomAudioFilter(Type type)
			{
				if (!s_IsCustomFilterType.TryGetValue(type, out bool isFilter)) {
					var flags = System.Reflection.BindingFlags.Instance |
					            System.Reflection.BindingFlags.Public |
					            System.Reflection.BindingFlags.NonPublic |
					            System.Reflection.BindingFlags.DeclaredOnly
					            ;

					// Traverse the parents as we use DeclaredOnly because private methods won't be found if declared in the base class.
					for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType) {
						if (t.GetMethod("OnAudioFilterRead", flags) != null) {
							isFilter = true;
							break;
						}
					}
					s_IsCustomFilterType[type] = isFilter;
				}
				return isFilter;
			}

			#endregion
		}

		internal class LastTemplateUsed : MonoBehaviour
		{
			public AudioSource TemplateUsed;
			public List<Component> AddedFilterComponents = new List<Component>();
		}
	}
}
