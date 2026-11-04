using System.Globalization;
using System.Text;

namespace InventoryManagement.Application.Common;

/// <summary>
/// Minimal RFC 4180 CSV builder for report exports: fields containing a
/// comma, quote or line break are quoted, embedded quotes doubled. Numbers
/// and dates are written with the invariant culture so the file opens the
/// same in Excel regardless of the PC's regional settings.
///
/// Fields starting with =, +, - or @ are prefixed with an apostrophe so a
/// product name like "=HYPERLINK(...)" is shown as text in Excel instead of
/// being run as a formula (CSV/formula injection).
/// </summary>
public sealed class CsvWriter
{
    private readonly StringBuilder _builder = new();

    public CsvWriter AddRow(params object?[] fields)
    {
        _builder.AppendJoin(',', fields.Select(Format)).Append("\r\n");
        return this;
    }

    public CsvWriter AddBlankRow()
    {
        _builder.Append("\r\n");
        return this;
    }

    public override string ToString() => _builder.ToString();

    private static string Format(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            DateTimeOffset dto => dto.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            decimal d => d.ToString("0.00##", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

        if (value is string && text.Length > 0 && text[0] is '=' or '+' or '-' or '@')
        {
            text = "'" + text;
        }

        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? "\"" + text.Replace("\"", "\"\"") + "\""
            : text;
    }
}
