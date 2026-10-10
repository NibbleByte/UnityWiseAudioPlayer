using System;
using UnityEngine;

namespace DevLocker.Audio.Utils
{
	/// <summary>
	/// Utilities to copy audio components.
	/// </summary>
	public static class AudioCopyUtils
	{
		private static readonly AnimationCurve s_ResetCurve_CustomRolloff = new AnimationCurve(new Keyframe(0, 1, 0, 0), new Keyframe(1, 0, 0, 0));
		private static readonly AnimationCurve s_ResetCurve_SpatialBlend = new AnimationCurve(new Keyframe(0, 0));
		private static readonly AnimationCurve s_ResetCurve_ReverbZoneMix = new AnimationCurve(new Keyframe(0, 1));
		private static readonly AnimationCurve s_ResetCurve_Spread = new AnimationCurve(new Keyframe(0, 0));

		/// <summary>
		/// Copy all AudioSource properties.
		/// </summary>
		public static void CopyAudioSource(AudioSource destination, AudioSource source)
		{
			destination.playOnAwake = source.playOnAwake;
			destination.resource = source.resource;
			destination.outputAudioMixerGroup = source.outputAudioMixerGroup;
			destination.loop = source.loop;
			destination.volume = source.volume;

			CopyAudioSourceDetails(destination, source);
		}

		/// <summary>
		/// Copy the AudioSource details properties.
		/// </summary>
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
			source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, s_ResetCurve_CustomRolloff);
			source.SetCustomCurve(AudioSourceCurveType.SpatialBlend, s_ResetCurve_SpatialBlend);
			source.SetCustomCurve(AudioSourceCurveType.ReverbZoneMix, s_ResetCurve_ReverbZoneMix);
			source.SetCustomCurve(AudioSourceCurveType.Spread, s_ResetCurve_Spread);

			source.rolloffMode = AudioRolloffMode.Logarithmic;  // Because changing the curve changes this property to custom.
																// Overrides the CustomRolloff curve, but the curve is still stored.
		}

		public static void CopyAudioFilter(AudioChorusFilter destination, AudioChorusFilter source)
		{
			destination.dryMix = source.dryMix;
			destination.wetMix1 = source.wetMix1;
			destination.wetMix2 = source.wetMix2;
			destination.wetMix3 = source.wetMix3;
			destination.delay = source.delay;
			destination.rate = source.rate;
			destination.depth = source.depth;
		}

		public static void CopyAudioFilter(AudioDistortionFilter destination, AudioDistortionFilter source)
		{
			destination.distortionLevel = source.distortionLevel;
		}

		public static void CopyAudioFilter(AudioEchoFilter destination, AudioEchoFilter source)
		{
			destination.delay = source.delay;
			destination.decayRatio = source.decayRatio;
			destination.dryMix = source.dryMix;
			destination.wetMix = source.wetMix;
		}

		public static void CopyAudioFilter(AudioHighPassFilter destination, AudioHighPassFilter source)
		{
			destination.cutoffFrequency = source.cutoffFrequency;
			destination.highpassResonanceQ = source.highpassResonanceQ;
		}

		public static void CopyAudioFilter(AudioLowPassFilter destination, AudioLowPassFilter source)
		{
			destination.cutoffFrequency = source.cutoffFrequency;
			destination.lowpassResonanceQ = source.lowpassResonanceQ;
		}

		public static void CopyAudioFilter(AudioReverbFilter destination, AudioReverbFilter source)
		{
			destination.dryLevel = source.dryLevel;
			destination.room = source.room;
			destination.roomHF = source.roomHF;
			destination.roomLF = source.roomLF;
			destination.decayTime = source.decayTime;
			destination.decayHFRatio = source.decayHFRatio;
			destination.reflectionsLevel = source.reflectionsLevel;
			destination.reflectionsDelay = source.reflectionsDelay;
			destination.reverbLevel = source.reverbLevel;
			destination.reverbDelay = source.reverbDelay;
			destination.hfReference = source.hfReference;
			destination.lfReference = source.lfReference;
			destination.diffusion = source.diffusion;
			destination.density = source.density;

			destination.reverbPreset = source.reverbPreset;
		}
	}
}
