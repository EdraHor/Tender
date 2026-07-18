using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

public static class DialogueUtils
{
    /// <summary>
    /// Специальный маркер в тексте диалога
    /// </summary>
    public struct SpecialMarker
    {
        public enum MarkerType
        {
            Wait,    // [wait=1.5]
            Click    // [click]
        }
        
        public MarkerType Type;
        public int Position;      // Позиция в чистом тексте (без тегов)
        public float WaitTime;    // Для Type.Wait
    }
    
    /// <summary>
    /// Часть текста - либо обычный текст, либо тег форматирования
    /// </summary>
    public struct TextPart
    {
        public string Content;
        public bool IsTag;  // true = UI Toolkit тег (<b>, <color>), false = обычный текст
    }
    
    /// <summary>
    /// Извлекает имя персонажа из текста вида "Имя: текст" или "{$var}: текст"
    /// </summary>
    public static (string characterName, string textWithoutName) ExtractCharacterName(string text)
    {
        if (string.IsNullOrEmpty(text))
            return (string.Empty, text);
        
        var match = Regex.Match(text, @"^(.+?):\s*(.*)$");
        if (match.Success)
        {
            string name = match.Groups[1].Value.Trim();
            string content = match.Groups[2].Value;
            
            name = name.Replace("{$", "").Replace("}", "");
            
            return (name, content);
        }
        
        return (string.Empty, text);
    }
    
    /// <summary>
    /// Удаляет Yarn маркеры [happy], [/happy] но оставляет [wait=X] и [click]
    /// </summary>
    public static string RemoveYarnMarkers(string text)
    {
        return Regex.Replace(text, @"\[(?!wait=|click\])([^\]]*)\]", "");
    }
    
    /// <summary>
    /// Создаёт финальный текст для отображения - с UI Toolkit тегами, но без маркеров
    /// </summary>
    public static string GetDisplayText(string text)
    {
        // Удалить ВСЕ маркеры в квадратных скобках
        return Regex.Replace(text, @"\[[^\]]*\]", "");
    }
    
    /// <summary>
    /// Получает чистый текст без тегов и маркеров (для истории)
    /// </summary>
    public static string GetCleanText(string text)
    {
        // Удалить ВСЕ маркеры в квадратных скобках
        text = Regex.Replace(text, @"\[[^\]]*\]", "");
        
        // Удалить UI Toolkit теги
        text = Regex.Replace(text, @"<[^>]+>", "");
        
        return text;
    }
    
    /// <summary>
    /// Извлекает все специальные маркеры из текста и возвращает их позиции
    /// </summary>
    public static List<SpecialMarker> ExtractSpecialMarkers(string text)
    {
        var markers = new List<SpecialMarker>();
        int cleanPosition = 0;
        int i = 0;
        
        while (i < text.Length)
        {
            // [wait=X]
            var waitMatch = Regex.Match(text.Substring(i), @"^\[wait=([\d\.]+)\]");
            if (waitMatch.Success)
            {
                if (float.TryParse(waitMatch.Groups[1].Value, 
                    System.Globalization.NumberStyles.Float, 
                    System.Globalization.CultureInfo.InvariantCulture, 
                    out float waitTime))
                {
                    markers.Add(new SpecialMarker
                    {
                        Type = SpecialMarker.MarkerType.Wait,
                        Position = cleanPosition,
                        WaitTime = waitTime
                    });
                }
                i += waitMatch.Length;
                continue;
            }
            
            // [click]
            if (text.Substring(i).StartsWith("[click]"))
            {
                markers.Add(new SpecialMarker
                {
                    Type = SpecialMarker.MarkerType.Click,
                    Position = cleanPosition
                });
                i += 7;
                continue;
            }
            
            // UI Toolkit тег <...>
            if (text[i] == '<')
            {
                int tagEnd = text.IndexOf('>', i);
                if (tagEnd != -1)
                {
                    i = tagEnd + 1;
                    continue;
                }
            }
            
            // Любые другие [...] маркеры - пропустить
            if (text[i] == '[')
            {
                int closeIdx = text.IndexOf(']', i);
                if (closeIdx != -1)
                {
                    i = closeIdx + 1;
                    continue;
                }
            }
            
            cleanPosition++;
            i++;
        }
        
        return markers;
    }
    
    /// <summary>
    /// Парсит текст на части: обычный текст и теги форматирования
    /// </summary>
    public static List<TextPart> ParseRichText(string text)
    {
        var parts = new List<TextPart>();
        int i = 0;
        var currentContent = new StringBuilder();
        
        while (i < text.Length)
        {
            // UI Toolkit тег <...>
            if (text[i] == '<')
            {
                if (currentContent.Length > 0)
                {
                    parts.Add(new TextPart { Content = currentContent.ToString(), IsTag = false });
                    currentContent.Clear();
                }
                
                int tagStart = i;
                while (i < text.Length && text[i] != '>')
                    i++;
                if (i < text.Length)
                    i++;
                
                parts.Add(new TextPart 
                { 
                    Content = text.Substring(tagStart, i - tagStart), 
                    IsTag = true 
                });
                continue;
            }
            
            // Специальные маркеры [wait=X] или [click] - пропускаем
            if (text[i] == '[')
            {
                var substring = text.Substring(i);
                var waitMatch = Regex.Match(substring, @"^\[wait=[\d\.]+\]");
                if (waitMatch.Success)
                {
                    i += waitMatch.Length;
                    continue;
                }
                
                if (substring.StartsWith("[click]"))
                {
                    i += 7;
                    continue;
                }
                
                // Другие [...] маркеры - пропустить
                int closeIdx = text.IndexOf(']', i);
                if (closeIdx != -1)
                {
                    i = closeIdx + 1;
                    continue;
                }
            }
            
            currentContent.Append(text[i]);
            i++;
        }
        
        if (currentContent.Length > 0)
            parts.Add(new TextPart { Content = currentContent.ToString(), IsTag = false });
        
        return parts;
    }
}