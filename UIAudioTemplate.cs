using UnityEngine;

namespace DevLocker.Audio
{
	/// <summary>
	/// Template to be used by <see cref="UIAudioEffects"/> as a way of sharing settings and audio references.
	/// Can be placed next to AudioSource, preferably on a simple prefab. The AudioSource component will be used as template (if present).
	/// </summary>
	[AddComponentMenu("Audio/UI Audio Template")]
	public class UIAudioTemplate : MonoBehaviour
	{
		[Tooltip("Submit is called on pressing <Enter> or gamepad <A>, but NOT on pointer clicks.")]
		public AudioPlayer.AudioReferenceProperty SubmitAudio;
		[Tooltip("OnClick is called only for pointer clicks, NOT on pressing <Enter> or gamepad <A>.")]
		public AudioPlayer.AudioReferenceProperty PointerClickAudio;

		public AudioPlayer.AudioReferenceProperty PointerDownAudio;
		public AudioPlayer.AudioReferenceProperty PointerUpAudio;
		public AudioPlayer.AudioReferenceProperty PointerEnterAudio;
		public AudioPlayer.AudioReferenceProperty PointerExitAudio;

		public AudioPlayer.AudioReferenceProperty SelectAudio;
		public AudioPlayer.AudioReferenceProperty DeselectAudio;
	}
}
