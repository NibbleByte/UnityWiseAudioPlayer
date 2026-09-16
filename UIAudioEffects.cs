using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.UI;


namespace DevLocker.Audio
{
	/// <summary>
	/// Behaviour to add sounds to your UI buttons and other <see cref="Selectable"/> elements.
	/// Instead of copying audio references for multiple buttons, use a shared template prefab.
	/// The audio references here can be used to override the template ones.
	///
	/// NOTE: If audio reference is not supplied it won't consume the event itself.
	/// </summary>
	public class UIAudioEffects : MonoBehaviour
	{
		public enum InteractableModeType
		{
			AlwaysPlay,
			PlayWhenInteractable,
			PlayWhenNonInteractable,
		}

		[Tooltip("When should audio be played in relation to the Selectable attached to.")]
		public InteractableModeType InteractableMode = InteractableModeType.PlayWhenInteractable;

		[Tooltip("Optional template to share audio setup.\nIt can have AudioSource component to copy settings from - this way you can easily set common mixer output etc.")]
		public UIAudioTemplate Template;

		[Header("Template Overrides")]

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

		public AudioSourcePlayer AudioPlayer { get; private set; }
		public AudioSource AudioSource { get; private set; }
		private Selectable m_Selectable;

		void Awake()
		{
			AudioPlayer = GetComponent<AudioSourcePlayer>() ?? gameObject.AddComponent<AudioSourcePlayer>();

			if (Template) {

				var templateSource = Template.GetComponent<AudioSource>();
				if (templateSource) {
					AudioPlayer.Template = templateSource;
				} else {
					AudioPlayer.AudioSource.spatialBlend = 0f;  // Make it 2D
				}

				SubmitAudio = SubmitAudio.HasValidReference ? SubmitAudio : Template.SubmitAudio;
				PointerClickAudio = PointerClickAudio.HasValidReference ? PointerClickAudio : Template.PointerClickAudio;
				PointerDownAudio = PointerDownAudio.HasValidReference ? PointerDownAudio : Template.PointerDownAudio;
				PointerUpAudio = PointerUpAudio.HasValidReference ? PointerUpAudio : Template.PointerUpAudio;
				PointerEnterAudio = PointerEnterAudio.HasValidReference ? PointerEnterAudio : Template.PointerEnterAudio;
				PointerExitAudio = PointerExitAudio.HasValidReference ? PointerExitAudio : Template.PointerExitAudio;
				SelectAudio = SelectAudio.HasValidReference ? SelectAudio : Template.SelectAudio;
				DeselectAudio = DeselectAudio.HasValidReference ? DeselectAudio : Template.DeselectAudio;

			} else {
				AudioPlayer.AudioSource.spatialBlend = 0f;	// Make it 2D
			}

			// Add handler components instead of listening to ourselves.
			// If we did, we would have to implement all those interfaces, but only use a few.
			// Implementing the interface will consume the event, so child objects might not hear it.
			SetupHandler<UIAudioEffects_Submit>(SubmitAudio);
			SetupHandler<UIAudioEffects_PointerClick>(PointerClickAudio);

			SetupHandler<UIAudioEffects_PointerDown>(PointerDownAudio);
			SetupHandler<UIAudioEffects_PointerUp>(PointerUpAudio);
			SetupHandler<UIAudioEffects_PointerEnter>(PointerEnterAudio);
			SetupHandler<UIAudioEffects_PointerExit>(PointerExitAudio);
			SetupHandler<UIAudioEffects_Select>(SelectAudio);
			SetupHandler<UIAudioEffects_Deselect>(DeselectAudio);
		}

		void OnValidate()
		{
			SubmitAudio.OnValidate(this);
			PointerClickAudio.OnValidate(this);
			PointerDownAudio.OnValidate(this);
			PointerUpAudio.OnValidate(this);
			PointerEnterAudio.OnValidate(this);
			PointerExitAudio.OnValidate(this);
			SelectAudio.OnValidate(this);
			DeselectAudio.OnValidate(this);
		}

		private void PlayAudio(AudioSourcePlayer.AudioReferenceProperty audioReference)
		{
			if (!audioReference.HasValidReference)
				return;

			if (InteractableMode != InteractableModeType.AlwaysPlay) {
				if (m_Selectable == null) {
					m_Selectable = GetComponentInParent<Selectable>();
				}

				bool playWhenInteractable = InteractableMode == InteractableModeType.PlayWhenInteractable;
				if (m_Selectable && m_Selectable.IsInteractable() != playWhenInteractable) {
					return;
				}
			}

			AudioPlayer.PlayAudioReference(audioReference);
		}

		#region Helper behaviours

		private void SetupHandler<T>(AudioSourcePlayer.AudioReferenceProperty audioReference) where T : UIAudioEffects_EventHandler
		{
			if (!audioReference.HasValidReference)
				return;

			var handler = gameObject.AddComponent<T>();
			handler.Owner = this;
			handler.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
		}

		private class UIAudioEffects_EventHandler : MonoBehaviour
		{
			public UIAudioEffects Owner;
		}



		private class UIAudioEffects_Submit : UIAudioEffects_EventHandler, ISubmitHandler
		{
			// Same as OnClick, or else pressing Enter on selected button won't play.
			public void OnSubmit(BaseEventData eventData) => Owner.PlayAudio(Owner.SubmitAudio);
		}

		private class UIAudioEffects_PointerClick : UIAudioEffects_EventHandler, IPointerClickHandler
		{
			public void OnPointerClick(PointerEventData eventData) => Owner.PlayAudio(Owner.PointerClickAudio);
		}

		private class UIAudioEffects_PointerDown : UIAudioEffects_EventHandler, IPointerDownHandler
		{
			public void OnPointerDown(PointerEventData eventData) => Owner.PlayAudio(Owner.PointerDownAudio);
		}

		private class UIAudioEffects_PointerUp : UIAudioEffects_EventHandler, IPointerUpHandler
		{
			public void OnPointerUp(PointerEventData eventData) => Owner.PlayAudio(Owner.PointerUpAudio);
		}

		private class UIAudioEffects_PointerEnter : UIAudioEffects_EventHandler, IPointerEnterHandler
		{
			public void OnPointerEnter(PointerEventData eventData) => Owner.PlayAudio(Owner.PointerEnterAudio);
		}

		private class UIAudioEffects_PointerExit : UIAudioEffects_EventHandler, IPointerExitHandler
		{
			public void OnPointerExit(PointerEventData eventData) => Owner.PlayAudio(Owner.PointerExitAudio);
		}

		private class UIAudioEffects_Select : UIAudioEffects_EventHandler, ISelectHandler
		{
			public void OnSelect(BaseEventData eventData) => Owner.PlayAudio(Owner.SelectAudio);
		}

		private class UIAudioEffects_Deselect : UIAudioEffects_EventHandler, IDeselectHandler
		{
			public void OnDeselect(BaseEventData eventData) => Owner.PlayAudio(Owner.DeselectAudio);
		}

		#endregion
	}

}
