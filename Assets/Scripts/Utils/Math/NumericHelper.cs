using System;
using UnityEngine;

namespace Hypocycloid.Utils
{
    public static class NumericHelper
    {
        static readonly (int numericValue, string representation)[] RomanNumeralsLeadingLettersDescending = new[]
        {
            (1000, "M"),
            (900, "CM"),
            (500, "D"),
            (400, "CD"),
            (100, "C"),
            (90, "XC"),
            (50, "L"),
            (40, "XL"),
            (10, "X"),
            (9, "IX"),
            (5, "V"),
            (4, "IV"),
            (1, "I"),
        };

        /// <summary>
        /// Adjusts the given number to be even.
        /// Example: 0,2,4,6...
        /// </summary>
        public static int ToEven(int number)
        {
            return number % 2 == 0 ? number : number + 1;
        }

        /// <summary>
        /// Adjusts the given number to be odd.
        /// Example: 1,3,5,7...
        /// </summary>
        public static int ToOdd(int number)
        {
            return number % 2 == 0 ? number + 1 : number;
        }

        /// <summary>
        /// Converts an integer to its binary string representation with "0b" prefix.
        /// Example: 5 -> "0b101"
        /// </summary>
        public static string ToBinaryString(int number)
        {
            return "0b" + Convert.ToString(number, 2);
        }

        /// <summary>
        /// Converts an integer to its binary string representation with "0b" prefix
        /// and pads with zeros to match the specified length.
        /// Example: (5, 8) -> "0b00000101"
        /// </summary>
        public static string ToBinaryString(int number, int padLength)
        {
            return "0b" + Convert.ToString(number, 2).PadLeft(padLength, '0');
        }

        public static string ConvertToRomanNumeral(int number)
        {
            if (number <= 0)
                throw new ArgumentOutOfRangeException($"{nameof(number)} must be above 0");

            string romanNumeral = "";
            int remainder = number;
            foreach (var letter in RomanNumeralsLeadingLettersDescending)
            {
                while (remainder >= letter.numericValue)
                {
                    romanNumeral += letter.representation;
                    remainder -= letter.numericValue;
                }
            }

            return romanNumeral;
        }
    }
}
