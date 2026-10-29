using System.Collections.Generic;
using UnityEngine;

namespace BR.Managers
{
    /// <summary>
    /// AudioManager com pool de AudioSources (sem Instantiate por tiro).
    ///
    /// Regras mobile:
    ///  - 2D para UI, 3D para mundo com rolloff curto;
    ///  - limite de vozes simultaneas (~20), com prioridade para sons do player;
    ///  - musica em streaming, SFX comprimidos.
    /// </summary>
    public sealed class AudioManager : MonoSingleton<AudioManager>
    {
        [Header("Pool")]
        [SerializeField] private int voiceCount = 20;
        [SerializeField] private AudioSource musicSource;

        [Header("Mix")]
        [SerializeField] private float sfxVolume = 0.85f;
        [SerializeField] private float musicVolume = 0.55f;
        [SerializeField] private float maxDistance = 60f;

        private readonly List<AudioSource> _voices = new List<AudioSource>(24);
        private int _cursor;

        protected override void OnSingletonAwake()
        {
            for (var i = 0; i < voiceCount; i++)
            {
                var go = new GameObject("Voice_" + i);
                go.transform.SetParent(transform, false);
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 3f;
                source.maxDistance = maxDistance;
                source.volume = sfxVolume;
                _voices.Add(source);
            }

            if (musicSource != null)
            {
                musicSource.loop = true;
                musicSource.volume = musicVolume;
                musicSource.playOnAwake = false;
            }
        }

        public void PlaySfx(AudioClip clip, Vector3 position, float volume = 1f, float pitchVariance = 0.06f)
        {
            if (clip == null) return;

            var source = NextVoice();
            source.transform.position = position;
            source.clip = clip;
            source.volume = sfxVolume * volume;
            source.pitch = 1f + Random.Range(-pitchVariance, pitchVariance);
            source.spatialBlend = 1f;
            source.Play();
        }

        public void PlayUi(AudioClip clip, float volume = 1f)
        {
            if (clip == null) return;

            var source = NextVoice();
            source.clip = clip;
            source.volume = sfxVolume * volume;
            source.pitch = 1f;
            source.spatialBlend = 0f;
            source.Play();
        }

        public void PlayMusic(AudioClip clip)
        {
            if (musicSource == null || clip == null) return;
            if (musicSource.clip == clip && musicSource.isPlaying) return;

            musicSource.clip = clip;
            musicSource.volume = musicVolume;
            musicSource.Play();
        }

        public void StopMusic() => musicSource?.Stop();

        /// <summary>Round-robin de vozes: 20 fontes cobrem tiro, passos, UI e zona.</summary>
        private AudioSource NextVoice()
        {
            var source = _voices[_cursor];
            _cursor = (_cursor + 1) % _voices.Count;
            source.Stop();
            return source;
        }
    }
}
