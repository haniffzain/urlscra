using System.Text;
using URLScra.Models;

namespace URLScra.Services;

public static class CsvExporter
{
    public static async Task SaveAsync(string path, IEnumerable<ScrapeItem> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Type,Value,Source");
        foreach (var item in items)
        {
            sb.Append(Escape(item.Type)).Append(',')
              .Append(Escape(item.Value)).Append(',')
              .Append(Escape(item.SourceUrl)).AppendLine();
        }
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static string Escape(string? value)
    {
        value ??= "";
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
