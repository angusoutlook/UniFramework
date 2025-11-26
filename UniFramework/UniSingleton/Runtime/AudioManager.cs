using UnityEngine;

namespace UniFramework.Singleton
{
    /// <summary>
    /// 简单音频管理单例
    /// </summary>
    public sealed class AudioManager : SingletonInstance<AudioManager>, ISingleton
    {
        private GameObject _root;
        private AudioSource _musicSource;
        private AudioSource _sfxSource;

        private float _musicVolume = 1f;
        private float _sfxVolume = 1f;

        public float MusicVolume
        {
            get => _musicVolume;
            set
            {
                _musicVolume = Mathf.Clamp01(value);
                if (_musicSource != null)
                    _musicSource.volume = _musicVolume;
            }
        }

        public float SfxVolume
        {
            get => _sfxVolume;
            set
            {
                _sfxVolume = Mathf.Clamp01(value);
                if (_sfxSource != null)
                    _sfxSource.volume = _sfxVolume;
            }
        }

        public void OnCreate(object createParam)
        {
            _root = new GameObject("[AudioManager]");
            Object.DontDestroyOnLoad(_root);

            _musicSource = _root.AddComponent<AudioSource>();
            _musicSource.playOnAwake = false;
            _musicSource.loop = true;
            _musicSource.volume = _musicVolume;

            _sfxSource = _root.AddComponent<AudioSource>();
            _sfxSource.playOnAwake = false;
            _sfxSource.loop = false;
            _sfxSource.volume = _sfxVolume;
        }

        public void OnUpdate()
        {
            // 当前简单音频管理不需要逐帧逻辑
        }

        public void OnDestroy()
        {
            if (_musicSource != null)
            {
                _musicSource.Stop();
            }

            if (_sfxSource != null)
            {
                _sfxSource.Stop();
            }

            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
            }

            DestroyInstance();
        }

        /// <summary>
        /// 播放背景音乐
        /// </summary>
        public void PlayMusic(AudioClip clip, bool loop = true)
        {
            if (clip == null || _musicSource == null)
                return;

            _musicSource.clip = clip;
            _musicSource.loop = loop;
            _musicSource.volume = _musicVolume;
            _musicSource.Play();
        }

        /// <summary>
        /// 停止背景音乐
        /// </summary>
        public void StopMusic()
        {
            if (_musicSource != null)
                _musicSource.Stop();
        }

        /// <summary>
        /// 播放一次性音效
        /// </summary>
        public void PlaySfx(AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null || _sfxSource == null)
                return;

            _sfxSource.PlayOneShot(clip, Mathf.Clamp01(volumeScale) * _sfxVolume);
        }
    }
}


