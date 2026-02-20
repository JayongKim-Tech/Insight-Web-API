using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace VisionCore.Models
{
    internal class ini
    {
        private readonly string _path;

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern long WritePrivateProfileString(string section, string key, string val, string filePath);

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder retVal, int size, string filePath);

        public ini(string path) { _path = path; }

        public void Write(string section, string key, string value)
        {
            WritePrivateProfileString(section, key, value, _path);
        }

        public string Read(string section, string key, string def = "")
        {
            StringBuilder temp = new StringBuilder(255);
            GetPrivateProfileString(section, key, def, temp, 255, _path);
            return temp.ToString();
        }
    }
}
