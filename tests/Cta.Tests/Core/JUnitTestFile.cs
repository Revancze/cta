using System.Globalization;
using System.Xml;

namespace Cta.Tests.Core;

internal static class JUnitTestFile
{
    public static void Write(string path, int results, int outputSize)
    {
        var output = new string('x', outputSize);
        using var writer = XmlWriter.Create(path);
        writer.WriteStartElement("testsuites");
        writer.WriteStartElement("testsuite");
        writer.WriteAttributeString("name", "suite");
        writer.WriteAttributeString("tests", results.ToString(CultureInfo.InvariantCulture));

        for (var i = 0; i < results; i++)
        {
            writer.WriteStartElement("testcase");
            writer.WriteAttributeString(
                "classname",
                string.Create(CultureInfo.InvariantCulture, $"Sample.Class{i % 100}")
            );
            writer.WriteAttributeString(
                "name",
                string.Create(CultureInfo.InvariantCulture, $"Test{i}")
            );
            writer.WriteAttributeString("time", "0.001");
            if (outputSize > 0)
            {
                writer.WriteElementString("system-out", output);
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
    }
}
