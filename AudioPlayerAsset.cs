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

		#region Data helpers

		/// <summary>
		/// Use in conductors to show audio with volume.
		/// </summary>
		[Serializable]
		public struct ResourceWithVolume
		{
			public AudioResource Resource;

			public float Volume => AudioVolumeUtils.DecibelToFloat(VolumeDB);

			[Utils.FieldUnitDecorator("dB", "Decibels in range [-80, 0]", MinValue = -80f, MaxValue = 0f)]
			public float VolumeDB;
		}

		/// <summary>
		/// Use in conductors to show audio with volume.
		/// HINT: also check <see cref="ClipWithVolumePitch"/>
		/// </summary>
		[Serializable]
		public struct ClipWithVolume
		{
			public AudioClip Clip;

			public float Volume => AudioVolumeUtils.DecibelToFloat(VolumeDB);

			[Utils.FieldUnitDecorator("dB", "Decibels in range [-80, 0]", MinValue = -80f, MaxValue = 0f)]
			public float VolumeDB;
		}

		/// <summary>
		/// Use in conductors to show audio with volume.
		/// HINT: also check <see cref="ClipWithVolume"/>
		/// </summary>
		[Serializable]
		public struct ClipWithVolumePitch
		{
			public AudioClip Clip;

			public float Volume => AudioVolumeUtils.DecibelToFloat(VolumeDB);

			[Utils.FieldUnitDecorator("dB", "Decibels in range [-80, 0]", MinValue = -80f, MaxValue = 0f)]
			public float VolumeDB;

			[Tooltip(CentPitchHint + "\n\nA pitch will randomly be selected from this list.")]
			[Utils.FieldUnitDecorator("ct", "Cents")]
			public int[] Pitches;

			public bool HasPitches => Pitches != null && Pitches.Length > 0;
			public int GetRandomPitch() => HasPitches ? Pitches[UnityEngine.Random.Range(0, Pitches.Length)] : 0;
		}

		#endregion

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

		[Tooltip("Should it loop this audio asset (ignores the player settings)?")]
		public bool LoopRepeat;
		[Tooltip("Random interval to repeatedly play the audio asset.")]
		public AudioSourcePlayer.IntervalRange RepeatIntervalRange;

		[Tooltip("Delay before playing the audio asset. Will not be included in the loop.")]
		public float Delay = 0f;

		[Tooltip("Mixer to be used when playing asset. Will override the one specified on the AudioSourcePlayer")]
		public AudioMixerGroup OutputMixer;

		public AudioConductorBind[] Conductors;

		/// <summary>
		/// Used by conductors to persist state per asset between usages. For example: don't repeat last clip.
		/// Try to use unique key names.
		/// </summary>
		public Dictionary<string, object> ConductorsStateStorage = new Dictionary<string, object>();


		public IEnumerator Play(AudioSourcePlayer player, object context)
		{
			do {
				var conductorBind = Conductors.FirstOrDefault(bind => bind.Filters.All(f => f?.IsAllowed(context, player, this) ?? true));
				if (conductorBind.Conductor != null) {
					yield return conductorBind.Conductor.Play(player, this);
				}

				if (LoopRepeat) {
					float waitTime = RepeatIntervalRange.NextValue();
					float passedTime = 0.0f;

					while(passedTime <= waitTime && LoopRepeat) {
						yield return null;

						if (!player.IsPaused && !player.AudioSource.isPlaying) {
							passedTime += Time.deltaTime;
						}
					}
				}

			} while (LoopRepeat);
		}

		void OnValidate()
		{
			Utils.WiseSerializeReferenceValidation.ClearDuplicateReferences(this);

			RepeatIntervalRange.OnValidate(this);

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
