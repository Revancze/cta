using System.Globalization;
using System.Xml;

namespace Cta.Tests.Core;

internal static class TrxTestFile
{
    private const string Namespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public static void Write(
        string path,
        int results,
        int declaredTotal,
        string outcome,
        int outputSize
    )
    {
        var output = new string('x', outputSize);
        using var writer = XmlWriter.Create(path);
        writer.WriteStartElement("TestRun", Namespace);

        writer.WriteStartElement("Results", Namespace);
        for (var i = 0; i < results; i++)
        {
            writer.WriteStartElement("UnitTestResult", Namespace);
            writer.WriteAttributeString("testId", Id(i));
            writer.WriteAttributeString("testName", $"Test{i}");
            writer.WriteAttributeString("outcome", outcome);
            writer.WriteAttributeString("duration", "00:00:00.0010000");
            if (outputSize > 0)
            {
                writer.WriteStartElement("Output", Namespace);
                writer.WriteElementString("StdOut", Namespace, output);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();

        writer.WriteStartElement("TestDefinitions", Namespace);
        for (var i = 0; i < results; i++)
        {
            writer.WriteStartElement("UnitTest", Namespace);
            writer.WriteAttributeString("id", Id(i));
            writer.WriteStartElement("TestMethod", Namespace);
            writer.WriteAttributeString(
                "className",
                string.Create(CultureInfo.InvariantCulture, $"Sample.Class{i % 100}")
            );
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        writer.WriteEndElement();

        writer.WriteStartElement("ResultSummary", Namespace);
        writer.WriteStartElement("Counters", Namespace);
        writer.WriteAttributeString("total", declaredTotal.ToString(CultureInfo.InvariantCulture));
        writer.WriteEndElement();
        writer.WriteEndElement();

        writer.WriteEndElement();
    }

    private static string Id(int index) => new Guid(index, 0, 0, new byte[8]).ToString();
}
