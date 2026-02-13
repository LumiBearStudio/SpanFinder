using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Span.Helpers
{
    /// <summary>
    /// Natural string comparer using Windows StrCmpLogicalW API.
    /// Provides Windows Explorer-like sorting (1.txt, 2.txt, 10.txt instead of 1.txt, 10.txt, 2.txt).
    /// </summary>
    public class NaturalStringComparer : IComparer<string>
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string psz1, string psz2);

        public int Compare(string? x, string? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            return StrCmpLogicalW(x, y);
        }

        public static readonly NaturalStringComparer Instance = new NaturalStringComparer();
    }
}
