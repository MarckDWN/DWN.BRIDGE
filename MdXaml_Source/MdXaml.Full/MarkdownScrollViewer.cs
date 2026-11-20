using MdXaml.AnimatedGif;
using MdXaml.FrontMatter;
using MdXaml.Html;
using MdXaml.Plugins;
using MdXaml.Svg;
using Svg;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace MdXaml.Full
{
    public class MarkdownScrollViewer : MdXaml.SyntaxHigh.MarkdownScrollViewer
    {
        public override MdXamlPlugins? Plugins
        {
            get => base.Plugins;
            set
            {
                var nplg = value is null ? new MdXamlPlugins() : value;

                AddIfAbsent<FrontMatterPluginSetup>(nplg.Setups);

                AddIfAbsent<HtmlPluginSetup>(nplg.Setups);
                AddIfAbsent<SvgPluginSetup>(nplg.Setups);
                AddIfAbsent<AnimatedGifPluginSetup>(nplg.Setups);

                base.Plugins = nplg;
            }
        }

        public MarkdownScrollViewer()
        {
        }
    }
}