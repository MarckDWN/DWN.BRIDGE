using ApprovalTests;
using ApprovalTests.Reporters;
using MdXaml.Plugins;
using MdXamlTest;
using NUnit.Framework;
using System;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;


namespace MdXaml.Test
{
    [UseReporter(typeof(DiffReporter))]
    public class StandardTests
    {
        static StandardTests()
        {
            var fwNm = Utils.GetRuntimeName();
            Approvals.RegisterDefaultNamerCreation(() => new ChangeOutputPathNamer("Out/" + fwNm));
        }

        readonly string assetPath;
        readonly Uri baseUri;


        public StandardTests()
        {
            PackUriHelper.Create(new Uri("http://example.com"));

            var asm = Assembly.GetExecutingAssembly();
            assetPath = Path.GetDirectoryName(asm.Location);
            baseUri = new Uri($"pack://application:,,,/{asm.GetName().Name};Component/");
        }

        public static Markdown CreateMarkdown()
        {
            var markdown = new Markdown();
            markdown.Plugins = new MdXamlPlugins(SyntaxManager.Standard);
            if (markdown.Plugins.CodeBlockLoader.Count == 0)
            {
                markdown.Plugins.CodeBlockLoader.Add(new MdXaml.SyntaxHigh.AvalonCodeBlockLoader());
            }
            return markdown;
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Transform_givenLists1_generatesExpectedResult()
        {
            var text = Utils.LoadText("Lists1.md");
            var markdown = CreateMarkdown();
            markdown.DisabledContextMenu = true;
            var result = markdown.Transform(text);
            Approvals.Verify(Utils.AsXaml(result));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Transform_givenHorizontalRules_generatesExpectedResult()
        {
            var text = Utils.LoadText("HorizontalRules.md");
            var markdown = CreateMarkdown();
            markdown.DisabledContextMenu = true;
            var result = markdown.Transform(text);
            Approvals.Verify(Utils.AsXaml(result));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Transform_givenMixing_generatesExpectedResult()
        {
            var text = Utils.LoadText("Mixing.md");
            var markdown = CreateMarkdown();
            markdown.AssetPathRoot = assetPath;
            markdown.BaseUri = baseUri;
            markdown.DisabledContextMenu = true;

            var result = markdown.Transform(text);
            var resultXaml = Utils.AsXaml(result);

            var assetUri = new Uri(assetPath);

            // change absolute filepath to relative-like
            resultXaml = resultXaml.Replace("UriSource=\"" + assetPath, "UriSource=\"<assetpathroot>");

            Approvals.Verify(resultXaml);
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Transform_givenCodes_generatesExpectedResult()
        {
            var text = Utils.LoadText("Codes.md");
            var markdown = CreateMarkdown();
            markdown.AssetPathRoot = assetPath;
            markdown.DisabledContextMenu = true;

            var result = markdown.Transform(text);
            Approvals.Verify(Utils.AsXaml(result));
        }
    }
}