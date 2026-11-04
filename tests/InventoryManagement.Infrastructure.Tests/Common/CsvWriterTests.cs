using InventoryManagement.Application.Common;
using Xunit;

namespace InventoryManagement.Infrastructure.Tests.Common;

public class CsvWriterTests
{
    [Fact]
    public void QuotesFieldsThatNeedIt_AndDoublesEmbeddedQuotes()
    {
        var csv = new CsvWriter().AddRow("plain", "has,comma", "has \"quote\"", "multi\nline", null).ToString();

        Assert.Equal("plain,\"has,comma\",\"has \"\"quote\"\"\",\"multi\nline\",\r\n", csv);
    }

    [Fact]
    public void WritesNumbersWithInvariantCulture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            var csv = new CsvWriter().AddRow(1234.5m, 7).ToString();

            Assert.Equal("1234.50,7\r\n", csv);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("=HYPERLINK(\"x\")")]
    [InlineData("+1+1")]
    [InlineData("@SUM(A1)")]
    public void NeutralisesSpreadsheetFormulas_InTextFields(string value)
    {
        var csv = new CsvWriter().AddRow(value).ToString();

        Assert.StartsWith("'", csv.TrimStart('"'));
    }

    [Fact]
    public void NegativeNumbers_AreNotTreatedAsFormulas()
    {
        Assert.Equal("-5.00\r\n", new CsvWriter().AddRow(-5m).ToString());
    }
}
