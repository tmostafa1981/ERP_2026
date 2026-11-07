using System;
using System.Text;

namespace ERP.Application.Helpers
{
    public static class CurrencyTafqeetHelper
    {
        private static readonly string[] ArabicOnes =
        {
            "", "واحد", "اثنان", "ثلاثة", "أربعة", "خمسة", "ستة", "سبعة", "ثمانية", "تسعة",
            "عشرة", "أحد عشر", "اثنا عشر", "ثلاثة عشر", "أربعة عشر", "خمسة عشر", "ستة عشر",
            "سبعة عشر", "ثمانية عشر", "تسعة عشر"
        };

        private static readonly string[] ArabicTens =
        {
            "", "", "عشرون", "ثلاثون", "أربعون", "خمسون", "ستون", "سبعون", "ثمانون", "تسعون"
        };

        private static readonly string[] ArabicHundreds =
        {
            "", "مائة", "مئتان", "ثلاثمائة", "أربعمائة", "خمسمائة", "ستمائة", "سبعمائة", "ثمانمائة", "تسعمائة"
        };

        public static string ConvertToWords(decimal amount, string currencyCode = "EGP", bool isArabic = true)
        {
            if (amount == 0)
                return isArabic ? "صفر" : "Zero";

            long integerPart = (long)Math.Floor(amount);
            int decimalPart = (int)Math.Round((amount - integerPart) * 100);

            if (!isArabic)
            {
                return $"{amount:N2} {currencyCode}";
            }

            var (mainUnitSingular, mainUnitPlural, subUnitSingular) = currencyCode.ToUpperInvariant() switch
            {
                "USD" => ("دولار أمريكي", "دولارات أمريكية", "سنت"),
                "EUR" => ("يورو", "يورو", "سنت"),
                "SAR" => ("ريال سعودي", "ريالات سعودية", "هللة"),
                _ => ("جنيه مصري", "جنيهات مصرية", "قرش")
            };

            var sb = new StringBuilder("فقط ");
            sb.Append(ConvertIntegerToArabicWords(integerPart));
            sb.Append(" ");
            sb.Append(integerPart is >= 3 and <= 10 ? mainUnitPlural : mainUnitSingular);

            if (decimalPart > 0)
            {
                sb.Append(" و ");
                sb.Append(ConvertIntegerToArabicWords(decimalPart));
                sb.Append(" ");
                sb.Append(subUnitSingular);
            }

            sb.Append(" لا غير");
            return sb.ToString();
        }

        private static string ConvertIntegerToArabicWords(long number)
        {
            if (number == 0) return "";
            if (number < 0) return "سالب " + ConvertIntegerToArabicWords(-number);

            var sb = new StringBuilder();

            if (number >= 1_000_000_000)
            {
                long billions = number / 1_000_000_000;
                sb.Append(ConvertGroup(billions, "مليار", "ملياران", "مليارات"));
                number %= 1_000_000_000;
                if (number > 0) sb.Append(" و ");
            }

            if (number >= 1_000_000)
            {
                long millions = number / 1_000_000;
                sb.Append(ConvertGroup(millions, "مليون", "مليونان", "ملايين"));
                number %= 1_000_000;
                if (number > 0) sb.Append(" و ");
            }

            if (number >= 1_000)
            {
                long thousands = number / 1_000;
                sb.Append(ConvertGroup(thousands, "ألف", "ألفان", "آلاف"));
                number %= 1_000;
                if (number > 0) sb.Append(" و ");
            }

            if (number > 0)
            {
                sb.Append(ConvertHundreds(number));
            }

            return sb.ToString();
        }

        private static string ConvertGroup(long val, string singular, string dual, string plural)
        {
            if (val == 1) return singular;
            if (val == 2) return dual;
            if (val is >= 3 and <= 10) return ConvertHundreds(val) + " " + plural;
            return ConvertHundreds(val) + " " + singular;
        }

        private static string ConvertHundreds(long val)
        {
            var sb = new StringBuilder();
            long hundreds = val / 100;
            long remainder = val % 100;

            if (hundreds > 0)
            {
                sb.Append(ArabicHundreds[hundreds]);
                if (remainder > 0) sb.Append(" و ");
            }

            if (remainder > 0)
            {
                if (remainder < 20)
                {
                    sb.Append(ArabicOnes[remainder]);
                }
                else
                {
                    long ones = remainder % 10;
                    long tens = remainder / 10;

                    if (ones > 0)
                    {
                        sb.Append(ArabicOnes[ones]);
                        sb.Append(" و ");
                    }
                    sb.Append(ArabicTens[tens]);
                }
            }

            return sb.ToString();
        }
    }
}
