

using DevLocker.Audio;
using DevLocker.Audio.Conductors;
using UnityEngine;

public class SampleImpactTester : MonoBehaviour
{
	public AudioPlayer AudioPlayer;

	private void Awake()
	{
		OnImpactForceChanged(0);
	}

	public void OnImpactForceChanged(int force)
	{
		// Create context that the audio asset filters will read.
		var audioContext = DictionaryContext.Create("ImpactForce", force * 4);
		AudioPlayer.ConductorsFilterContext = audioContext;
	}
}