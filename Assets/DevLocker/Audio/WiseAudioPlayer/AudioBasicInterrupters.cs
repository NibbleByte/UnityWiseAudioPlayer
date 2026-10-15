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
		public bool AllPlayers = true;

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
		public bool AllPlayers = true;

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
		public bool AllPlayers = true;

		[Min(0f)]
		[Tooltip(FadeOutDurationTooltip)]
		public float FadeOutDuration;

		[Tooltip("Stops sounds routed to exactly this mixer group. Child groups are not included.")]
		public AudioMixerGroup OutputMixer;

		public override void Interrupt(AudioPlayback playback)
		{
			if (OutputMixer == null) {
				Debug.LogWarning($"{nameof(StopByMixerInterrupter)} {playback.ConductorAsset} has missing output mixer.", playback.ConductorAsset ? playback.ConductorAsset : playback.Player);
				return;
			}

			if (AllPlayers) {
				AudioPlayer.StopAllPlayersWithOutputMixer(OutputMixer, playback, FadeOutDuration);
			} else {
				playback.Player.StopAllWithOutputMixer(OutputMixer, playback, FadeOutDuration);
			}
		}

		public override void OnValidate(UnityEngine.Object context)
		{
			base.OnValidate(context);

			if (OutputMixer == null && !ReferenceEquals(OutputMixer, null)) {
				Debug.LogWarning($"{nameof(StopByMixerInterrupter)} has missing output mixer.", context);
			}
		}
	}

}
