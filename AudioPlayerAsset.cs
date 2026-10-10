using System;
using System.Collections;
using UnityEngine;

namespace DevLocker.Audio
{
	/// <summary>
	/// Asset used by the <see cref="AudioPlayer"/> to play sound in a specific way, picking a conductor by its conditions.
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


		[Tooltip("Settings used when playing this asset.")]
		public AudioPlaybackSettings Settings;

		public ConditionalConductor[] Conductors;

		void OnValidate()
		{
			Utils.WiseSerializeReferenceValidation.ClearDuplicateReferences(this);

			Settings.OnValidate(this);

			foreach (var conductorEntry in Conductors) {
				conductorEntry.Conductor?.OnValidate(this);

				foreach(var condition in conductorEntry.Conditions) {
					condition?.OnValidate(this);
				}
			}
		}
		
	}

}
