using UnityEngine;

/// <summary>
/// Example custom audio filter that crushes the bits, producing low-quality audio.
/// Credits to Game Dev Buddies: https://www.youtube.com/watch?v=ih39x1psPQY
/// </summary>
public class BitCrushFilter : MonoBehaviour
{
	[SerializeField, Range(1, 16)] private int _bitDepth = 16;
	[SerializeField, Range(1, 32)] private int _sampleRateReduction = 1;

	private float[] _lastSamples = new float[2]; // Held sample per channel.
	private int _frameCounter;                   // Persists across buffers.

	private void OnAudioFilterRead(float[] data, int channels)
	{
		if (_lastSamples.Length < channels)
			_lastSamples = new float[channels];

		float quantizationStep = 1f / (1 << _bitDepth);
		int reduction = _sampleRateReduction;

		// Step one frame (all channels) at a time.
		for (int i = 0; i < data.Length; i += channels) {
			bool capture = _frameCounter == 0;

			for (int c = 0; c < channels; c++) {
				int idx = i + c;

				if (capture)
					_lastSamples[c] = data[idx];
				else
					data[idx] = _lastSamples[c];

				data[idx] = Mathf.Round(data[idx] / quantizationStep) * quantizationStep;
			}

			if (++_frameCounter >= reduction)
				_frameCounter = 0;
		}
	}
}