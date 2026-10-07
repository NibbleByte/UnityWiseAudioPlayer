using System;
using System.Collections;
using UnityEngine;

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
		///
		/// Can be played via <see cref="AudioPlayerAsset"/> or standalone via <see cref="AudioPlayer.PlayConductor"/>.
		/// Use <see cref="AudioPlayback.GetConductorStateValue{T}"/> and
		/// <see cref="AudioPlayback.SetConductorStateValue"/> to persist state.
		/// </summary>
		[Serializable]
		public abstract class AudioConductor
		{
			public abstract IEnumerator Play(AudioPlayback playback);

			/// <summary>
			/// Context is the object owning this conductor.
			/// If you keep conductors in your own assets, call this from their OnValidate().
			/// </summary>
			public virtual void OnValidate(UnityEngine.Object context) { }
		}

		/// <summary>
		/// Used as filters when choosing which conductor to play.
		/// </summary>
		[Serializable]
		public abstract class AudioConductorFilter
		{
			public abstract bool IsAllowed(object context, AudioPlayback playback);

			public virtual void OnValidate(UnityEngine.Object context) { }
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

		[Tooltip("Settings used when playing this asset.")]
		public AudioPlaybackSettings Settings;

		public FilteredConductor[] Conductors;

		void OnValidate()
		{
			Utils.WiseSerializeReferenceValidation.ClearDuplicateReferences(this);

			Settings.OnValidate(this);

			foreach (var conductorEntry in Conductors) {
				conductorEntry.Conductor?.OnValidate(this);

				foreach(var filter in conductorEntry.Filters) {
					filter?.OnValidate(this);
				}
			}
		}
		
	}

}
