using System;
using UnityEngine;
using UnityEngine.Audio;

namespace DevLocker.Audio.Utils
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
		public const string VolumeRangeHint = "Random volume offset in whole decibels, rolled on every play and added on top of the clip volume.\nThe final volume is clamped to [-80, 0] dB.";
		public const string PitchRangeHint = "Random pitch offset in cents, rolled on every play and added on top of the clip pitch.\n\n" + AudioPlayerAsset.CentPitchHint;
		public const string UsePitchRangeHint = "A pitch will randomly be selected from this range.\nHas priority over the pitches list - if used, the list is ignored.\n\n" + AudioPlayerAsset.CentPitchHint;
		public const string RangeStepHint = "Roll only values divisible by this step (positive or negative), so the randomization is noticeable enough.\nExample: range [-200, 200] with step 100 rolls -200, -100, 0, 100 or 200.\n\n0 means no step - any value in the range.";

		/// <summary>
		/// Roll the volume randomization range (if used) on top of the constant volume, plus any additional offset
		/// (for example rolled by the conductor itself). Result is in decibels.
		/// </summary>
		public static float RollVolumeDB(float volumeDB, bool useVolumeRange, VolumeRange volumeRange, int extraOffsetDB = 0)
		{
			int offsetDB = extraOffsetDB + (useVolumeRange ? volumeRange.Roll() : 0);

			return Mathf.Clamp(volumeDB + offsetDB, MinVolumeDB, MaxVolumeDB);
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
	/// Random volume offset in whole decibels. Roll it once per play.
	/// </summary>
	[Serializable]
	public struct VolumeRange
	{
		[FieldUnitDecorator("dB", AudioPlayerUtils.VolumeRangeHint)]
		public int MinDB;

		[FieldUnitDecorator("dB", AudioPlayerUtils.VolumeRangeHint)]
		public int MaxDB;

		[FieldUnitDecorator("dB", AudioPlayerUtils.RangeStepHint, MinValue = 0f)]
		public int StepDB;

		public bool IsUsed => MinDB != 0 || MaxDB != 0;

		/// <summary>
		/// Random volume offset in decibels - rolls a new value on every call.
		/// </summary>
		public int Roll() => AudioPlayerUtils.RollRange(MinDB, MaxDB, StepDB);
	}

	/// <summary>
	/// Random pitch offset in cents. Roll it once per play.
	/// </summary>
	[Serializable]
	public struct PitchRange
	{
		[FieldUnitDecorator("ct", AudioPlayerUtils.PitchRangeHint)]
		public int Min;

		[FieldUnitDecorator("ct", AudioPlayerUtils.PitchRangeHint)]
		public int Max;

		[FieldUnitDecorator("ct", AudioPlayerUtils.RangeStepHint, MinValue = 0f)]
		public int Step;

		public bool IsUsed => Min != 0 || Max != 0;

		/// <summary>
		/// Random pitch offset in cents - rolls a new value on every call.
		/// </summary>
		public int Roll() => AudioPlayerUtils.RollRange(Min, Max, Step);
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

		[Tooltip(AudioPlayerUtils.VolumeRangeHint)]
		public VolumeRange VolumeRange;

		/// <summary>
		/// Volume to be used for a single playback - rolls the randomization range if used, so call it only once per play.
		/// Pass any additional offset in decibels (for example rolled by the conductor itself).
		/// </summary>
		public float GetPlayVolume(int extraOffsetDB = 0) => AudioVolumeUtils.DecibelToFloat(GetPlayVolumeDB(extraOffsetDB));

		/// <inheritdoc cref="GetPlayVolume"/>
		public float GetPlayVolumeDB(int extraOffsetDB = 0) => AudioPlayerUtils.RollVolumeDB(VolumeDB, UseVolumeRange, VolumeRange, extraOffsetDB);
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

		[Tooltip(AudioPlayerUtils.VolumeRangeHint)]
		public VolumeRange VolumeRange;

		/// <summary>
		/// Volume to be used for a single playback - rolls the randomization range if used, so call it only once per play.
		/// Pass any additional offset in decibels (for example rolled by the conductor itself).
		/// </summary>
		public float GetPlayVolume(int extraOffsetDB = 0) => AudioVolumeUtils.DecibelToFloat(GetPlayVolumeDB(extraOffsetDB));

		/// <inheritdoc cref="GetPlayVolume"/>
		public float GetPlayVolumeDB(int extraOffsetDB = 0) => AudioPlayerUtils.RollVolumeDB(VolumeDB, UseVolumeRange, VolumeRange, extraOffsetDB);
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

		[Tooltip(AudioPlayerUtils.VolumeRangeHint)]
		public VolumeRange VolumeRange;

		[Tooltip(AudioPlayerUtils.UsePitchRangeHint)]
		public bool UsePitchRange;

		[Tooltip(AudioPlayerUtils.UsePitchRangeHint)]
		public PitchRange PitchRange;

		[Tooltip("A pitch will randomly be selected from this list.\nIgnored if the pitch range is used.\n\n" + AudioPlayerAsset.CentPitchHint)]
		[FieldUnitDecorator("ct", "Cents")]
		public int[] Pitches;

		/// <summary>
		/// Volume to be used for a single playback - rolls the randomization range if used, so call it only once per play.
		/// Pass any additional offset in decibels (for example rolled by the conductor itself).
		/// </summary>
		public float GetPlayVolume(int extraOffsetDB = 0) => AudioVolumeUtils.DecibelToFloat(GetPlayVolumeDB(extraOffsetDB));

		/// <inheritdoc cref="GetPlayVolume"/>
		public float GetPlayVolumeDB(int extraOffsetDB = 0) => AudioPlayerUtils.RollVolumeDB(VolumeDB, UseVolumeRange, VolumeRange, extraOffsetDB);

		public bool HasPitchesList => Pitches != null && Pitches.Length > 0;

		/// <summary>
		/// Does it have any pitch variation - either the range or the list.
		/// The range has priority over the list.
		/// </summary>
		public bool HasPitchVariation => UsePitchRange || HasPitchesList;

		/// <summary>
		/// Select a random pitch in cents. The pitch range has priority over the pitches list.
		/// Pass any additional offset in cents (for example rolled by the conductor itself).
		/// </summary>
		public int GetRandomPitch(int extraOffsetCents = 0)
		{
			if (UsePitchRange)
				return PitchRange.Roll() + extraOffsetCents;

			if (HasPitchesList)
				return Pitches[UnityEngine.Random.Range(0, Pitches.Length)] + extraOffsetCents;

			return extraOffsetCents;
		}
	}
}
