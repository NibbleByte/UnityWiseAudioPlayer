using System;
using UnityEngine;
using UnityEngine.Audio;

namespace DevLocker.Audio
{
	/// <summary>
	/// Component to quickly stop any playing players directly or players playing specific <see cref="AudioResource"/>s.
	/// </summary>
	[AddComponentMenu("Audio/Audio Source Interrupter")]
	public class AudioSourceInterrupter : MonoBehaviour
	{
		[Header("Which?")]
		[Tooltip("AudioSourcePlayers to be stopped (interrupted)")]
		public AudioSourcePlayer[] Players;

		[Tooltip("AudioSourcePlayers that are playing these AudioResources will be stopped")]
		public AudioResource[] Resources;

		[Tooltip("AudioSourcePlayers that are playing AudioResources with names containing this string (case-insensitive) will be stopped")]
		public string ResourceNameContains = "";

		[Header("When?")]
		[Tooltip("Stop specified above targets on enabling this component")]
		public bool StopTargetsOnEnable = true;

		[Tooltip("Stop specified above targets when specified player starts playing")]
		public AudioSourcePlayer StopTargetsOnWhenPlaying;

		void OnEnable()
		{
			if (StopTargetsOnEnable) {
				StopTargets();
			}

			if (StopTargetsOnWhenPlaying) {
				AudioSourcePlayer.PlaybackStarted += OnPlayStarted;

				if (StopTargetsOnWhenPlaying.IsPlaying) {
					StopTargets();
				}
			}
		}

		void OnDisable()
		{
			// Always unsubscribe as StopTargetsOnWhenPlaying may have been destroyed, so null check is not valid.
			AudioSourcePlayer.PlaybackStarted -= OnPlayStarted;
		}

		public void StopTargets()
		{
			foreach (var player in Players) {
				if (player && player.IsPlaying) {
					player.Stop();
				}
			}

			if (Resources.Length > 0 || !string.IsNullOrWhiteSpace(ResourceNameContains)) {
				foreach (var player in AudioSourcePlayer.ActivePlayers) {
					if (!player.IsPlaying)
						continue;

					// Already stopped above.
					if (Array.IndexOf(Players, player) != -1)
						continue;

					var playbacks = player.ActivePlaybacks;
					for (int i = playbacks.Count - 1; i >= 0; i--) {
						var playback = playbacks[i];

						if (playback.AudioSource == null)
							continue;

						AudioResource resource = playback.AudioSource.resource;

						if (resource && Array.IndexOf(Resources, resource) != -1) {
							player.Stop(playback);
							continue;
						}

						if (!string.IsNullOrWhiteSpace(ResourceNameContains) && resource && resource.name.Contains(ResourceNameContains, StringComparison.OrdinalIgnoreCase)) {
							player.Stop(playback);
							continue;
						}
					}
				}
			}
		}

		private void OnPlayStarted(AudioSourcePlayer.AudioPlayback playback)
		{
			if (StopTargetsOnWhenPlaying == playback.Player) {
				StopTargets();
			}
		}
	}
}
