using System.Xml;
using Cta.Contracts;

namespace Cta.Core;

/// <summary>
/// Recognises a report by its root element, never by the file name: JUnit, NUnit and xUnit
/// reports all end with .xml, and anyone can rename a file. Only the start of the file is read,
/// so the cost does not depend on its size (ADR-0007).
/// </summary>
public static class ReportFormatDetector
{
    public static ReportFormat Detect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            using var reader = ReportParsing.CreateReader(path);
            reader.MoveToContent();

            return (reader.LocalName, reader.NamespaceURI) switch
            {
                ("TestRun", ReportParsing.TrxNamespace) => ReportFormat.Trx,
                ("testsuites" or "testsuite", "") => ReportFormat.JUnit,
                ("test-run", "") => ReportFormat.NUnit3,
                ("assemblies", "") => ReportFormat.XUnit2,
                var (name, xmlNamespace) => throw new InvalidDataException(
                    xmlNamespace.Length == 0
                        ? $"Unknown report format: root is <{name}>."
                        : $"Unknown report format: root is <{name}> in namespace '{xmlNamespace}'."
                ),
            };
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException(
                $"File is not an XML report: {exception.Message}",
                exception
            );
        }
    }
}
