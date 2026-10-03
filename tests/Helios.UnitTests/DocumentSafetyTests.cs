using System.Text;
using Helios.Infrastructure.Documents;

namespace Helios.UnitTests;

public class DocumentSafetyTests
{
    [Theory]
    [InlineData("<< /S /JavaScript /JS (x) >>", "JavaScript")]
    [InlineData("<< /Type /Action /S /Launch /F (cmd.exe) >>", "Launch")]
    [InlineData("<< /EmbeddedFiles 5 0 R >>", "EmbeddedFiles")]
    [InlineData("<</JS(x)>>", "JS")]
    [InlineData("<< /S /Java#53cript >>", "JavaScript")]       // hex-escaped name
    [InlineData("<< /S /#4caunch >>", "Launch")]
    public void Active_content_names_are_found_outside_streams(string pdfFragment, string expected)
    {
        Assert.Equal(expected, DocumentInspector.FindForbiddenName(Encoding.ASCII.GetBytes(pdfFragment)));
    }

    [Theory]
    [InlineData("<< /Type /Page /JSON 1 >>")]                         // a longer name, not /JS
    [InlineData("<< /Length 9 >>\nstream\n/JS /Launch\nendstream")]  // inside a stream body
    [InlineData("<< /Type /Catalog /Pages 2 0 R >>")]
    public void Benign_content_is_not_flagged(string pdfFragment)
    {
        Assert.Null(DocumentInspector.FindForbiddenName(Encoding.ASCII.GetBytes(pdfFragment)));
    }

    [Fact]
    public void Names_after_a_stream_are_still_checked()
    {
        var bytes = Encoding.ASCII.GetBytes("stream\nrandom /JS bytes\nendstream\n<< /S /Launch >>");

        Assert.Equal("Launch", DocumentInspector.FindForbiddenName(bytes));
    }

    [Theory]
    [InlineData("stream: OK\0", true, null)]
    [InlineData("stream: Win.Test.EICAR_HDB-1 FOUND\0", false, "Win.Test.EICAR_HDB-1")]
    public void ClamAv_replies_are_interpreted(string reply, bool clean, string? signature)
    {
        var verdict = ClamAvScanner.Interpret(reply);

        Assert.Equal(clean, verdict.Clean);
        Assert.Equal(signature, verdict.Signature);
    }

    [Fact]
    public void An_unexpected_ClamAv_reply_is_an_error_not_a_pass()
    {
        Assert.Throws<InvalidOperationException>(() => ClamAvScanner.Interpret("INSTREAM size limit exceeded. ERROR\0"));
    }
}
