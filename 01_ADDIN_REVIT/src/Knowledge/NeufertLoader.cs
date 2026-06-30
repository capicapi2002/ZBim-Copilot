#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ZBIMCopilot.Knowledge
{
    public class NeufertLoader
    {
        private readonly List<NeufertEntry> _entries;

        public NeufertLoader(string jsonFolderPath)
        {
            _entries = new List<NeufertEntry>();
            if (!Directory.Exists(jsonFolderPath))
                throw new DirectoryNotFoundException($"Carpeta de Neufert no encontrada: {jsonFolderPath}");

            var files = Directory.GetFiles(jsonFolderPath, "neufert_parte_*.json");
            if (files.Length == 0)
                throw new FileNotFoundException($"No se encontraron archivos neufert_parte_*.json en {jsonFolderPath}");

            foreach (var file in files)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var list = JsonSerializer.Deserialize<List<NeufertEntry>>(json);
                    if (list != null)
                        _entries.AddRange(list);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error cargando {file}: {ex.Message}");
                }
            }
        }

        public List<NeufertEntry> GetEntries(string domain, string element)
        {
            return _entries
                .Where(e => string.Equals(e.Domain, domain, StringComparison.OrdinalIgnoreCase)
                         && string.Equals(e.Element, element, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public List<NeufertSpace> GetSpacesFor(string elementKeyword)
        {
            var entries = SearchByElement(elementKeyword);
            var spaces = new List<NeufertSpace>();
            foreach (var entry in entries)
            {
                double width = 0, length = 0;
                if (entry.TypicalValue is JsonElement jsonVal)
                {
                    if (jsonVal.ValueKind == JsonValueKind.Number)
                    {
                        double val = jsonVal.GetDouble();
                        if (entry.ParameterOrSubject != null && entry.ParameterOrSubject.IndexOf("ancho", StringComparison.OrdinalIgnoreCase) >= 0)
                            width = val;
                        else if (entry.ParameterOrSubject != null && (entry.ParameterOrSubject.IndexOf("largo", StringComparison.OrdinalIgnoreCase) >= 0 || entry.ParameterOrSubject.IndexOf("longitud", StringComparison.OrdinalIgnoreCase) >= 0))
                            length = val;
                        else
                            width = val;
                    }
                    else if (jsonVal.ValueKind == JsonValueKind.String && double.TryParse(jsonVal.GetString(), out double parsed))
                    {
                        width = parsed;
                    }
                }
                if (width == 0 && entry.MinValue is JsonElement minElem && minElem.ValueKind == JsonValueKind.Number)
                    width = minElem.GetDouble();
                if (length == 0 && entry.MaxValue is JsonElement maxElem && maxElem.ValueKind == JsonValueKind.Number)
                    length = maxElem.GetDouble();

                spaces.Add(new NeufertSpace
                {
                    Nombre = entry.Element,
                    AreaMinM2 = width * length,
                    Zona = entry.Domain ?? "General",
                    AdyacenciasTipicas = entry.Tags?.ToArray() ?? Array.Empty<string>(),
                    Dimensiones = new Dictionary<string, double>
                    {
                        ["ancho"] = width,
                        ["largo"] = length
                    }
                });
            }
            return spaces;
        }

        public List<NeufertEntry> SearchByElement(string keyword)
        {
            return _entries
                .Where(e => e.Element != null && e.Element.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

        public List<NeufertEntry> SearchByKeyword(string keyword)
        {
            return _entries.Where(e =>
                (e.Element != null && e.Element.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (e.RuleDescription != null && e.RuleDescription.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (e.Tags != null && e.Tags.Any(t => t.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0))
            ).ToList();
        }

        public double? GetTypicalValue(string domain, string element, string parameterSubstring)
        {
            var entry = _entries.FirstOrDefault(e =>
                string.Equals(e.Domain, domain, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Element, element, StringComparison.OrdinalIgnoreCase)
                && e.ParameterOrSubject != null
                && e.ParameterOrSubject.IndexOf(parameterSubstring, StringComparison.OrdinalIgnoreCase) >= 0
                && e.TypicalValue != null);

            if (entry?.TypicalValue is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.Number)
                    return jsonElement.GetDouble();
                if (jsonElement.ValueKind == JsonValueKind.String && double.TryParse(jsonElement.GetString(), out double val))
                    return val;
            }
            return null;
        }
    }

    public class NeufertEntry
    {
        public string DataType { get; set; }
        public string Domain { get; set; }
        public string Element { get; set; }
        public string ParameterOrSubject { get; set; }
        public string ValueType { get; set; }
        public object MinValue { get; set; }
        public object MaxValue { get; set; }
        public object TypicalValue { get; set; }
        public string Unit { get; set; }
        public string RuleDescription { get; set; }
        public string FormulaExpression { get; set; }
        public string FormulaVariables { get; set; }
        public object TabularData { get; set; }
        public object SpatialRelationships { get; set; }
        public string Condition { get; set; }
        public string NormativeReference { get; set; }
        public string ImportanceLevel { get; set; }
        public string SourceReference { get; set; }
        public List<string> Tags { get; set; }
    }

    public struct NeufertSpace
    {
        public string Nombre { get; set; }
        public double AreaMinM2 { get; set; }
        public string Zona { get; set; }
        public string[] AdyacenciasTipicas { get; set; }
        public Dictionary<string, double> Dimensiones { get; set; }
    }
}