using MdXaml.Plugins;
using System.Windows;

namespace MdXaml.SyntaxHigh
{
    internal class SytaxHighStyleOverwriter : IStyleOverwriter
    {
        public Style Overwrite(Style style)
        {
            return SyntaxHighStyle.OverwriteStyle(style);
        }
    }
}