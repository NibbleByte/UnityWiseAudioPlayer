using System;
using UnityEngine;
using UnityEngine.Audio;

namespace DevLocker.Audio.AudioPlayerUtils
{
	/// <summary>
	/// Shared constants and helpers used by the audio data helper structs below
	/// (<see cref="ResourceWithVolume"/>, <see cref="ClipWithVolume"/>, <see cref="ClipWithVolumePitch"/>).
	/// </summary>
	public static class AudioPlayerUtils
	{
		public const float MinVolumeDB = -80f;
		public const float MaxVolumeDB = 0f;

		/// <summary>
		/// Limits used by the editor slider when drawing the volume randomization range. Values can still be typed in manually.
		/// </summary>
		public const int MinVolumeRangeDB = -20;
		public const int MaxVolumeRangeDB = 20;

		/// <summary>
		/// Limits used by the editor slider when drawing the pitch randomization range. Values can still be typed in manually.
		/// </summary>
		public const int MinPitchRangeCents = -1200;
		public const int MaxPitchRangeCents = 1200;

		public const string VolumeDBHint = "Decibels in range [-80, 0]";
		public const string VolumeRangeHint = "Random volume offset in whole decibels, rolled on every play and added on top of the constant volume.\nThe final volume is clamped to [-80, 0] dB.";
		public const string PitchRangeHint = "A pitch will randomly be selected from this range.\nHas priority over the pitches list - if used, the list is ignored.\n\n" + AudioPlayerAsset.CentPitchHint;
		public const string RangeStepHint = "Roll only values divisible by this step (positive or negative), so the randomization is noticeable enough.\nExample: range [-200, 200] with step 100 rolls -200, -100, 0, 100 or 200.\n\n0 means no step - any value in the range.";

		/// <summary>
		/// Roll the volume randomization range (if used) on top of the constant volume. Result is in decibels.
		/// </summary>
		public static float RollVolumeDB(float volumeDB, bool useVolumeRange, int rangeMinDB, int rangeMaxDB, int rangeStepDB)
		{
			if (!useVolumeRange)
				return volumeDB;

			return Mathf.Clamp(volumeDB + RollRange(rangeMinDB, rangeMaxDB, rangeStepDB), MinVolumeDB, MaxVolumeDB);
		}

		/// <summary>
		/// Random value in the [min, max] range, divisible by the step (positive or negative).
		/// Step of 0 means no step - any value in the range.
		/// If no multiple of the step fits in the range, the step is ignored.
		/// </summary>
		public static int RollRange(int min, int max, int step)
		{
			if (min > max) {
				int swap = min;
				min = max;
				max = swap;
			}

			step = Mathf.Abs(step);

			if (step > 0) {
				int firstStep = Mathf.CeilToInt((float) min / step);
				int lastStep = Mathf.FloorToInt((float) max / step);

				if (firstStep <= lastStep)
					return UnityEngine.Random.Range(firstStep, lastStep + 1) * step;
			}

			return UnityEngine.Random.Range(min, max + 1);
		}
	}

	/// <summary>
	/// Use in conductors to show audio with volume.
	/// </summary>
	[Serializable]
	public struct ResourceWithVolume
	{
		public AudioResource Resource;

		/// <summary>
		/// Constant volume, without the randomization range. Check <see cref="GetPlayVolume"/>.
		/// </summary>
		public float Volume => AudioVolumeUtils.DecibelToFloat(VolumeDB);

		[FieldUnitDecorator("dB", AudioPlayerUtils.VolumeDBHint, MinValue = AudioPlayerUtils.MinVolumeDB, MaxValue = AudioPlayerUtils.MaxVolumeDB)]
		public float VolumeDB;

		[Tooltip(AudioPlayerUtils.VolumeRangeHint)]
		public bool UseVolumeRange;

		[FieldUnitDecorator("dB", AudioPlayerUtils.VolumeRangeHint)]
		public int VolumeRangeMinDB;

		[FieldUnitDecorator("dB", AudioPlayerUtils.VolumeRangeHint)]
		public int VolumeRangeMaxDB;

		[FieldUnitDecorator("dB", AudioPlayerUtils.RangeStepHint, MinValue = 0f)]
		public int VolumeRangeStepDB;

		/// <summary>
		/// Volume to be used for a single playback - rolls the randomization range if used, so call it only once per play.
		/// </summary>
		public float GetPlayVolume() => AudioVolumeUtils.DecibelToFloat(GetPlayVolumeDB());

		/// <inheritdoc cref="GetPlayVolume"/>
		public float GetPlayVolumeDB() => AudioPlayerUtils.RollVolumeDB(VolumeDB, UseVolumeRange, VolumeRangeMinDB, VolumeRangeMaxDB, VolumeRangeStepDB);
	}

	/// <summary>
	/// Use in conductors to show audio with volume.
	/// HINT: also check <see cref="ClipWithVolumePitch"/>
	/// </summary>
	[Serializable]
	public struct ClipWithVolume
	{
		public AudioClip Clip;

		/// <summary>
		/// Constant volume, without the randomization range. Check <see cref="GetPlayVolume"/>.
		/// </summary>
		public float Volume => AudioVolumeUtils.DecibelToFloat(VolumeDB);

		[FieldUnitDecorator("dB", AudioPlayerUtils.VolumeDBHint, MinValue = AudioPlayerUtils.MinVolumeDB, MaxValue = AudioPlayerUtils.MaxVolumeDB)]
		public float VolumeDB;

		[Tooltip(AudioPlayerUtils.VolumeRangeHint)]
		public bool UseVolumeRange;

		[FieldUnitDecorator("dB", AudioPlayerUtils.VolumeRangeHint)]
		public int VolumeRangeMinDB;

		[FieldUnitDecorator("dB", AudioPlayerUtils.VolumeRangeHint)]
		public int VolumeRangeMaxDB;

		[FieldUnitDecorator("dB", AudioPlayerUtils.RangeStepHint, MinValue = 0f)]
		public int VolumeRangeStepDB;

		/// <summary>
		/// Volume to be used for a single playback - rolls the randomization range if used, so call it only once per play.
		/// </summary>
		public float GetPlayVolume() => AudioVolumeUtils.DecibelToFloat(GetPlayVolumeDB());

		/// <inheritdoc cref="GetPlayVolume"/>
		public float GetPlayVolumeDB() => AudioPlayerUtils.RollVolumeDB(VolumeDB, UseVolumeRange, VolumeRangeMinDB, VolumeRangeMaxDB, VolumeRangeStepDB);
	}

	/// <summary>
	/// Use in conductors to show audio with volume.
	/// HINT: also check <see cref="ClipWithVolume"/>
	/// </summary>
	[Serializable]
	public struct ClipWithVolumePitch
	{
		public AudioClip Clip;

		/// <summary>
		/// Constant volume, without the randomization range. Check <see cref="GetPlayVolume"/>.
		/// </summary>
		public float Volume => AudioVolumeUtils.DecibelToFloat(VolumeDB);

		[FieldUnitDecorator("dB", AudioPlayerUtils.VolumeDBHint, MinValue = AudioPlayerUtils.MinVolumeDB, MaxValue = AudioPlayerUtils.MaxVolumeDB)]
		public float VolumeDB;

		[Tooltip(AudioPlayerUtils.VolumeRangeHint)]
		public bool UseVolumeRange;

		[FieldUnitDecorator("dB", AudioPlayerUtils.VolumeRangeHint)]
		public int VolumeRangeMinDB;

		[FieldUnitDecorator("dB", AudioPlayerUtils.VolumeRangeHint)]
		public int VolumeRangeMaxDB;

		[FieldUnitDecorator("dB", AudioPlayerUtils.RangeStepHint, MinValue = 0f)]
		public int VolumeRangeStepDB;

		[Tooltip(AudioPlayerUtils.PitchRangeHint)]
		public bool UsePitchRange;

		[FieldUnitDecorator("ct", AudioPlayerUtils.PitchRangeHint)]
		public int PitchRangeMin;

		[FieldUnitDecorator("ct", AudioPlayerUtils.PitchRangeHint)]
		public int PitchRangeMax;

		[FieldUnitDecorator("ct", AudioPlayerUtils.RangeStepHint, MinValue = 0f)]
		public int PitchRangeStep;

		[Tooltip("A pitch will randomly be selected from this list.\nIgnored if the pitch range is used.\n\n" + AudioPlayerAsset.CentPitchHint)]
		[FieldUnitDecorator("ct", "Cents")]
		public int[] Pitches;

		/// <summary>
		/// Volume to be used for a single playback - rolls the randomization range if used, so call it only once per play.
		/// </summary>
		public float GetPlayVolume() => AudioVolumeUtils.DecibelToFloat(GetPlayVolumeDB());

		/// <inheritdoc cref="GetPlayVolume"/>
		public float GetPlayVolumeDB() => AudioPlayerUtils.RollVolumeDB(VolumeDB, UseVolumeRange, VolumeRangeMinDB, VolumeRangeMaxDB, VolumeRangeStepDB);

		public bool HasPitchesList => Pitches != null && Pitches.Length > 0;

		/// <summary>
		/// Does it have any pitch variation - either the range or the list.
		/// The range has priority over the list.
		/// </summary>
		public bool HasPitchVariation => UsePitchRange || HasPitchesList;

		/// <summary>
		/// Select a random pitch in cents. The pitch range has priority over the pitches list.
		/// </summary>
		public int GetRandomPitch()
		{
			if (UsePitchRange)
				return AudioPlayerUtils.RollRange(PitchRangeMin, PitchRangeMax, PitchRangeStep);

			if (HasPitchesList)
				return Pitches[UnityEngine.Random.Range(0, Pitches.Length)];

			return 0;
		}
	}
}
