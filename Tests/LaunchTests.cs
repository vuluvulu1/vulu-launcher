using Xunit;

namespace VuluLauncher.Tests
{
    /// <summary>Oyunun XML günlük olaylarının düz satırlara çevrilmesi (Log4jXml).</summary>
    public class Log4jXmlTests
    {
        private static List<(string Text, bool Error)> Read(params string[] lines)
        {
            var result = new List<(string, bool)>();
            var reader = new Log4jXml((text, error) => result.Add((text, error)));
            foreach (var line in lines) reader.Feed(line);
            return result;
        }

        [Fact]
        public void EventBecomesOnePlainLine()
        {
            var lines = Read(
                "<log4j:Event logger=\"FabricLoader/Mixin\" timestamp=\"1794144387431\" level=\"INFO\" thread=\"main\">",
                "  <log4j:Message><![CDATA[SpongePowered MIXIN Subsystem Version=0.8.7]]></log4j:Message>",
                "</log4j:Event>");
            Assert.Single(lines);
            Assert.Matches(@"^\[\d\d:\d\d:\d\d\] \[main/INFO\]: SpongePowered MIXIN Subsystem Version=0\.8\.7$", lines[0].Text);
            Assert.False(lines[0].Error);
        }

        [Fact]
        public void WarningsAndThrowablesAreErrors()
        {
            var lines = Read(
                "<log4j:Event logger=\"x\" timestamp=\"1794144387431\" level=\"WARN\" thread=\"Render thread\">",
                "<log4j:Message><![CDATA[Something failed]]></log4j:Message>",
                "<log4j:Throwable><![CDATA[java.lang.RuntimeException: boom",
                "\tat a.b.C.d(C.java:1)",
                "]]></log4j:Throwable>",
                "</log4j:Event>");
            Assert.Equal(3, lines.Count);
            Assert.Contains("[Render thread/WARN]: Something failed", lines[0].Text);
            Assert.Equal("java.lang.RuntimeException: boom", lines[1].Text);
            Assert.All(lines, l => Assert.True(l.Error));
        }

        [Fact]
        public void PlainLinesPassThrough()
        {
            var lines = Read("[16:05:33] [main/INFO]: Loading Minecraft 26.3 with Fabric Loader 0.20.1", "plain");
            Assert.Equal(new[] { "[16:05:33] [main/INFO]: Loading Minecraft 26.3 with Fabric Loader 0.20.1", "plain" }, lines.Select(l => l.Text));
        }

        [Fact]
        public void MultiLineMessageKeepsItsLines()
        {
            var lines = Read(
                "<log4j:Event logger=\"x\" timestamp=\"1794144387431\" level=\"INFO\" thread=\"main\">",
                "<log4j:Message><![CDATA[Loading 3 mods:",
                "\t- fabricloader 0.20.1",
                "\t- minecraft 26.3]]></log4j:Message>",
                "</log4j:Event>");
            Assert.Equal(3, lines.Count);
            Assert.EndsWith("Loading 3 mods:", lines[0].Text);
            Assert.Equal("\t- minecraft 26.3", lines[2].Text);
        }
    }
}
