using System.IO;
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

    public static async Task SaveSmartAsync(string path, IEnumerable<SmartRecord> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Name,Price,Image,Link,Rating,Stock,Source");

        foreach (var item in items)
        {
            sb.Append(Escape(item.Name)).Append(',')
              .Append(Escape(item.Price)).Append(',')
              .Append(Escape(item.Image)).Append(',')
              .Append(Escape(item.Link)).Append(',')
              .Append(Escape(item.Rating)).Append(',')
              .Append(Escape(item.Stock)).Append(',')
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
