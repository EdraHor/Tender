// SaveData.cs  
using System;
using System.Collections.Generic;

[Serializable]
public class SettingsData
{
    public int graphicsPreset = 2;
    public float shadowDistance = 150f;
    public int shadowResolution = 2;
    public int shadowCascades = 2;
    public bool vSync = true;
    public float masterVolume = 1f;
    public float musicVolume = 1f;
    public float sfxVolume = 1f;
    public float voiceVolume = 1f;
}

[Serializable]
public class SaveMetadata
{
    public string saveDate;
    public int chapter;
    public float playTime;
}

[Serializable]
public class SaveData
{
    public SaveMetadata metadata = new SaveMetadata();
    public int currentChapter;
    public YarnVariables yarnVariables;
}

[Serializable]
public class YarnVariables
{
    public string[] floatKeys, stringKeys, boolKeys;
    public float[] floatValues;
    public string[] stringValues;
    public bool[] boolValues;
    
    public static YarnVariables FromDictionaries(
        Dictionary<string, float> floats,
        Dictionary<string, string> strings,
        Dictionary<string, bool> bools)
    {
        var v = new YarnVariables();
        
        if (floats?.Count > 0)
        {
            v.floatKeys = new string[floats.Count];
            v.floatValues = new float[floats.Count];
            int i = 0;
            foreach (var kvp in floats)
            {
                v.floatKeys[i] = kvp.Key;
                v.floatValues[i] = kvp.Value;
                i++;
            }
        }
        
        if (strings?.Count > 0)
        {
            v.stringKeys = new string[strings.Count];
            v.stringValues = new string[strings.Count];
            int i = 0;
            foreach (var kvp in strings)
            {
                v.stringKeys[i] = kvp.Key;
                v.stringValues[i] = kvp.Value;
                i++;
            }
        }
        
        if (bools?.Count > 0)
        {
            v.boolKeys = new string[bools.Count];
            v.boolValues = new bool[bools.Count];
            int i = 0;
            foreach (var kvp in bools)
            {
                v.boolKeys[i] = kvp.Key;
                v.boolValues[i] = kvp.Value;
                i++;
            }
        }
        
        return v;
    }
    
    public void ToDictionaries(
        out Dictionary<string, float> floats,
        out Dictionary<string, string> strings,
        out Dictionary<string, bool> bools)
    {
        floats = new Dictionary<string, float>();
        if (floatKeys != null)
            for (int i = 0; i < floatKeys.Length; i++)
                floats[floatKeys[i]] = floatValues[i];
            
        strings = new Dictionary<string, string>();
        if (stringKeys != null)
            for (int i = 0; i < stringKeys.Length; i++)
                strings[stringKeys[i]] = stringValues[i];
            
        bools = new Dictionary<string, bool>();
        if (boolKeys != null)
            for (int i = 0; i < boolKeys.Length; i++)
                bools[boolKeys[i]] = boolValues[i];
    }
}