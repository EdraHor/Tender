// SaveVariableStorage.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using Yarn.Unity;

public class SaveVariableStorage : VariableStorageBehaviour
{
    private Dictionary<string, object> variables = new Dictionary<string, object>();
    private SaveData currentData = new SaveData();
    
    public int CurrentChapter { get => currentData.currentChapter; set => currentData.currentChapter = value; }
    
    public override void SetValue(string variableName, string value)
    {
        variables[variableName] = value;
    }
    
    public override void SetValue(string variableName, float value)
    {
        variables[variableName] = value;
    }
    
    public override void SetValue(string variableName, bool value)
    {
        variables[variableName] = value;
    }
    
    public override bool TryGetValue<T>(string variableName, out T result)
    {
        if (variables.TryGetValue(variableName, out var value) && value is T typed)
        {
            result = typed;
            return true;
        }
        
        if (this.Program != null && this.Program.TryGetInitialValue<T>(variableName, out result))
            return true;
        
        result = default;
        return false;
    }
    
    public override void Clear()
    {
        variables.Clear();
        currentData = new SaveData();
    }
    
    public override bool Contains(string variableName)
    {
        return variables.ContainsKey(variableName);
    }
    
    public SaveData GetSaveData()
    {
        currentData.metadata.chapter = currentData.currentChapter;
        
        var floats = new Dictionary<string, float>();
        var strings = new Dictionary<string, string>();
        var bools = new Dictionary<string, bool>();
        
        foreach (var kvp in variables)
        {
            if (kvp.Value is float f) floats[kvp.Key] = f;
            else if (kvp.Value is string s) strings[kvp.Key] = s;
            else if (kvp.Value is bool b) bools[kvp.Key] = b;
        }
        
        currentData.yarnVariables = YarnVariables.FromDictionaries(floats, strings, bools);
        return currentData;
    }
    
    public void LoadSaveData(SaveData data)
    {
        currentData = data;
        variables.Clear();
        
        if (data.yarnVariables != null)
        {
            data.yarnVariables.ToDictionaries(out var floats, out var strings, out var bools);
            
            foreach (var kvp in floats) variables[kvp.Key] = kvp.Value;
            foreach (var kvp in strings) variables[kvp.Key] = kvp.Value;
            foreach (var kvp in bools) variables[kvp.Key] = kvp.Value;
        }
    }
    
    public override (Dictionary<string, float>, Dictionary<string, string>, Dictionary<string, bool>) GetAllVariables()
    {
        var floats = new Dictionary<string, float>();
        var strings = new Dictionary<string, string>();
        var bools = new Dictionary<string, bool>();
        
        foreach (var kvp in variables)
        {
            if (kvp.Value is float f) floats[kvp.Key] = f;
            else if (kvp.Value is string s) strings[kvp.Key] = s;
            else if (kvp.Value is bool b) bools[kvp.Key] = b;
        }
        
        return (floats, strings, bools);
    }
    
    public override void SetAllVariables(Dictionary<string, float> floats, Dictionary<string, string> strings, Dictionary<string, bool> bools, bool clear = true)
    {
        if (clear) variables.Clear();
        
        foreach (var kvp in floats) variables[kvp.Key] = kvp.Value;
        foreach (var kvp in strings) variables[kvp.Key] = kvp.Value;
        foreach (var kvp in bools) variables[kvp.Key] = kvp.Value;
    }
}