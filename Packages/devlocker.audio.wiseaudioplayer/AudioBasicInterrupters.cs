using System;
using UnityEngine;
using UnityEngine.Audio;

namespace DevLocker.Audio.Playback
{
	[Serializable]
	public class MultipleInterrupters : AudioInterrupter
	{
		[Tooltip("Run each interrupter in order.")]
		[SerializeReference]
		public AudioInterrupter[] Interrupters;

		public override void Interrupt(AudioPlayback playback)
		{
			foreach (AudioInterrupter interrupter in Interrupters) {
				if (interrupter == null) {
					Debug.LogWarning($"Missing interrupter in {nameof(MultipleInterrupters)} for \"{playback.DebugContext}\".", playback.DebugContext);
					continue;
				}

				interrupter.Interrupt(playback);
			}
		}

		public override void OnValidate(UnityEngine.Object context)
		{
			base.OnValidate(context);

			foreach (AudioInterrupter interrupter in Interrupters) {
				interrupter?.OnValidate(context);
			}
		}
	}

	[Serializable]
	public class StopAllInterrupter : AudioInterrupter
	{
		[Tooltip(AllPlayersTooltip)]
		public bool AllPlayers;

		[Min(0f)]
		[Tooltip(FadeOutDurationTooltip)]
		public float FadeOutDuration;

		public override void Interrupt(AudioPlayback playback)
		{
			if (AllPlayers) {
				AudioPlayer.StopAllPlayersExcept(playback, FadeOutDuration);
			} else {
				playback.Player.StopAllExcept(playback, FadeOutDuration);
			}
		}
	}

	[Serializable]
	public class StopSameAssetInterrupter : AudioInterrupter
	{
		[Tooltip(AllPlayersTooltip)]
		public bool AllPlayers;

		[Min(0f)]
		[Tooltip(FadeOutDurationTooltip)]
		public float FadeOutDuration;

		public override void Interrupt(AudioPlayback playback)
		{
			if (playback.ConductorAsset != null) {
				if (AllPlayers) {
					AudioPlayer.StopAllPlayersFromConductorAsset(playback.ConductorAsset, playback, FadeOutDuration);
				} else {
					playback.Player.StopAllFromConductorAsset(playback.ConductorAsset, playback, FadeOutDuration);
				}
			} else {
				Debug.LogWarning($"Trying to interrupt audio player, but no valid conductor asset was used.", playback.Player);
			}
		}
	}

	[Serializable]
	public class StopByTagsInterrupter : AudioInterrupter
	{
		[Tooltip(AllPlayersTooltip)]
		public bool AllPlayers;

		[Min(0f)]
		[Tooltip(FadeOutDurationTooltip)]
		public float FadeOutDuration;

		[Tooltip("Stops sounds whose asset has any of these tags.")]
		public AudioTag[] Tags = Array.Empty<AudioTag>();

		public override void Interrupt(AudioPlayback playback)
		{
			if (AllPlayers) {
				AudioPlayer.StopAllPlayersWithAnyTag(Tags, playback, FadeOutDuration);
			} else {
				playback.Player.StopAllWithAnyTag(Tags, playback, FadeOutDuration);
			}
		}

		public override void OnValidate(UnityEngine.Object context)
		{
			base.OnValidate(context);

			foreach(var tag in Tags) {
				if (tag == null && !ReferenceEquals(tag, null)) {
					Debug.LogWarning($"{nameof(StopByTagsInterrupter)} has missing tag.", context);
					break;
				}
			}
		}
	}

	[Serializable]
	public class StopByMixerInterrupter : AudioInterrupter
	{
		[Tooltip(AllPlayersTooltip)]
		public bool AllPlayers;

		[Min(0f)]
		[Tooltip(FadeOutDurationTooltip)]
		public float FadeOutDuration;

		[Tooltip("Stops sounds routed to any of these mixer groups or their child groups.")]
		public AudioMixerGroup[] OutputMixers = Array.Empty<AudioMixerGroup>();

		public override void Interrupt(AudioPlayback playback)
		{
			if (AllPlayers) {
				AudioPlayer.StopAllPlayersWithAnyOutputMixer(OutputMixers, playback, FadeOutDuration);
			} else {
				playback.Player.StopAllWithAnyOutputMixer(OutputMixers, playback, FadeOutDuration);
			}
		}

		public override void OnValidate(UnityEngine.Object context)
		{
			base.OnValidate(context);

			if (OutputMixers == null)
				return;

			foreach (var outputMixer in OutputMixers) {
				if (outputMixer == null && !ReferenceEquals(outputMixer, null)) {
					Debug.LogWarning($"{nameof(StopByMixerInterrupter)} has missing output mixer.", context);
					break;
				}
			}
		}
	}

}
