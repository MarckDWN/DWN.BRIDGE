
using ApprovalTests;
using ApprovalTests.Reporters;
using MdXaml.FrontMatter;
using MdXaml.Plugins;
using MdXamlTest;
using NUnit.Framework;
using System.Threading;
using System.Windows.Documents;

namespace MdXaml.FrontMatter.Test
{
    [UseReporter(typeof(DiffReporter))]
    public class UnitTest
    {
        private Markdown manager;

        [SetUp]
        [Apartment(ApartmentState.STA)]
        public void Setup()
        {
            var fwNm = Utils.GetRuntimeName();
            Approvals.RegisterDefaultNamerCreation(() => new ChangeOutputPathNamer("Out/" + fwNm));

            var plugins = new MdXamlPlugins();
            plugins.Setups.Add(new FrontMatterPluginSetup());

            manager = new Markdown
            {
                Plugins = plugins
            };
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void WithFrontMatter()
        {
            var markdown = Utils.ReadMarkdown();

            var doc = manager.Transform(markdown);

            Assert.That(FrontMatter.GetFrontMatterYaml(doc), Is.Not.Null, "FrontMatter YAML should be attached to the document.");
            var dic = FrontMatter.GetFrontMatterDic(doc);
            Assert.That(dic, Is.Not.Null);
            Assert.That(dic.ContainsKey("title"), Is.True);
            Assert.That(dic["title"], Is.EqualTo("Hello"));

            var xaml = Utils.AsXaml(doc);
            Approvals.Verify(xaml);
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void WithoutFrontMatter()
        {
            var markdown = Utils.ReadMarkdown();

            var doc = manager.Transform(markdown);

            Assert.That(FrontMatter.GetFrontMatterYaml(doc), Is.Null, "Document without front matter should have no YAML attached.");
            Assert.That(FrontMatter.GetFrontMatterDic(doc), Is.Null);

            var xaml = Utils.AsXaml(doc);
            Approvals.Verify(xaml);
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void UnclosedFrontMatter()
        {
            var markdown = Utils.ReadMarkdown();

            var doc = manager.Transform(markdown);

            Assert.That(FrontMatter.GetFrontMatterYaml(doc), Is.Null, "Unclosed front matter should be treated as normal content.");

            var xaml = Utils.AsXaml(doc);
            Approvals.Verify(xaml);
        }
    }
}
