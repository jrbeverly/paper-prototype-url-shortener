using System.Globalization;

namespace ControlPlane.Api.Services;

public sealed record CsvLinkRecord
{
    public required string Domain { get; init; }
    public string? Slug { get; init; }
    public required string Destination { get; init; }
    public string RedirectType { get; init; } = "302";
}

public static class CsvParser
{
    /// <summary>
    /// Parses a CSV stream with header row. Expected columns: domain, slug, destination, redirect_type.
    /// Returns one record per data row and a list of row-level errors.
    /// </summary>
    public static (List<CsvLinkRecord> Records, List<string> Errors) ParseLinkCsv(Stream stream)
    {
        var records = new List<CsvLinkRecord>();
        var errors = new List<string>();

        using var reader = new StreamReader(stream);
        string? headerLine = reader.ReadLine();
        if (headerLine is null)
        {
            errors.Add("CSV file is empty");
            return (records, errors);
        }

        var headers = ParseLine(headerLine);
        if (headers.Count == 0)
        {
            errors.Add("CSV file has no header row");
            return (records, errors);
        }

        var columnMap = MapColumns(headers);
        var missingRequired = new List<string>();
        if (!columnMap.ContainsKey("domain")) missingRequired.Add("domain");
        if (!columnMap.ContainsKey("destination")) missingRequired.Add("destination");
        if (missingRequired.Count > 0)
        {
            errors.Add($"CSV is missing required columns: {string.Join(", ", missingRequired)}");
            return (records, errors);
        }

        int rowNumber = 1; // header is row 0
        while (!reader.EndOfStream)
        {
            rowNumber++;
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var fields = ParseLine(line);
            if (fields.Count == 0) continue;

            var record = new CsvLinkRecord
            {
                Domain = GetField(fields, columnMap, "domain")?.Trim() ?? "",
                Slug = GetField(fields, columnMap, "slug")?.Trim(),
                Destination = GetField(fields, columnMap, "destination")?.Trim() ?? "",
                RedirectType = GetField(fields, columnMap, "redirect_type")?.Trim() ?? "302"
            };

            if (string.IsNullOrEmpty(record.Domain))
            {
                errors.Add($"Row {rowNumber}: domain is required");
                continue;
            }
            if (string.IsNullOrEmpty(record.Destination))
            {
                errors.Add($"Row {rowNumber}: destination is required");
                continue;
            }

            records.Add(record);
        }

        return (records, errors);
    }

    private static List<string> ParseLine(string? line)
    {
        var fields = new List<string>();
        if (string.IsNullOrEmpty(line))
            return fields;

        bool inQuotes = false;
        int start = 0;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                var field = line[start..i].Trim();
                if (field.StartsWith('"') && field.EndsWith('"') && field.Length >= 2)
                    field = field[1..^1].Replace("\"\"", "\"");
                fields.Add(field);
                start = i + 1;
            }
        }

        var last = line[start..].Trim();
        if (last.StartsWith('"') && last.EndsWith('"') && last.Length >= 2)
            last = last[1..^1].Replace("\"\"", "\"");
        fields.Add(last);

        return fields;
    }

    private static Dictionary<string, int> MapColumns(List<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Count; i++)
        {
            var header = headers[i].Trim().ToLowerInvariant();
            if (!map.ContainsKey(header))
                map[header] = i;
        }
        return map;
    }

    private static string? GetField(List<string> fields, Dictionary<string, int> columnMap, string column)
    {
        if (columnMap.TryGetValue(column, out var index) && index < fields.Count)
        {
            var value = fields[index];
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        return null;
    }

    /// <summary>
    /// Escapes a value for CSV output (wraps in quotes if it contains commas, quotes, or newlines).
    /// </summary>
    public static string EscapeCsvField(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
