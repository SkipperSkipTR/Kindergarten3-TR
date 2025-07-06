using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Kindergarten3_TR_Installer.Resources
{
    public static class ZipHelper
    {
        public static string Extract7ZipIfMissing(string gamePath)
        {
            string tempPath = Path.Combine(gamePath, "7za.exe");

            if (!File.Exists(tempPath))
            {
                var assembly = Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream("Kindergarten3_TR_Installer.Resources.7za.exe")) // adjust this namespace if needed
                using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fileStream);
                }
            }

            return tempPath; // Return the path to use it later
        }
    }
}
