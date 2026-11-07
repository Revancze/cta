using Cta.Contracts;
using Cta.Core;

namespace Cta.Tests.Core;

public sealed class ReportFormatDetectorTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("cta-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // @relation(CTA-PARSE-007, scope=range_start)
    [Theory]
    [InlineData("xunit.trx", ReportFormat.Trx)]
    [InlineData("nunit.trx", ReportFormat.Trx)]
    [InlineData("pytest.xml", ReportFormat.JUnit)]
    [InlineData("ctest.xml", ReportFormat.JUnit)]
    [InlineData("dotnet-junit.xml", ReportFormat.JUnit)]
    [InlineData("nunit3.xml", ReportFormat.NUnit3)]
    [InlineData("xunit-v2.xml", ReportFormat.XUnit2)]
    public void Sample_report_is_recognised(string sample, ReportFormat expected)
    {
        Assert.Equal(expected, ReportFormatDetector.Detect(SamplePath(sample)));
    }

    [Theory]
    [InlineData("xunit.trx", "report.xml", ReportFormat.Trx)]
    [InlineData("pytest.xml", "report.trx", ReportFormat.JUnit)]
    [InlineData("nunit3.xml", "report", ReportFormat.NUnit3)]
    public void File_name_does_not_matter(string sample, string fileName, ReportFormat expected)
    {
        var path = Path.Combine(_directory, fileName);
        File.Copy(SamplePath(sample), path);

        Assert.Equal(expected, ReportFormatDetector.Detect(path));
    }

    [Fact]
    public void Only_the_start_of_the_file_is_read()
    {
        // The report is cut off after the root element, which a full read would refuse.
        var path = Write("<testsuites><testsuite tests=\"1\"><testcase");

        Assert.Equal(ReportFormat.JUnit, ReportFormatDetector.Detect(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a report")]
    [InlineData("<test-results />")]
    [InlineData("<TestRun />")]
    [InlineData("""<testsuites xmlns="urn:other" />""")]
    [InlineData("<!DOCTYPE html><html />")]
    [InlineData("<html><body /></html>")]
    public void Unknown_or_broken_file_is_refused(string content)
    {
        var path = Write(content);

        Assert.Throws<InvalidDataException>(() => ReportFormatDetector.Detect(path));
    }

    // @relation(CTA-PARSE-007, scope=range_end)

    private static string SamplePath(string sample) =>
        Path.Combine(AppContext.BaseDirectory, "Samples", sample);

    private string Write(string content)
    {
        var path = Path.Combine(_directory, "report.xml");
        File.WriteAllText(path, content);
        return path;
    }
}
