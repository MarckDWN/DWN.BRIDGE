using MdXaml.Plugins;

namespace MdXaml.FrontMatter
{
    /// <summary>
    /// Registers the FrontMatter filter as the first filter so it runs at the start of the transform chain.
    /// </summary>
    public class FrontMatterPluginSetup : IPluginSetup
    {
        public void Setup(MdXamlPlugins plugins)
        {
            plugins.Filters.Insert(0, new FrontMatterFilter());
            plugins.ViewerArranger.Add(new FrontMatterViewerArranger());
        }
    }
}