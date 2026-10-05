using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Audio;

namespace DevLocker.Audio
{
	/// <summary>
	/// Asset used by the <see cref="AudioPlayer"/> to play sound in a specific way with given filters.
	/// </summary>
	[CreateAssetMenu(fileName = "Unknown_AudioAsset", menuName = "Audio/Audio Player Asset")]
	public class AudioPlayerAsset : ScriptableObject
	{
		// To comply with music theory, the size of pitch difference should use semitones or cents.
		// One octave corresponding to a doubling of frequency. For example, the frequency one octave above 40 Hz is 80 Hz. In other words - power of two.
		// Semitone is the smallest musical step (white-to-black keys on piano distance).
		// Each octave is 12 semitones. To move a frequency up one octave you multiply by 2. So to move a frequency up one semitone you multiply by 2^(1/12)= 1.059463
		// Each semitone has 100 cent units. So to move a frequency up one cent you multiply by 2^(1/1200)= 1.0005777895065548592967925757932
		// Read more here: https://www.reddit.com/r/Unity3D/comments/18ycc02/sharing_a_really_basic_but_useful_tip_if_theres_a/
		// We use cents, because Unity uses cents in their AudioRandomContainer.
		public const float CentPitchSize = 1.0005777895065548592967925757932f;
		public const string CentPitchHint = "Pitch sequence in cents. One semitone has 100 cents. One octave has 12 semitones.\nPrefer using semitone pitches, e.g. 100, 200, 400, etc.\n0 means no pitch change.";


		/// <summary>
		/// Responsible for playing the desired audio.
		/// Inherit to have custom behaviour.
		/// </summary>
		[Serializable]
		public abstract class AudioConductor
		{
			public abstract IEnumerator Play(AudioPlayer.AudioPlayback playback, AudioPlayerAsset asset);

			public virtual void OnValidate(AudioPlayerAsset context) { }
		}

		/// <summary>
		/// Used as filters when choosing which conductor to play.
		/// </summary>
		[Serializable]
		public abstract class AudioConductorFilter
		{
			public abstract bool IsAllowed(object context, AudioPlayer player, AudioPlayerAsset asset);

			public virtual void OnValidate(AudioPlayerAsset context) { }
		}

		public enum ConductorsStateScope
		{
			PerAsset,
			PerPlayer,
		}

		public enum InterruptSoundsMode
		{
			DontInterrupt = 0,
			InterruptAll = 1,
			InterruptSameAsset = 4,
		}

		[Serializable]
		public struct FilteredConductor
		{
			[Tooltip("Responsible for playing the desired audio.")]
			[SerializeReference]
			public AudioConductor Conductor;

			[Tooltip("All filters must pass for this conductor to be used. The first conductor whose filters all pass is played.")]
			[SerializeReference]
			public AudioConductorFilter[] Filters;
		}

		[Tooltip("Scope of the conductors' state, such as shuffle order or last clip played.\nPer Asset: shared by all players.\nPer Player: each player has its own.\n\nFor example, should screams shuffle per character or globally?")]
		public ConductorsStateScope StateScope;

		[Tooltip("How the sound repeats. Loop With Interval adds seconds of silence after each play.\n\nSome conductors ignore this and loop on their own.\nOverrides the AudioPlayer setting.")]
		public AudioPlayer.RepeatSettings Repeat;

		[Tooltip("When this asset starts, what happens to the sounds already playing on the same player: keep them, stop them all, or stop only the ones from this asset. Stops are instant.")]
		public InterruptSoundsMode InterruptMode;

		[Tooltip("Seconds to wait before the asset starts. Not repeated when looping.")]
		public float Delay = 0f;

		[Tooltip("Mixer group to output to. Overrides the AudioPlayer's mixer. If empty, uses the template's mixer.")]
		public AudioMixerGroup OutputMixer;

		[Tooltip("AudioSource (prefab or scene object) whose settings are copied to the playing source. Overrides the player's template.")]
		public AudioSource Template;

		public FilteredConductor[] Conductors;

		/// <summary>
		/// Used by conductors to persist state per asset between usages. For example: don't repeat last clip.
		/// Try to use unique key names.
		/// </summary>
		public Dictionary<string, object> ConductorsStateStorage = new Dictionary<string, object>();


		public IEnumerator Play(AudioPlayer.AudioPlayback playback, object context)
		{
			AudioPlayer player = playback.Player;
			AudioSource source = playback.AudioSource;

			switch (Repeat.Mode) {
				case AudioPlayer.RepeatMode.Once:
					source.loop = false;
					break;
				case AudioPlayer.RepeatMode.Loop:
					source.loop = true;
					break;
				case AudioPlayer.RepeatMode.LoopWithInterval:
					source.loop = false;
					break;
				default:
					throw new NotSupportedException();
			}

			switch (InterruptMode) {
				case InterruptSoundsMode.DontInterrupt:
					// Do nothing.
					break;
				case InterruptSoundsMode.InterruptAll:
					player.StopAllExcept(playback);
					break;
				case InterruptSoundsMode.InterruptSameAsset:
					player.StopAllWithAsset(this, playback);
					break;
				default: throw new NotSupportedException(InterruptMode.ToString());
			}

			bool assetCustomLoop;
			do {
				assetCustomLoop = false;

				var conductorEntry = Conductors.FirstOrDefault(entry => entry.Filters.All(f => f?.IsAllowed(context, player, this) ?? true));
				if (conductorEntry.Conductor != null) {
					var playConductor = conductorEntry.Conductor as Conductors.PlayAudioConductor;

					// If conductor has custom logic other than just playing a looped sound,
					// we implement the loop. Normal sounds should still loop via the source itself,
					// which should drop any playback gaps between loops.
					if (Repeat.IsRepeating && playConductor == null) {
						assetCustomLoop = true;
						playback.AudioSource.loop = false;
					}

					yield return conductorEntry.Conductor.Play(playback, this);

				} else {

					if (Repeat.IsRepeating) {
						// If no match, keep looping untill we get a match according to the loop pattern.
						yield return null;
						assetCustomLoop = true;
						continue;
					} else {
						playback.WasCancelled = true;
						yield break;
					}
				}

				if (assetCustomLoop) {
					do {
						yield return null;
					} while (player && (source.isPlaying || player.IsPaused));

					if (player == null)
						yield break;
				}

				if (Repeat.IsInterval) {
					float waitTime = Repeat.RollInterval();
					float passedTime = 0.0f;

					while (passedTime <= waitTime && Repeat.IsRepeating) {
						yield return null;

						if (player == null)
							yield break;

						if (!player.IsPaused && !source.isPlaying) {
							passedTime += Time.unscaledDeltaTime;
						}
					}
				}

			} while (Repeat.IsInterval || assetCustomLoop);
		}

		void OnValidate()
		{
			Utils.WiseSerializeReferenceValidation.ClearDuplicateReferences(this);

			Repeat.OnValidate(this);

			foreach (var conductorEntry in Conductors) {
				conductorEntry.Conductor?.OnValidate(this);

				foreach(var filter in conductorEntry.Filters) {
					filter?.OnValidate(this);
				}
			}
		}

#if UNITY_EDITOR
		//
		// This is the only way to reset scriptable object's state if assembly reload is disabled. Sad!
		//
		void OnEnable()
		{
			UnityEditor.EditorApplication.playModeStateChanged += EditorStateChanged;
		}

		void OnDisable()
		{
			UnityEditor.EditorApplication.playModeStateChanged -= EditorStateChanged;
		}

		private void EditorStateChanged(UnityEditor.PlayModeStateChange state)
		{
			if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode) {
				ConductorsStateStorage = new Dictionary<string, object>();
			}
		}
#endif


		/// <summary>
		/// Get conductors storage value based on the <see cref="ConductorsStateStorage"/> setting.
		/// </summary>
		public T GetConductorsStorageValue<T>(string keyName, AudioPlayer audioPlayer, T defaultValue)
		{
			object objValue;

			switch (StateScope) {

				case ConductorsStateScope.PerAsset:
					if (ConductorsStateStorage.TryGetValue(keyName, out objValue) && objValue is T) {
						return (T)objValue;
					}
					break;

				case ConductorsStateScope.PerPlayer:
					if (audioPlayer.ConductorsStateStorage.TryGetValue($"{keyName}_{name}_{GetInstanceID()}", out objValue) && objValue is T) {
						return (T)objValue;
					}
					break;

				default:
					break;
			}

			return defaultValue;
		}

		/// <summary>
		/// Set conductors storage value based on the <see cref="ConductorsStateStorage"/> setting.
		/// </summary>
		public void SetConductorsStorageValue(string keyName, AudioPlayer audioPlayer, object value)
		{
			switch (StateScope) {

				case ConductorsStateScope.PerAsset:
					ConductorsStateStorage[keyName] = value;
					break;

				case ConductorsStateScope.PerPlayer:
					audioPlayer.ConductorsStateStorage[$"{keyName}_{name}_{GetInstanceID()}"] = value;
					break;

				default:
					break;
			}
		}
	}

}
