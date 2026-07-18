using System;
using System.Collections.Generic;
using UnityEngine;

public class ObjectPool<T> where T : Component
{
    private readonly Queue<T> _available = new Queue<T>();
    private readonly HashSet<T> _inUse = new HashSet<T>();
    private readonly Transform _parent;
    private readonly Func<T> _createFunc;
    private readonly Action<T> _onGet;
    private readonly Action<T> _onRelease;
    private readonly Action<T> _onDestroy;
    
    public int CountAvailable => _available.Count;
    public int CountInUse => _inUse.Count;
    public int CountTotal => CountAvailable + CountInUse;
    
    public ObjectPool(
        Transform parent, 
        Func<T> createFunc, 
        Action<T> onGet = null, 
        Action<T> onRelease = null,
        Action<T> onDestroy = null)
    {
        _parent = parent;
        _createFunc = createFunc;
        _onGet = onGet;
        _onRelease = onRelease;
        _onDestroy = onDestroy;
    }
    
    public T Get()
    {
        T item;
        
        if (_available.Count > 0)
        {
            item = _available.Dequeue();
        }
        else
        {
            item = _createFunc();
            if (_parent != null)
                item.transform.SetParent(_parent);
        }
        
        _inUse.Add(item);
        _onGet?.Invoke(item);
        
        return item;
    }
    
    public void Release(T item)
    {
        if (!_inUse.Remove(item))
            return;
        
        _onRelease?.Invoke(item);
        _available.Enqueue(item);
    }
    
    public void Prewarm(int count)
    {
        for (int i = 0; i < count; i++)
        {
            T item = _createFunc();
            if (_parent != null)
                item.transform.SetParent(_parent);
            _onRelease?.Invoke(item);
            _available.Enqueue(item);
        }
    }
    
    public void Clear()
    {
        foreach (var item in _available)
        {
            _onDestroy?.Invoke(item);
            if (item != null)
                UnityEngine.Object.Destroy(item.gameObject);
        }
        _available.Clear();
        
        foreach (var item in _inUse)
        {
            _onDestroy?.Invoke(item);
            if (item != null)
                UnityEngine.Object.Destroy(item.gameObject);
        }
        _inUse.Clear();
    }
}