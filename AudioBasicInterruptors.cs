using System;
using UnityEngine;
using UnityEngine.Audio;

namespace DevLocker.Audio.Playback
{
	[Serializable]
	public class StopAllSounds : AudioInterrupter
	{
		[Tooltip("Should it stop sounds for all players, or just the currently playing audio player.")]
		public bool AllPlayers;

		public override void Interrupt(AudioPlayback playback)
		{
			if (AllPlayers) {
				AudioPlayer.StopAllPlayersExcept(playback);
			} else {
				playback.Player.StopAllExcept(playback);
			}
		}
	}

	[Serializable]
	public class StopSoundsFromThisAsset : AudioInterrupter
	{
		[Tooltip("Should it stop sounds for all players, or just the currently playing audio player.")]
		public bool AllPlayers = true;

		public override void Interrupt(AudioPlayback playback)
		{
			if (playback.ConductorAsset != null) {
				if (AllPlayers) {
					AudioPlayer.StopAllPlayersFromConductorAsset(playback.ConductorAsset, playback);
				} else {
					playback.Player.StopAllFromConductorAsset(playback.ConductorAsset, playback);
				}
			} else {
				Debug.LogWarning($"Trying to interrupt audio player, but no valid conductor asset was used.", playback.Player);
			}
		}
	}

	[Serializable]
	public class StopSoundsWithTag : AudioInterrupter
	{
		[Tooltip("Should it stop sounds for all players, or just the currently playing audio player.")]
		public bool AllPlayers = true;

		[Tooltip("Stop sounds from assets with any of these tags.")]
		public AudioTagObject[] Tags = Array.Empty<AudioTagObject>();

		public override void Interrupt(AudioPlayback playback)
		{
			if (AllPlayers) {
				AudioPlayer.StopAllPlayersWithTags(Tags, playback);
			} else {
				playback.Player.StopAllWithTags(Tags, playback);
			}
		}

		public override void OnValidate(UnityEngine.Object context)
		{
			base.OnValidate(context);

			foreach(var tag in Tags) {
				if (tag == null && !ReferenceEquals(tag, null)) {
					Debug.LogWarning($"{nameof(StopSoundsWithTag)} has missing tag.", context);
					break;
				}
			}
		}
	}

	[Serializable]
	public class StopSoundsWithOutputMixer : AudioInterrupter
	{
		[Tooltip("Should it stop sounds for all players, or just the currently playing audio player.")]
		public bool AllPlayers = true;

		[Tooltip("Stop sounds using this output mixer.")]
		public AudioMixerGroup OutputMixer;

		public override void Interrupt(AudioPlayback playback)
		{
			if (OutputMixer == null) {
				Debug.LogWarning($"{nameof(StopSoundsWithOutputMixer)} {playback.ConductorAsset} has missing output mixer.", playback.ConductorAsset ? playback.ConductorAsset : playback.Player);
				return;
			}

			if (AllPlayers) {
				AudioPlayer.StopAllPlayersWithOutputMixer(OutputMixer, playback);
			} else {
				playback.Player.StopAllWithOutputMixer(OutputMixer, playback);
			}
		}

		public override void OnValidate(UnityEngine.Object context)
		{
			base.OnValidate(context);

			if (OutputMixer == null && !ReferenceEquals(OutputMixer, null)) {
				Debug.LogWarning($"{nameof(StopSoundsWithOutputMixer)} has missing output mixer.", context);
			}
		}
	}

}
