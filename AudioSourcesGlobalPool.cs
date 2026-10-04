using System.Collections.Generic;
using UnityEngine;

namespace DevLocker.Audio
{
	/// <summary>
	/// Component that holds a global pool of <see cref="AudioSource"/> for the <see cref="AudioSourcePlayer"/> to use.
	/// It is automatically created on first use of <see cref="AudioSourcePlayer"/> and persists across scene loads.
	///
	/// Audio sources that are returned but still playing and NOT looping will be kept "alive" until they finish playing, then returned to the pool.
	/// </summary>
	[AddComponentMenu("")] // Singleton - hide from the add component menu.
	public class AudioSourcesGlobalPool : MonoBehaviour
	{
		public static AudioSourcesGlobalPool Instance {
			get {
				if (s_IsQuitting)
					return null;
				
				if (s_Instance == null) {
					var go = new GameObject("AudioSourcesGlobalPool");
					s_Instance = go.AddComponent<AudioSourcesGlobalPool>();
					DontDestroyOnLoad(go);
				}

				return s_Instance;
			}
		}

		private static AudioSourcesGlobalPool s_Instance;

		public int Capacity = 20;

		private List<AudioSource> m_AudioSourcesPool = new List<AudioSource>();
		private List<AudioSource> m_PendingReturnSources = new List<AudioSource>();
		
		private static bool s_IsQuitting = false;
		
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ClearStaticsCache()
		{
			s_IsQuitting = false;
		}

		void OnApplicationQuit()
		{
			s_IsQuitting = true;
		}

		/// <summary>
		/// Acquires an audio source from the pool or creates a new one if the pool is empty.
		/// It may have left over changes from last use.
		/// </summary>
		public AudioSource AcquireAudioSource()
		{
			if (m_AudioSourcesPool.Count > 0) {
				var source = m_AudioSourcesPool[m_AudioSourcesPool.Count - 1];
				m_AudioSourcesPool.RemoveAt(m_AudioSourcesPool.Count - 1);

				return source;

			} else {

				var source = new GameObject("PooledAudioSource", typeof(AudioSource)).GetComponent<AudioSource>();
				source.playOnAwake = false;
				source.transform.SetParent(transform);

				return source;
			}
		}

		/// <summary>
		/// Audio sources that are returned but still playing and NOT looping will be kept "alive" until they finish playing, then returned to the pool.
		/// </summary>
		public void ReleaseAudioSource(AudioSource source)
		{
			if (source == null)
				return;

			// Will wait for the source to finish playing before returning it to the pool.
			if (source.isPlaying && !source.loop) {
				m_PendingReturnSources.Add(source);
				return;
			}

			if (m_AudioSourcesPool.Count < Capacity) {
				source.Stop(); // In case it is looping.
				source.playOnAwake = false;
				source.resource = null;
				source.outputAudioMixerGroup = null;
				source.loop = false;
				source.volume = 1.0f;
				source.mute = false;
				source.transform.SetParent(transform);  // Just in case.

				m_AudioSourcesPool.Add(source);

			} else {
				Destroy(source.gameObject);
			}
		}

		void Update()
		{
			for(int i = m_PendingReturnSources.Count - 1; i >= 0; --i) {
				var source = m_PendingReturnSources[i];

				if (!source.isPlaying) {
					m_PendingReturnSources.RemoveAt(i);
					ReleaseAudioSource(source);
				}
			}
		}
	}
}
