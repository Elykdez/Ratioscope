using Hypocycloid.Utils;
using NUnit.Framework;

namespace Hypocycloid.Editor
{
    public class LogHelperTests
    {
        [Test]
        public void TruncateJsonTruncatesBase64ValuePastLogLimit()
        {
            string base64 = new('A', 6000);
            string payload = $"{{\"image\":\"{base64}\",\"status\":\"ok\"}}";

            string result = LogHelper.TruncateJson(payload);

            Assert.That(
                result,
                Does.Contain($"\"image\":\"{new string('A', 30)}... (truncated)\"")
            );
            Assert.That(result, Does.Not.Contain(new string('A', 200)));
        }
    }
}
