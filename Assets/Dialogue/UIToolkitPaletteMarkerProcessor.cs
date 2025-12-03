using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Yarn.Markup;
using Yarn.Unity;

#nullable enable

/// <summary>
/// Обработчик маркеров для UI Toolkit с поддержкой MarkupPalette.
/// Преобразует Yarn маркеры [happy]текст[/happy] в UI Toolkit теги.
/// </summary>
public sealed class UIToolkitPaletteMarkerProcessor : ReplacementMarkupHandler
{
    [Tooltip("Палитра стилей для маркеров")]
    [SerializeField] private MarkupPalette? palette;
    
    [Tooltip("LineProvider для регистрации (автопоиск если не указан)")]
    [SerializeField] private LineProviderBehaviour? lineProvider;
    
    public override List<LineParser.MarkupDiagnostic> ProcessReplacementMarker(
        MarkupAttribute marker, 
        StringBuilder childBuilder, 
        List<MarkupAttribute> childAttributes, 
        string localeCode)
    {
        if (palette == null)
        {
            return new List<LineParser.MarkupDiagnostic>
            {
                new LineParser.MarkupDiagnostic($"Palette not set for marker [{marker.Name}]")
            };
        }
        
        if (palette.PaletteForMarker(marker.Name, out var format))
        {
            childBuilder.Insert(0, format.Start);
            childBuilder.Append(format.End);
            
            if (format.MarkerOffset != 0)
            {
                for (int i = 0; i < childAttributes.Count; i++)
                {
                    childAttributes[i] = childAttributes[i].Shift(format.MarkerOffset);
                }
            }
            
            return ReplacementMarkupHandler.NoDiagnostics;
        }
        
        return new List<LineParser.MarkupDiagnostic>
        {
            new LineParser.MarkupDiagnostic($"Marker [{marker.Name}] not found in palette")
        };
    }
    
    private void Start()
    {
        if (palette == null || palette.BasicMarkers.Count == 0 && palette.CustomMarkers.Count == 0)
            return;
        
        if (lineProvider == null)
        {
            var runner = FindAnyObjectByType<DialogueRunner>();
            if (runner != null)
                lineProvider = (LineProviderBehaviour)runner.LineProvider;
        }
        
        if (lineProvider == null)
        {
            Debug.LogError("[UIToolkitPaletteMarkerProcessor] LineProvider not found!");
            return;
        }
        
        foreach (var marker in palette.BasicMarkers)
            lineProvider.RegisterMarkerProcessor(marker.Marker, this);
        
        foreach (var marker in palette.CustomMarkers)
            lineProvider.RegisterMarkerProcessor(marker.Marker, this);
    }
}