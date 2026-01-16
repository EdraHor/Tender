using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioSourcePool : MonoBehaviour
{
    private ObjectPool<AudioSource> _pool;
    private readonly Dictionary<AudioSource, Coroutine> _activeCoroutines = new Dictionary<AudioSource, Coroutine>();
    
    private void Awake()
    {
        _pool = new ObjectPool<AudioSource>(
            parent: transform,
            createFunc: CreateAudioSource,
            onGet: null,
            onRelease: OnReleaseAudioSource
        );
        
        _pool.Prewarm(40);
    }
    
    private AudioSource CreateAudioSource()
    {
        GameObject go = new GameObject("AudioSource");
        AudioSource source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.loop = false;
        return source;
    }
    
    private void OnReleaseAudioSource(AudioSource source)
    {
        source.Stop();
        source.clip = null;
        source.pitch = 1f;
        source.volume = 1f;
    }
    
    public AudioSource Play(AudioClip clip, Vector3 position, float pitch = 1f, float volume = 1f)
    {
        if (clip == null) return null;
        
        AudioSource source = _pool.Get();
        source.transform.position = position;
        source.clip = clip;
        source.pitch = pitch;
        source.volume = volume;
        source.Play();
        
        Coroutine routine = StartCoroutine(AutoRelease(source, clip.length / pitch));
        _activeCoroutines[source] = routine;
        
        return source;
    }
    
    public AudioSource PlayScheduled(AudioClip clip, Vector3 position, double scheduledTime, float pitch = 1f, float volume = 1f)
    {
        if (clip == null) return null;
        
        AudioSource source = _pool.Get();
        source.transform.position = position;
        source.clip = clip;
        source.pitch = pitch;
        source.volume = volume;
        source.PlayScheduled(scheduledTime);
        
        double duration = clip.length / pitch;
        double releaseTime = scheduledTime + duration;
        
        Coroutine routine = StartCoroutine(AutoReleaseScheduled(source, releaseTime));
        _activeCoroutines[source] = routine;
        
        return source;
    }
    
    public void Stop(AudioSource source, float fadeTime = 0.1f)
    {
        if (source == null) return;
        
        if (_activeCoroutines.TryGetValue(source, out Coroutine routine))
        {
            StopCoroutine(routine);
            _activeCoroutines.Remove(source);
        }
        
        StartCoroutine(FadeOutAndRelease(source, fadeTime));
    }
    
    private IEnumerator AutoRelease(AudioSource source, float duration)
    {
        yield return new WaitForSeconds(duration + 0.05f);
        
        _activeCoroutines.Remove(source);
        _pool.Release(source);
    }
    
    private IEnumerator AutoReleaseScheduled(AudioSource source, double releaseTime)
    {
        while (AudioSettings.dspTime < releaseTime + 0.05)
        {
            yield return null;
        }
        
        _activeCoroutines.Remove(source);
        _pool.Release(source);
    }
    
    private IEnumerator FadeOutAndRelease(AudioSource source, float fadeTime)
    {
        float startVolume = source.volume;
        float elapsed = 0f;
        
        while (elapsed < fadeTime && source.isPlaying)
        {
            elapsed += Time.deltaTime;
            source.volume = Mathf.Lerp(startVolume, 0f, elapsed / fadeTime);
            yield return null;
        }
        
        _pool.Release(source);
    }
    
    private void OnDestroy()
    {
        _pool?.Clear();
    }
}