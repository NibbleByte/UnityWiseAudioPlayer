using System;
using UnityEngine;

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
					AudioPlayer.StopAllPlayersWithConductorAsset(playback.ConductorAsset, playback);
				} else {
					playback.Player.StopAllWithConductorAsset(playback.ConductorAsset, playback);
				}
			} else {
				Debug.LogWarning($"Trying to interrupt audio player, but no valid conductor asset was used.", playback.Player);
			}
		}
	}

}
