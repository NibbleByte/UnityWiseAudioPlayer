using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Audio;

namespace DevLocker.Audio
{
	/// <summary>
	/// Asset used by the <see cref="AudioSourcePlayer"/> to play sound in a specific way with given filters.
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
			public abstract IEnumerator Play(AudioSourcePlayer player, AudioPlayerAsset asset);

			public virtual void OnValidate(AudioPlayerAsset context) { }
		}

		/// <summary>
		/// Used as filters when choosing which conductor to play.
		/// </summary>
		[Serializable]
		public abstract class AudioPredicate
		{
			public abstract bool IsAllowed(object context, AudioSourcePlayer player, AudioPlayerAsset asset);

			public virtual void OnValidate(AudioPlayerAsset context) { }
		}

		public enum ConductorsStateStorageLocation
		{
			Asset,
			Player,
		}

		[Serializable]
		public struct AudioConductorBind
		{
			[Tooltip("Responsible for playing the desired audio.")]
			[SerializeReference]
			public AudioConductor Conductor;

			[Tooltip("All filters should be satisfied in order for this event to execute.")]
			[SerializeReference]
			public AudioPredicate[] Filters;
		}

		[Tooltip("Where to store conductors state (if any)?\nExample: should screams shuffle per character or per asset?")]
		public ConductorsStateStorageLocation StateStorageLocation;

		[Tooltip("How sound should be repeated. Repeat interval allows you to specify seconds of silence every time after audio finished playing.\n\nSome conductors may ignore this property and loop on their own.\nThis will override the AudioSourcePlayer setting.")]
		public AudioSourcePlayer.RepeatOptions RepeatPattern;

		[Tooltip("Delay before playing the audio asset. Will not be included in the loop.")]
		public float Delay = 0f;

		[Tooltip("Mixer to be used when playing asset. Will override the one specified on the AudioSourcePlayer. When empty it will try to use the Template's output mixer.")]
		public AudioMixerGroup OutputMixer;

		[Tooltip("Prefab to be used as template when initializing the AudioSource properties")]
		public AudioSource Template;

		public AudioConductorBind[] Conductors;

		/// <summary>
		/// Used by conductors to persist state per asset between usages. For example: don't repeat last clip.
		/// Try to use unique key names.
		/// </summary>
		public Dictionary<string, object> ConductorsStateStorage = new Dictionary<string, object>();


		public IEnumerator Play(AudioSourcePlayer player, AudioSource source, object context)
		{
			switch (RepeatPattern.Pattern) {
				case AudioSourcePlayer.RepeatPatternType.Once:
					source.loop = false;
					break;
				case AudioSourcePlayer.RepeatPatternType.Loop:
					source.loop = true;
					break;
				case AudioSourcePlayer.RepeatPatternType.RepeatInterval:
					source.loop = false;
					break;
				default:
					throw new NotSupportedException();
			}

			bool assetCustomLoop;
			do {
				assetCustomLoop = false;

				var conductorBind = Conductors.FirstOrDefault(bind => bind.Filters.All(f => f?.IsAllowed(context, player, this) ?? true));
				if (conductorBind.Conductor != null) {
					var playConductor = conductorBind.Conductor as Conductors.PlayAudioConductor;

					// If conductor has custom logic other than just playing a looped sound,
					// we implement the loop. Normal sounds should still loop via the source itself,
					// which should drop any playback gaps between loops.
					//
					// NOTE: PlayOneShot() is not looped by the audio source so we must handle it ourselves.
					if (RepeatPattern.IsLooping && (playConductor == null || !playConductor.StopPlayingSound)) {
						assetCustomLoop = true;
						source.loop = false;
					}

					yield return conductorBind.Conductor.Play(player, this);

				} else {

					if (RepeatPattern.IsLooping) {
						// If no match, keep looping untill we get a match according to the loop pattern.
						yield return null;
						assetCustomLoop = true;
						continue;
					} else {
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

				if (RepeatPattern.IsPatternInterval) {
					float waitTime = RepeatPattern.NextIntervalValue();
					float passedTime = 0.0f;

					while (passedTime <= waitTime && RepeatPattern.IsLooping) {
						yield return null;

						if (player == null)
							yield break;

						if (!player.IsPaused && !source.isPlaying) {
							passedTime += Time.deltaTime;
						}
					}
				}

			} while (RepeatPattern.IsPatternInterval || assetCustomLoop);
		}

		void OnValidate()
		{
			Utils.WiseSerializeReferenceValidation.ClearDuplicateReferences(this);

			RepeatPattern.OnValidate(this);

			foreach (var conductorBind in Conductors) {
				conductorBind.Conductor?.OnValidate(this);

				foreach(var filter in conductorBind.Filters) {
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
		public T GetConductorsStorageValue<T>(string keyName, AudioSourcePlayer audioPlayer, T defaultValue)
		{
			object objValue;

			switch (StateStorageLocation) {

				case ConductorsStateStorageLocation.Asset:
					if (ConductorsStateStorage.TryGetValue(keyName, out objValue) && objValue is T) {
						return (T)objValue;
					}
					break;

				case ConductorsStateStorageLocation.Player:
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
		public void SetConductorsStorageValue(string keyName, AudioSourcePlayer audioPlayer, object value)
		{
			switch (StateStorageLocation) {

				case ConductorsStateStorageLocation.Asset:
					ConductorsStateStorage[keyName] = value;
					break;

				case ConductorsStateStorageLocation.Player:
					audioPlayer.ConductorsStateStorage[$"{keyName}_{name}_{GetInstanceID()}"] = value;
					break;

				default:
					break;
			}
		}
	}

}
