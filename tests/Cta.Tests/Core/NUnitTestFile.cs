using System.Globalization;
using System.Xml;

namespace Cta.Tests.Core;

internal static class NUnitTestFile
{
    public static void Write(string path, int results, int outputSize)
    {
        var output = new string('x', outputSize);
        using var writer = XmlWriter.Create(path);
        writer.WriteStartElement("test-run");
        writer.WriteAttributeString("total", results.ToString(CultureInfo.InvariantCulture));
        writer.WriteStartElement("test-suite");
        writer.WriteAttributeString("type", "Assembly");
        writer.WriteAttributeString("name", "Sample.dll");

        for (var i = 0; i < results; i++)
        {
            writer.WriteStartElement("test-case");
            writer.WriteAttributeString(
                "fullname",
                string.Create(CultureInfo.InvariantCulture, $"Sample.Class{i % 100}.Test{i}")
            );
            writer.WriteAttributeString("result", "Passed");
            writer.WriteAttributeString("duration", "0.001");
            if (outputSize > 0)
            {
                writer.WriteElementString("output", output);
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
    }
}
