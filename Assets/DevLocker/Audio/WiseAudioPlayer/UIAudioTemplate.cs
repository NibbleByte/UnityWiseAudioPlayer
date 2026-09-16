using UnityEngine;

namespace DevLocker.Audio
{
	/// <summary>
	/// Template to be used by <see cref="UIAudioEffects"/> as a way of sharing settings and audio references.
	/// Can be placed next to AudioSource, preferably on a simple prefab. The AudioSource component will be used as template (if present).
	/// </summary>
	public class UIAudioTemplate : MonoBehaviour
	{
		[Tooltip("Submit is called on pressing <Enter> or gamepad <A>, but NOT on pointer clicks.")]
		public AudioSourcePlayer.AudioReferenceProperty SubmitAudio;
		[Tooltip("OnClick is called on only for pointer clicks, NOT on pressing <Enter> or gamepad <A>.")]
		public AudioSourcePlayer.AudioReferenceProperty PointerClickAudio;

		public AudioSourcePlayer.AudioReferenceProperty PointerDownAudio;
		public AudioSourcePlayer.AudioReferenceProperty PointerUpAudio;
		public AudioSourcePlayer.AudioReferenceProperty PointerEnterAudio;
		public AudioSourcePlayer.AudioReferenceProperty PointerExitAudio;

		public AudioSourcePlayer.AudioReferenceProperty SelectAudio;
		public AudioSourcePlayer.AudioReferenceProperty DeselectAudio;
	}
}
