using MdXaml.Plugins;

namespace MdXaml.SyntaxHigh
{
    public class SyntaxHighPluginSetup : IPluginSetup
    {
        public void Setup(MdXamlPlugins plugins)
        {
            plugins.CodeBlockLoader.Add(new AvalonCodeBlockLoader());
            plugins.StyleOverwriter.Add(new SytaxHighStyleOverwriter());
        }
    }
}
