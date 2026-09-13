using System;
using UnityEngine;
using UnityEngine.Audio;

namespace DevLocker.Audio
{
	/// <summary>
	/// Component to quickly stop any playing players directly or players playing specific <see cref="AudioResource"/>s.
	/// </summary>
	public class AudioSourceInterrupter : MonoBehaviour
	{
		[Header("Which?")]
		[Tooltip("AudioSourcePlayers to be stopped (interrupted)")]
		public AudioSourcePlayer[] Players;

		[Tooltip("Audisources (without players) to be stopped (interrupted)")]
		public AudioSource[] AudioSources;

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
				AudioSourcePlayer.PlayStarted += OnPlayStarted;

				if (StopTargetsOnWhenPlaying.IsPlaying) {
					StopTargets();
				}
			}
		}

		void OnDisable()
		{
			// Always unsubscribe as StopTargetsOnWhenPlaying may have been destroyed, so null check is not valid.
			AudioSourcePlayer.PlayStarted -= OnPlayStarted;
		}

		public void StopTargets()
		{
			foreach (var player in Players) {
				if (player && player.IsPlaying) {
					player.Stop();
				}
			}

			foreach (var player in AudioSources) {
				if (player && player.isPlaying) {
					player.Stop();
				}
			}

			if (Resources.Length > 0 || !string.IsNullOrWhiteSpace(ResourceNameContains)) {
				foreach (var player in AudioSourcePlayer.ActivePlayersRegister) {
					if (!player.IsPlaying)
						continue;

					// Already stopped above.
					if (Array.IndexOf(Players, player) != -1)
						continue;

					AudioResource resource = player.AudioReference.AudioResource;

					if (resource && Array.IndexOf(Resources, resource) != -1) {
						player.Stop();

					} else if (!string.IsNullOrWhiteSpace(ResourceNameContains) && resource && resource.name.Contains(ResourceNameContains, StringComparison.OrdinalIgnoreCase)) {
						player.Stop();
					}
				}
			}
		}

		private void OnPlayStarted(AudioSourcePlayer player)
		{
			if (StopTargetsOnWhenPlaying == player) {
				StopTargets();
			}
		}
	}
}
