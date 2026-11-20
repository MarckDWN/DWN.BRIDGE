using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Markup;
using System.Xml;

namespace MdXamlTest
{
    public static class Utils
    {
        public static string ReadMarkdown([CallerMemberName] string fileBaseName = null)
        {
            if (fileBaseName is null)
                throw new ArgumentNullException(nameof(fileBaseName));

            var asm = Assembly.GetCallingAssembly();
            var asmDir = Path.GetDirectoryName(asm.Location);
            var path = Path.Combine(asmDir, "Input", fileBaseName + ".md");
            return File.ReadAllText(path);
        }

        public static string AsXaml(object instance)
        {
            using (var writer = new StringWriter())
            {
                var settings = new XmlWriterSettings { Indent = true };
                using (var xmlWriter = XmlWriter.Create(writer, settings))
                {
                    XamlWriter.Save(instance, xmlWriter);
                }

                writer.WriteLine();
                return writer.ToString();
            }
        }

        public static string GetRuntimeName()
        {
            var description = RuntimeInformation.FrameworkDescription.ToLower();
            // ".NET Framework"
            // ".NET Core"(for .NET Core 1.0 - 3.1)
            // ".NET Native"
            // ".NET"(for .NET 5.0 and later versions)

            if (description.Contains("framework"))
            {
                return "framework";
            }

            if (description.Contains("core"))
            {
                return "core";
            }

            if (description.Contains("native"))
            {
                return "native";
            }

            return "dotnet";
        }
    }
}
