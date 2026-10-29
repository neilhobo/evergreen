using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Globeweigh.Model;

namespace Globeweigh.Shared.Helpers
{
    public static class BarcodeHelper
    {
        public static int GetEntityId(string scanResult, int expectedPalletType)
        {
            if (string.IsNullOrEmpty(scanResult)) return -1;
            if (!scanResult.Contains("[")) return -1;
            if (!scanResult.Contains("]")) return -1;
            int barcodeType = Convert.ToInt32(scanResult.Split('[', ']')[1]);

            if (barcodeType != expectedPalletType) return -1;

            string palletIdString = scanResult.GetUntilOrEmpty();

            return Convert.ToInt32(palletIdString);

        }

        public static int GetPalletId(string scanResult, int expectedPalletType)
        {
            if (string.IsNullOrEmpty(scanResult)) return -1;
            if (!scanResult.Contains("[")) return -1;
            if (!scanResult.Contains("]")) return -1;
            int barcodeType = Convert.ToInt32(scanResult.Split('[', ']')[1]);

            if (barcodeType != expectedPalletType) return -1;

            string palletIdString = scanResult.GetUntilOrEmpty();

            return Convert.ToInt32(palletIdString);

        }

        public static string GetUntilOrEmpty(this string text, string stopAt = "[")
        {
            if (!String.IsNullOrWhiteSpace(text))
            {
                int charLocation = text.IndexOf(stopAt, StringComparison.Ordinal);

                if (charLocation > 0)
                {
                    return text.Substring(0, charLocation);
                }
            }

            return String.Empty;
        }
    }
}
