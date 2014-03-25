using System;
using System.Text;

namespace STUN
{
    class SASLPrep
    {

        private enum StringType
        {
            STORED_STRING,
            QUERY
        };


        private static string GeneralSASLPrep(string input, bool checkBidi, StringType stringType, bool includeUnassigned)
        {

            // String prüfen
            if (stringType == StringType.STORED_STRING && includeUnassigned)
            {
                throw new Exception("Stored strings using the profile MUST NOT contain any unassigned code points.");
            }


            // Mapping
            StringBuilder sb = new StringBuilder(input.Length);

            foreach (char c in input.ToCharArray())
            {
                if (contains(c12Entries, c))
                    sb.Append(' ');

                else if (contains(b1Entries, c))
                    continue;

                else
                    sb.Append(c);
            }

            string mappedInput = sb.ToString();


            // Normalization
            string normalizedInput = mappedInput.Normalize(NormalizationForm.FormKC);


            // Prohibited Output
            char[] ni = normalizedInput.ToCharArray();
            int length = ni.Length;

            for (int i = 0; i < length; i++)
            {
                if (contains(c12Entries, ni[i])) throw new Exception("Prohibited output: Non-ASCII space characters");
                if (contains(c21Entries, ni[i])) throw new Exception("Prohibited output: ASCII control characters");
                if (contains(c22Entries, ni[i])) throw new Exception("Prohibited output: Non-ASCII control characters");
                if (contains(c3Entries, ni[i])) throw new Exception("Prohibited output: Private Use characters");
                if (contains(c4Entries, ni[i])) throw new Exception("Prohibited output: Non-character code points");
                if (contains(c6Entries, ni[i])) throw new Exception("Prohibited output: Inappropriate for plain text characters");
                if (contains(c7Entries, ni[i])) throw new Exception("Prohibited output: Inappropriate for canonical representation characters");
                if (contains(c8Entries, ni[i])) throw new Exception("Prohibited output: Change display properties or deprecated characters");
                if (contains(c9Entries, ni[i])) throw new Exception("Prohibited output: Tagging characters");
                if (isHighSurrogate((int)ni[i]))
                {
                    if (i + 1 == length) throw new Exception("Malformed supplementary code");
                    if (!isLowSurrogate((int)ni[i + 1])) throw new Exception("Malformed supplementary code");
                    if (contains(c22EntriesS, CodePointAt(normalizedInput, i))) throw new Exception("Prohibited output: Non-ASCII control characters");
                    if (contains(c3EntriesS, CodePointAt(normalizedInput, i))) throw new Exception("Prohibited output: Private Use characters");
                    if (contains(c4EntriesS, CodePointAt(normalizedInput, i))) throw new Exception("Prohibited output: Non-character code points");
                    if (contains(c9EntriesS, CodePointAt(normalizedInput, i))) throw new Exception("Prohibited output: Tagging characters");
                    i++;
                }
            }


            // Bidirectional Characters
            string nonProhibitedOutput = normalizedInput;
            length = nonProhibitedOutput.Length;

            if (checkBidi)
            {
                //if (!(length == 1 || (length == 2 && nonProhibitedOutput.codePointAt(0) > 65535)))
                if (!(length == 1 || (length == 2 && CodePointAt(nonProhibitedOutput, 0) > 65535)))
                {
                    bool anyRandALCat, anyLCat, firstCatIsRandL, lastCatIsRandL = false, lastCharALowSurrogate = false;
                    int firstChar, lastChar;
                    int codePoint;
                    //firstChar = nonProhibitedOutput.codePointAt(0);
                    firstChar = CodePointAt(nonProhibitedOutput, 0);
                    anyRandALCat = contains(d1, firstChar) || contains(d1S, firstChar);
                    firstCatIsRandL = anyRandALCat;
                    anyLCat = contains(d2, firstChar) || contains(d2S, firstChar);

                    //lastCharALowSurrogate = isLowSurrogate(nonProhibitedOutput.codePointAt(length - 1));
                    lastCharALowSurrogate = isLowSurrogate(CodePointAt(nonProhibitedOutput, length - 1));


                    //lastChar = nonProhibitedOutput.codePointAt(length - (lastCharALowSurrogate ? 2 : 1));
                    lastChar = CodePointAt(nonProhibitedOutput, length - (lastCharALowSurrogate ? 2 : 1));

                    if (contains(d1, lastChar) || contains(d1S, lastChar))
                    {
                        anyRandALCat = true;
                        lastCatIsRandL = anyRandALCat;
                    }
                    if (contains(d2, lastChar) || contains(d2S, lastChar))
                    {
                        anyLCat = true;
                    }
                    if (anyRandALCat && anyLCat)
                    {
                        throw new Exception("If a string contains any RandALCat character, the string MUST NOT contain any LCat character");
                    }
                    if ((firstCatIsRandL && !lastCatIsRandL) || (!firstCatIsRandL && lastCatIsRandL))
                    {
                        throw new Exception("If a string contains any RandALCat character, a RandALCat character MUST be the first character "
                            + "of the string, and a RandALCat character MUST be the last character of the string");
                    }


                    //for (int i = (nonProhibitedOutput.codePointAt(0) > 65535 ? 2 : 1); i < length - (lastCharALowSurrogate ? 2 : 1); i++)
                    for (int i = (CodePointAt(nonProhibitedOutput, 0) > 65535 ? 2 : 1); i < length - (lastCharALowSurrogate ? 2 : 1); i++)
                    {
                        //codePoint = nonProhibitedOutput.codePointAt(i);
                        codePoint = CodePointAt(nonProhibitedOutput, i);
                        if (contains(d1, codePoint) || contains(d1S, codePoint))
                        {
                            anyRandALCat = true;
                        }
                        if (contains(d2, codePoint) || contains(d2S, codePoint))
                        {
                            anyLCat = true;
                        }
                        if (anyRandALCat)
                        {
                            if (anyLCat)
                            {
                                throw new Exception("If a string contains any RandALCat character, the string MUST NOT contain any LCat character");
                            }
                            if (!(firstCatIsRandL && lastCatIsRandL))
                            {
                                throw new Exception("If a string contains any RandALCat character, a RandALCat character MUST be the first character "
                                    + "of the string, and a RandALCat character MUST be the last character of the string");
                            }
                        }

                        if (isHighSurrogate(codePoint))
                        {
                            i++;
                        }
                    }
                }

            }

            if (stringType == StringType.QUERY && includeUnassigned)
                return nonProhibitedOutput;


            // Unassigned Code Points
            sb = new StringBuilder(nonProhibitedOutput.Length);
            int _codePoint;

            //boolean lastCharALowSurrogate = isLowSurrogate(nonProhibitedOutput.codePointAt(length - 1));
            bool _lastCharALowSurrogate = isLowSurrogate(CodePointAt(nonProhibitedOutput, length - 1));
            bool highSurrogate;
            for (int i = 0; i < length - (_lastCharALowSurrogate ? 2 : 1); i++)
            {

                //codePoint = nonProhibitedOutput.codePointAt(i);
                _codePoint = CodePointAt(nonProhibitedOutput, i);
                highSurrogate = isHighSurrogate(_codePoint);
                if (!(contains(a1, _codePoint) || contains(a1S, _codePoint)))
                {
                    sb.Append(nonProhibitedOutput[i]);
                    if (highSurrogate)
                    {
                        sb.Append(nonProhibitedOutput[i + 1]);
                    }
                }
                if (highSurrogate) i++;
            }

            return sb.ToString();


        }

        public static string STUNSASLPrep(string input)
        {
            return GeneralSASLPrep(input, true, StringType.QUERY, true);
        }


        #region Prüffunktionen

        private static bool contains(int[] map, int entry)
        {
            foreach (int c in map)
            {
                if (c == entry)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool contains(char[] map, char entry)
        {
            foreach (char c in map)
            {
                if (c == entry)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool contains(int[][] map, int entry)
        {
            foreach (int[] i in map)
            {
                if (entry >= i[0] && entry <= i[1])
                {
                    return true;
                }
            }
            return false;
        }

        private static bool contains(char[][] map, char entry)
        {
            foreach (char[] i in map)
            {
                if (entry >= i[0] && entry <= i[1])
                {
                    return true;
                }
            }
            return false;
        }

        private static bool isHighSurrogate(int codePoint)
        {
            return codePoint >= 55296 && codePoint <= 56319;
        }

        private static bool isLowSurrogate(int codePoint)
        {
            return codePoint >= 56320 && codePoint <= 57343;
        }

        #endregion


        #region Einträge

        private static readonly char[] c12Entries = new char[]{(char)0x00a0, (char)0x1680, (char)0x2000, (char)0x2001, (char)
                                                         0x2002, (char)0x2003, (char)0x2004, (char)0x2005, (char)
                                                         0x2006, (char)0x2007, (char)0x2008, (char)0x2009, (char)
                                                         0x200a, (char)0x200b, (char)0x202f, (char)0x2057, (char)
                                                         0x3000};

        private static readonly char[] c21Entries = new char[]{(char)0x0000, (char)0x0001, (char)0x0002, (char)0x0003, (char)
                                                         0x0004, (char)0x0005, (char)0x0006, (char)0x0007, (char)
                                                         0x0008, (char)0x0009, (char)0x000a, (char)0x000b, (char)
                                                         0x000c, (char)0x000d, (char)0x000e, (char)0x000f, (char)
                                                         0x0010, (char)0x0011, (char)0x0012, (char)0x0013, (char)
                                                         0x0014, (char)0x0015, (char)0x0016, (char)0x0017, (char)
                                                         0x0018, (char)0x0019, (char)0x001a, (char)0x001b, (char)
                                                         0x001c, (char)0x001d, (char)0x001e, (char)0x001f, (char)
                                                         0x007F};

        private static readonly char[] c22Entries = new char[]{(char)0x0080, (char)0x0081, (char)0x0082, (char)0x0083, (char)
                                                         0x0084, (char)0x0085, (char)0x0086, (char)0x0087, (char)
                                                         0x0088, (char)0x0089, (char)0x008a, (char)0x008b, (char)
                                                         0x008c, (char)0x008d, (char)0x008e, (char)0x008f, (char)
                                                         0x0090, (char)0x0091, (char)0x0092, (char)0x0093, (char)
                                                         0x0094, (char)0x0095, (char)0x0096, (char)0x0097, (char)
                                                         0x0098, (char)0x0099, (char)0x009a, (char)0x009b, (char)
                                                         0x009c, (char)0x009d, (char)0x009e, (char)0x009f, (char)
                                                         0x06dd, (char)0x070e, (char)0x180e, (char)0x200c, (char)
                                                         0x200d, (char)0x2028, (char)0x2029, (char)0x2060, (char)
                                                         0x2061, (char)0x2062, (char)0x2063, (char)0x206a, (char)
                                                         0x206b, (char)0x206c, (char)0x206d, (char)0x206e, (char)
                                                         0x206f, (char)0xfeff, (char)0xfff9, (char)0xfffa, (char)
                                                         0xfffb, (char)0xfffc};

        private static readonly int[][] c22EntriesS = new int[][] { new int[] { 119155, 119162 } };

        private static readonly char[][] c3Entries = new char[][] { new char[] { (char)0xe000, (char)0xf8ff } };

        private static readonly int[][] c3EntriesS = new int[][] { new int[] { 983040, 1048573 }, new int[] { 1048576, 1114109 } };

        private static readonly char[][] c4Entries = new char[][] { new char[] { (char)0xfdd0, (char)0xfdef }, new char[] { (char)0xfffe, (char)0xffff } };

        private static readonly int[][] c4EntriesS = new int[][]{new int[] {131070,131071},
                                                            new int[] {196606,196607},
                                                            new int[] {262142,262143},
                                                            new int[] {327678,327679},
                                                            new int[] {393214,393215},
                                                            new int[] {458750,458751},
                                                            new int[] {524286,524287},
                                                            new int[] {589822,589823},
                                                            new int[] {655358,655359},
                                                            new int[] {720894,720895},
                                                            new int[] {786430,786431},
                                                            new int[] {851966,851967},
                                                            new int[] {917502,917503},
                                                            new int[] {983038,983039},
                                                            new int[] {1048574,1048575},
                                                            new int[] {1114110,1114111}};

        private static readonly char[] c6Entries = new char[] { (char)0xfff9, (char)0xfffa, (char)0xfffb, (char)0xfffc, (char)0xfffd };

        private static readonly char[][] c7Entries = new char[][] { new char[] { (char)0x2ff0, (char)0x2ffb } };

        private static readonly char[] c8Entries = new char[]{(char)0x0340, (char)0x0341, (char)0x200e, (char)0x200f, (char)0x202a, (char)
                                                             0x202b, (char)0x202c, (char)0x202d, (char)0x202e, (char)0x206a, (char)
                                                             0x206b, (char)0x206c, (char)0x206d, (char)0x206e, (char)0x206f};

        private static readonly char[] c9Entries = new char[] { (char)0xe001 };

        private static readonly int[][] c9EntriesS = new int[][] { new int[] { 917536, 917631 } };

        private static readonly char[] b1Entries = new char[]{(char)0x00ad, (char)0x034f, (char)0x1806, (char)0x180b, (char)
                                                         0x180c, (char)0x180d, (char)0x200b, (char)0x200c, (char)
                                                         0x200d, (char)0x2060, (char)0xfe00, (char)0xfe01, (char)
                                                         0xfe02, (char)0xfe03, (char)0xfe04, (char)0xfe05, (char)
                                                         0xfe06, (char)0xfe07, (char)0xfe08, (char)0xfe09, (char)
                                                         0xfe0a, (char)0xfe0b, (char)0xfe0c, (char)0xfe0d, (char)
                                                         0xfe0e, (char)0xfe0f, (char)0xfeff};

        /*
         * Retreived from the UnicodeData.txt file of Unicode version 3.2.0
         */
        private static readonly int[] d1 = new int[]{           1470,
                                                                1472,
                                                                1475,
                                                                1563,
                                                                1567,
                                                                1757,
                                                                1808,
                                                                1969,
                                                                8207,
                                                                64285,
                                                                64318};

        /*
         * Retreived from the UnicodeData.txt file of Unicode version 3.2.0
         */
        private static readonly int[][] d1S = new int[][]{              new int[] {1488,1514},
                                                                        new int[] {1520,1524},
                                                                        new int[] {1569,1594},
                                                                        new int[] {1600,1610},
                                                                        new int[] {1645,1647},
                                                                        new int[] {1649,1749},
                                                                        new int[] {1765,1766},
                                                                        new int[] {1786,1790},
                                                                        new int[] {1792,1805},
                                                                        new int[] {1810,1836},
                                                                        new int[] {1920,1957},
                                                                        new int[] {64287,64296},
                                                                        new int[] {64298,64310},
                                                                        new int[] {64312,64316},
                                                                        new int[] {64320,64321},
                                                                        new int[] {64323,64324},
                                                                        new int[] {64326,64433},
                                                                        new int[] {64467,64829},
                                                                        new int[] {64848,64911},
                                                                        new int[] {64914,64967},
                                                                        new int[] {65008,65020},
                                                                        new int[] {65136,65140},
                                                                        new int[] {65142,65276}};

        /*
         * Retreived from the UnicodeData.txt file of Unicode version 3.2.0
         */
        private static readonly int[] d2 = new int[]{           170,
                                                                181,
                                                                186,
                                                                750,
                                                                890,
                                                                902,
                                                                908,
                                                                1417,
                                                                2307,
                                                                2384,
                                                                2482,
                                                                2519,
                                                                2654,
                                                                2691,
                                                                2701,
                                                                2761,
                                                                2768,
                                                                2784,
                                                                2880,
                                                                2903,
                                                                2947,
                                                                2972,
                                                                3031,
                                                                3262,
                                                                3294,
                                                                3415,
                                                                3517,
                                                                3716,
                                                                3722,
                                                                3725,
                                                                3749,
                                                                3751,
                                                                3773,
                                                                3782,
                                                                3894,
                                                                3896,
                                                                3967,
                                                                3973,
                                                                4047,
                                                                4140,
                                                                4145,
                                                                4152,
                                                                4347,
                                                                4680,
                                                                4696,
                                                                4744,
                                                                4784,
                                                                4800,
                                                                4880,
                                                                6108,
                                                                8025,
                                                                8027,
                                                                8029,
                                                                8126,
                                                                8206,
                                                                8305,
                                                                8319,
                                                                8450,
                                                                8455,
                                                                8469,
                                                                8484,
                                                                8486,
                                                                8488,
                                                                9109,
                                                                13312,
                                                                19893,
                                                                19968,
                                                                40869,
                                                                44032,
                                                                55203,
                                                                55296,
                                                                119970,
                                                                119995,
                                                                120134,
                                                                131072,
                                                                173782,
                                                                983040,
                                                                1048573,
                                                                1048576,
                                                                1114109};

        /*
         * Retreived from the UnicodeData.txt file of Unicode version 3.2.0
         */
        private static readonly int[][] d2S = new int[][]{              new int[] {65,90},
                                                                        new int[] {97,122},
                                                                        new int[] {192,214},
                                                                        new int[] {216,246},
                                                                        new int[] {248,544},
                                                                        new int[] {546,563},
                                                                        new int[] {592,685},
                                                                        new int[] {688,696},
                                                                        new int[] {699,705},
                                                                        new int[] {720,721},
                                                                        new int[] {736,740},
                                                                        new int[] {904,906},
                                                                        new int[] {910,929},
                                                                        new int[] {931,974},
                                                                        new int[] {976,1013},
                                                                        new int[] {1024,1154},
                                                                        new int[] {1162,1230},
                                                                        new int[] {1232,1269},
                                                                        new int[] {1272,1273},
                                                                        new int[] {1280,1295},
                                                                        new int[] {1329,1366},
                                                                        new int[] {1369,1375},
                                                                        new int[] {1377,1415},
                                                                        new int[] {2309,2361},
                                                                        new int[] {2365,2368},
                                                                        new int[] {2377,2380},
                                                                        new int[] {2392,2401},
                                                                        new int[] {2404,2416},
                                                                        new int[] {2434,2435},
                                                                        new int[] {2437,2444},
                                                                        new int[] {2447,2448},
                                                                        new int[] {2451,2472},
                                                                        new int[] {2474,2480},
                                                                        new int[] {2486,2489},
                                                                        new int[] {2494,2496},
                                                                        new int[] {2503,2504},
                                                                        new int[] {2507,2508},
                                                                        new int[] {2524,2525},
                                                                        new int[] {2527,2529},
                                                                        new int[] {2534,2545},
                                                                        new int[] {2548,2554},
                                                                        new int[] {2565,2570},
                                                                        new int[] {2575,2576},
                                                                        new int[] {2579,2600},
                                                                        new int[] {2602,2608},
                                                                        new int[] {2610,2611},
                                                                        new int[] {2613,2614},
                                                                        new int[] {2616,2617},
                                                                        new int[] {2622,2624},
                                                                        new int[] {2649,2652},
                                                                        new int[] {2662,2671},
                                                                        new int[] {2674,2676},
                                                                        new int[] {2693,2699},
                                                                        new int[] {2703,2705},
                                                                        new int[] {2707,2728},
                                                                        new int[] {2730,2736},
                                                                        new int[] {2738,2739},
                                                                        new int[] {2741,2745},
                                                                        new int[] {2749,2752},
                                                                        new int[] {2763,2764},
                                                                        new int[] {2790,2799},
                                                                        new int[] {2818,2819},
                                                                        new int[] {2821,2828},
                                                                        new int[] {2831,2832},
                                                                        new int[] {2835,2856},
                                                                        new int[] {2858,2864},
                                                                        new int[] {2866,2867},
                                                                        new int[] {2870,2873},
                                                                        new int[] {2877,2878},
                                                                        new int[] {2887,2888},
                                                                        new int[] {2891,2892},
                                                                        new int[] {2908,2909},
                                                                        new int[] {2911,2913},
                                                                        new int[] {2918,2928},
                                                                        new int[] {2949,2954},
                                                                        new int[] {2958,2960},
                                                                        new int[] {2962,2965},
                                                                        new int[] {2969,2970},
                                                                        new int[] {2974,2975},
                                                                        new int[] {2979,2980},
                                                                        new int[] {2984,2986},
                                                                        new int[] {2990,2997},
                                                                        new int[] {2999,3001},
                                                                        new int[] {3006,3007},
                                                                        new int[] {3009,3010},
                                                                        new int[] {3014,3016},
                                                                        new int[] {3018,3020},
                                                                        new int[] {3047,3058},
                                                                        new int[] {3073,3075},
                                                                        new int[] {3077,3084},
                                                                        new int[] {3086,3088},
                                                                        new int[] {3090,3112},
                                                                        new int[] {3114,3123},
                                                                        new int[] {3125,3129},
                                                                        new int[] {3137,3140},
                                                                        new int[] {3168,3169},
                                                                        new int[] {3174,3183},
                                                                        new int[] {3202,3203},
                                                                        new int[] {3205,3212},
                                                                        new int[] {3214,3216},
                                                                        new int[] {3218,3240},
                                                                        new int[] {3242,3251},
                                                                        new int[] {3253,3257},
                                                                        new int[] {3264,3268},
                                                                        new int[] {3271,3272},
                                                                        new int[] {3274,3275},
                                                                        new int[] {3285,3286},
                                                                        new int[] {3296,3297},
                                                                        new int[] {3302,3311},
                                                                        new int[] {3330,3331},
                                                                        new int[] {3333,3340},
                                                                        new int[] {3342,3344},
                                                                        new int[] {3346,3368},
                                                                        new int[] {3370,3385},
                                                                        new int[] {3390,3392},
                                                                        new int[] {3398,3400},
                                                                        new int[] {3402,3404},
                                                                        new int[] {3424,3425},
                                                                        new int[] {3430,3439},
                                                                        new int[] {3458,3459},
                                                                        new int[] {3461,3478},
                                                                        new int[] {3482,3505},
                                                                        new int[] {3507,3515},
                                                                        new int[] {3520,3526},
                                                                        new int[] {3535,3537},
                                                                        new int[] {3544,3551},
                                                                        new int[] {3570,3572},
                                                                        new int[] {3585,3632},
                                                                        new int[] {3634,3635},
                                                                        new int[] {3648,3654},
                                                                        new int[] {3663,3675},
                                                                        new int[] {3713,3714},
                                                                        new int[] {3719,3720},
                                                                        new int[] {3732,3735},
                                                                        new int[] {3737,3743},
                                                                        new int[] {3745,3747},
                                                                        new int[] {3754,3755},
                                                                        new int[] {3757,3760},
                                                                        new int[] {3762,3763},
                                                                        new int[] {3776,3780},
                                                                        new int[] {3792,3801},
                                                                        new int[] {3804,3805},
                                                                        new int[] {3840,3863},
                                                                        new int[] {3866,3892},
                                                                        new int[] {3902,3911},
                                                                        new int[] {3913,3946},
                                                                        new int[] {3976,3979},
                                                                        new int[] {4030,4037},
                                                                        new int[] {4039,4044},
                                                                        new int[] {4096,4129},
                                                                        new int[] {4131,4135},
                                                                        new int[] {4137,4138},
                                                                        new int[] {4160,4183},
                                                                        new int[] {4256,4293},
                                                                        new int[] {4304,4344},
                                                                        new int[] {4352,4441},
                                                                        new int[] {4447,4514},
                                                                        new int[] {4520,4601},
                                                                        new int[] {4608,4614},
                                                                        new int[] {4616,4678},
                                                                        new int[] {4682,4685},
                                                                        new int[] {4688,4694},
                                                                        new int[] {4698,4701},
                                                                        new int[] {4704,4742},
                                                                        new int[] {4746,4749},
                                                                        new int[] {4752,4782},
                                                                        new int[] {4786,4789},
                                                                        new int[] {4792,4798},
                                                                        new int[] {4802,4805},
                                                                        new int[] {4808,4814},
                                                                        new int[] {4816,4822},
                                                                        new int[] {4824,4846},
                                                                        new int[] {4848,4878},
                                                                        new int[] {4882,4885},
                                                                        new int[] {4888,4894},
                                                                        new int[] {4896,4934},
                                                                        new int[] {4936,4954},
                                                                        new int[] {4961,4988},
                                                                        new int[] {5024,5108},
                                                                        new int[] {5121,5750},
                                                                        new int[] {5761,5786},
                                                                        new int[] {5792,5872},
                                                                        new int[] {5888,5900},
                                                                        new int[] {5902,5905},
                                                                        new int[] {5920,5937},
                                                                        new int[] {5941,5942},
                                                                        new int[] {5952,5969},
                                                                        new int[] {5984,5996},
                                                                        new int[] {5998,6000},
                                                                        new int[] {6016,6070},
                                                                        new int[] {6078,6085},
                                                                        new int[] {6087,6088},
                                                                        new int[] {6100,6106},
                                                                        new int[] {6112,6121},
                                                                        new int[] {6160,6169},
                                                                        new int[] {6176,6263},
                                                                        new int[] {6272,6312},
                                                                        new int[] {7680,7835},
                                                                        new int[] {7840,7929},
                                                                        new int[] {7936,7957},
                                                                        new int[] {7960,7965},
                                                                        new int[] {7968,8005},
                                                                        new int[] {8008,8013},
                                                                        new int[] {8016,8023},
                                                                        new int[] {8031,8061},
                                                                        new int[] {8064,8116},
                                                                        new int[] {8118,8124},
                                                                        new int[] {8130,8132},
                                                                        new int[] {8134,8140},
                                                                        new int[] {8144,8147},
                                                                        new int[] {8150,8155},
                                                                        new int[] {8160,8172},
                                                                        new int[] {8178,8180},
                                                                        new int[] {8182,8188},
                                                                        new int[] {8458,8467},
                                                                        new int[] {8473,8477},
                                                                        new int[] {8490,8493},
                                                                        new int[] {8495,8497},
                                                                        new int[] {8499,8505},
                                                                        new int[] {8509,8511},
                                                                        new int[] {8517,8521},
                                                                        new int[] {8544,8579},
                                                                        new int[] {9014,9082},
                                                                        new int[] {9372,9449},
                                                                        new int[] {12293,12295},
                                                                        new int[] {12321,12329},
                                                                        new int[] {12337,12341},
                                                                        new int[] {12344,12348},
                                                                        new int[] {12353,12438},
                                                                        new int[] {12445,12447},
                                                                        new int[] {12449,12538},
                                                                        new int[] {12540,12543},
                                                                        new int[] {12549,12588},
                                                                        new int[] {12593,12686},
                                                                        new int[] {12688,12727},
                                                                        new int[] {12784,12828},
                                                                        new int[] {12832,12867},
                                                                        new int[] {12896,12923},
                                                                        new int[] {12927,12976},
                                                                        new int[] {12992,13003},
                                                                        new int[] {13008,13054},
                                                                        new int[] {13056,13174},
                                                                        new int[] {13179,13277},
                                                                        new int[] {13280,13310},
                                                                        new int[] {40960,42124},
                                                                        new int[] {56191,56192},
                                                                        new int[] {56319,56320},
                                                                        new int[] {57343,57344},
                                                                        new int[] {63743,64045},
                                                                        new int[] {64048,64106},
                                                                        new int[] {64256,64262},
                                                                        new int[] {64275,64279},
                                                                        new int[] {65313,65338},
                                                                        new int[] {65345,65370},
                                                                        new int[] {65382,65470},
                                                                        new int[] {65474,65479},
                                                                        new int[] {65482,65487},
                                                                        new int[] {65490,65495},
                                                                        new int[] {65498,65500},
                                                                        new int[] {66304,66334},
                                                                        new int[] {66336,66339},
                                                                        new int[] {66352,66378},
                                                                        new int[] {66560,66597},
                                                                        new int[] {66600,66637},
                                                                        new int[] {118784,119029},
                                                                        new int[] {119040,119078},
                                                                        new int[] {119082,119142},
                                                                        new int[] {119146,119154},
                                                                        new int[] {119171,119172},
                                                                        new int[] {119180,119209},
                                                                        new int[] {119214,119261},
                                                                        new int[] {119808,119892},
                                                                        new int[] {119894,119964},
                                                                        new int[] {119966,119967},
                                                                        new int[] {119973,119974},
                                                                        new int[] {119977,119980},
                                                                        new int[] {119982,119993},
                                                                        new int[] {119997,120000},
                                                                        new int[] {120002,120003},
                                                                        new int[] {120005,120069},
                                                                        new int[] {120071,120074},
                                                                        new int[] {120077,120084},
                                                                        new int[] {120086,120092},
                                                                        new int[] {120094,120121},
                                                                        new int[] {120123,120126},
                                                                        new int[] {120128,120132},
                                                                        new int[] {120138,120144},
                                                                        new int[] {120146,120483},
                                                                        new int[] {120488,120777},
                                                                        new int[] {194560,195101}};

        private static readonly int[] a1 = new int[]{           545,
                                                                907,
                                                                909,
                                                                930,
                                                                975,
                                                                1159,
                                                                1231,
                                                                1376,
                                                                1416,
                                                                1442,
                                                                1466,
                                                                1568,
                                                                1791,
                                                                1806,
                                                                2308,
                                                                2436,
                                                                2473,
                                                                2481,
                                                                2493,
                                                                2526,
                                                                2601,
                                                                2609,
                                                                2612,
                                                                2615,
                                                                2621,
                                                                2653,
                                                                2692,
                                                                2700,
                                                                2702,
                                                                2706,
                                                                2729,
                                                                2737,
                                                                2740,
                                                                2758,
                                                                2762,
                                                                2820,
                                                                2857,
                                                                2865,
                                                                2910,
                                                                2948,
                                                                2961,
                                                                2971,
                                                                2973,
                                                                2998,
                                                                3017,
                                                                3076,
                                                                3085,
                                                                3089,
                                                                3113,
                                                                3124,
                                                                3141,
                                                                3145,
                                                                3204,
                                                                3213,
                                                                3217,
                                                                3241,
                                                                3252,
                                                                3269,
                                                                3273,
                                                                3295,
                                                                3332,
                                                                3341,
                                                                3345,
                                                                3369,
                                                                3401,
                                                                3460,
                                                                3506,
                                                                3516,
                                                                3541,
                                                                3543,
                                                                3715,
                                                                3721,
                                                                3736,
                                                                3744,
                                                                3748,
                                                                3750,
                                                                3756,
                                                                3770,
                                                                3781,
                                                                3783,
                                                                3912,
                                                                3992,
                                                                4029,
                                                                4130,
                                                                4136,
                                                                4139,
                                                                4615,
                                                                4679,
                                                                4681,
                                                                4695,
                                                                4697,
                                                                4743,
                                                                4745,
                                                                4783,
                                                                4785,
                                                                4799,
                                                                4801,
                                                                4815,
                                                                4823,
                                                                4847,
                                                                4879,
                                                                4881,
                                                                4895,
                                                                4935,
                                                                5901,
                                                                5997,
                                                                6001,
                                                                6159,
                                                                8024,
                                                                8026,
                                                                8028,
                                                                8030,
                                                                8117,
                                                                8133,
                                                                8156,
                                                                8181,
                                                                8191,
                                                                9471,
                                                                9752,
                                                                9989,
                                                                10024,
                                                                10060,
                                                                10062,
                                                                10071,
                                                                10160,
                                                                11930,
                                                                12352,
                                                                12687,
                                                                13055,
                                                                13311,
                                                                64311,
                                                                64317,
                                                                64319,
                                                                64322,
                                                                64325,
                                                                65107,
                                                                65127,
                                                                65141,
                                                                65280,
                                                                65511,
                                                                66335,
                                                                119893,
                                                                119965,
                                                                119981,
                                                                119994,
                                                                119996,
                                                                120001,
                                                                120004,
                                                                120070,
                                                                120085,
                                                                120093,
                                                                120122,
                                                                120127,
                                                                120133,
                                                                120145,
                                                                917504};

        private static readonly int[][] a1S = new int[][]{              new int[] {564,591},
                                                                        new int[] {686,687},
                                                                        new int[] {751,767},
                                                                        new int[] {848,863},
                                                                        new int[] {880,883},
                                                                        new int[] {886,889},
                                                                        new int[] {891,893},
                                                                        new int[] {895,899},
                                                                        new int[] {1015,1023},
                                                                        new int[] {1270,1271},
                                                                        new int[] {1274,1279},
                                                                        new int[] {1296,1328},
                                                                        new int[] {1367,1368},
                                                                        new int[] {1419,1424},
                                                                        new int[] {1477,1487},
                                                                        new int[] {1515,1519},
                                                                        new int[] {1525,1547},
                                                                        new int[] {1549,1562},
                                                                        new int[] {1564,1566},
                                                                        new int[] {1595,1599},
                                                                        new int[] {1622,1631},
                                                                        new int[] {1774,1775},
                                                                        new int[] {1837,1839},
                                                                        new int[] {1867,1919},
                                                                        new int[] {1970,2304},
                                                                        new int[] {2362,2363},
                                                                        new int[] {2382,2383},
                                                                        new int[] {2389,2391},
                                                                        new int[] {2417,2432},
                                                                        new int[] {2445,2446},
                                                                        new int[] {2449,2450},
                                                                        new int[] {2483,2485},
                                                                        new int[] {2490,2491},
                                                                        new int[] {2501,2502},
                                                                        new int[] {2505,2506},
                                                                        new int[] {2510,2518},
                                                                        new int[] {2520,2523},
                                                                        new int[] {2532,2533},
                                                                        new int[] {2555,2561},
                                                                        new int[] {2563,2564},
                                                                        new int[] {2571,2574},
                                                                        new int[] {2577,2578},
                                                                        new int[] {2618,2619},
                                                                        new int[] {2627,2630},
                                                                        new int[] {2633,2634},
                                                                        new int[] {2638,2648},
                                                                        new int[] {2655,2661},
                                                                        new int[] {2677,2688},
                                                                        new int[] {2746,2747},
                                                                        new int[] {2766,2767},
                                                                        new int[] {2769,2783},
                                                                        new int[] {2785,2789},
                                                                        new int[] {2800,2816},
                                                                        new int[] {2829,2830},
                                                                        new int[] {2833,2834},
                                                                        new int[] {2868,2869},
                                                                        new int[] {2874,2875},
                                                                        new int[] {2884,2886},
                                                                        new int[] {2889,2890},
                                                                        new int[] {2894,2901},
                                                                        new int[] {2904,2907},
                                                                        new int[] {2914,2917},
                                                                        new int[] {2929,2945},
                                                                        new int[] {2955,2957},
                                                                        new int[] {2966,2968},
                                                                        new int[] {2976,2978},
                                                                        new int[] {2981,2983},
                                                                        new int[] {2987,2989},
                                                                        new int[] {3002,3005},
                                                                        new int[] {3011,3013},
                                                                        new int[] {3022,3030},
                                                                        new int[] {3032,3046},
                                                                        new int[] {3059,3072},
                                                                        new int[] {3130,3133},
                                                                        new int[] {3150,3156},
                                                                        new int[] {3159,3167},
                                                                        new int[] {3170,3173},
                                                                        new int[] {3184,3201},
                                                                        new int[] {3258,3261},
                                                                        new int[] {3278,3284},
                                                                        new int[] {3287,3293},
                                                                        new int[] {3298,3301},
                                                                        new int[] {3312,3329},
                                                                        new int[] {3386,3389},
                                                                        new int[] {3396,3397},
                                                                        new int[] {3406,3414},
                                                                        new int[] {3416,3423},
                                                                        new int[] {3426,3429},
                                                                        new int[] {3440,3457},
                                                                        new int[] {3479,3481},
                                                                        new int[] {3518,3519},
                                                                        new int[] {3527,3529},
                                                                        new int[] {3531,3534},
                                                                        new int[] {3552,3569},
                                                                        new int[] {3573,3584},
                                                                        new int[] {3643,3646},
                                                                        new int[] {3676,3712},
                                                                        new int[] {3717,3718},
                                                                        new int[] {3723,3724},
                                                                        new int[] {3726,3731},
                                                                        new int[] {3752,3753},
                                                                        new int[] {3774,3775},
                                                                        new int[] {3790,3791},
                                                                        new int[] {3802,3803},
                                                                        new int[] {3806,3839},
                                                                        new int[] {3947,3952},
                                                                        new int[] {3980,3983},
                                                                        new int[] {4045,4046},
                                                                        new int[] {4048,4095},
                                                                        new int[] {4147,4149},
                                                                        new int[] {4154,4159},
                                                                        new int[] {4186,4255},
                                                                        new int[] {4294,4303},
                                                                        new int[] {4345,4346},
                                                                        new int[] {4348,4351},
                                                                        new int[] {4442,4446},
                                                                        new int[] {4515,4519},
                                                                        new int[] {4602,4607},
                                                                        new int[] {4686,4687},
                                                                        new int[] {4702,4703},
                                                                        new int[] {4750,4751},
                                                                        new int[] {4790,4791},
                                                                        new int[] {4806,4807},
                                                                        new int[] {4886,4887},
                                                                        new int[] {4955,4960},
                                                                        new int[] {4989,5023},
                                                                        new int[] {5109,5120},
                                                                        new int[] {5751,5759},
                                                                        new int[] {5789,5791},
                                                                        new int[] {5873,5887},
                                                                        new int[] {5909,5919},
                                                                        new int[] {5943,5951},
                                                                        new int[] {5972,5983},
                                                                        new int[] {6004,6015},
                                                                        new int[] {6109,6111},
                                                                        new int[] {6122,6143},
                                                                        new int[] {6170,6175},
                                                                        new int[] {6264,6271},
                                                                        new int[] {6314,7679},
                                                                        new int[] {7836,7839},
                                                                        new int[] {7930,7935},
                                                                        new int[] {7958,7959},
                                                                        new int[] {7966,7967},
                                                                        new int[] {8006,8007},
                                                                        new int[] {8014,8015},
                                                                        new int[] {8062,8063},
                                                                        new int[] {8148,8149},
                                                                        new int[] {8176,8177},
                                                                        new int[] {8275,8278},
                                                                        new int[] {8280,8286},
                                                                        new int[] {8292,8297},
                                                                        new int[] {8306,8307},
                                                                        new int[] {8335,8351},
                                                                        new int[] {8370,8399},
                                                                        new int[] {8427,8447},
                                                                        new int[] {8507,8508},
                                                                        new int[] {8524,8530},
                                                                        new int[] {8580,8591},
                                                                        new int[] {9167,9215},
                                                                        new int[] {9255,9279},
                                                                        new int[] {9291,9311},
                                                                        new int[] {9748,9749},
                                                                        new int[] {9854,9855},
                                                                        new int[] {9866,9984},
                                                                        new int[] {9994,9995},
                                                                        new int[] {10067,10069},
                                                                        new int[] {10079,10080},
                                                                        new int[] {10133,10135},
                                                                        new int[] {10175,10191},
                                                                        new int[] {10220,10223},
                                                                        new int[] {11008,11903},
                                                                        new int[] {12020,12031},
                                                                        new int[] {12246,12271},
                                                                        new int[] {12284,12287},
                                                                        new int[] {12439,12440},
                                                                        new int[] {12544,12548},
                                                                        new int[] {12589,12592},
                                                                        new int[] {12728,12783},
                                                                        new int[] {12829,12831},
                                                                        new int[] {12868,12880},
                                                                        new int[] {12924,12926},
                                                                        new int[] {13004,13007},
                                                                        new int[] {13175,13178},
                                                                        new int[] {13278,13279},
                                                                        new int[] {19894,19967},
                                                                        new int[] {40870,40959},
                                                                        new int[] {42125,42127},
                                                                        new int[] {42183,44031},
                                                                        new int[] {55204,55295},
                                                                        new int[] {64046,64047},
                                                                        new int[] {64107,64255},
                                                                        new int[] {64263,64274},
                                                                        new int[] {64280,64284},
                                                                        new int[] {64434,64466},
                                                                        new int[] {64832,64847},
                                                                        new int[] {64912,64913},
                                                                        new int[] {64968,64975},
                                                                        new int[] {65021,65023},
                                                                        new int[] {65040,65055},
                                                                        new int[] {65060,65071},
                                                                        new int[] {65095,65096},
                                                                        new int[] {65132,65135},
                                                                        new int[] {65277,65278},
                                                                        new int[] {65471,65473},
                                                                        new int[] {65480,65481},
                                                                        new int[] {65488,65489},
                                                                        new int[] {65496,65497},
                                                                        new int[] {65501,65503},
                                                                        new int[] {65519,65528},
                                                                        new int[] {65536,66303},
                                                                        new int[] {66340,66351},
                                                                        new int[] {66379,66559},
                                                                        new int[] {66598,66599},
                                                                        new int[] {66638,118783},
                                                                        new int[] {119030,119039},
                                                                        new int[] {119079,119081},
                                                                        new int[] {119262,119807},
                                                                        new int[] {119968,119969},
                                                                        new int[] {119971,119972},
                                                                        new int[] {119975,119976},
                                                                        new int[] {120075,120076},
                                                                        new int[] {120135,120137},
                                                                        new int[] {120484,120487},
                                                                        new int[] {120778,120781},
                                                                        new int[] {120832,131069},
                                                                        new int[] {173783,194559},
                                                                        new int[] {195102,196605},
                                                                        new int[] {196608,262141},
                                                                        new int[] {262144,327677},
                                                                        new int[] {327680,393213},
                                                                        new int[] {393216,458749},
                                                                        new int[] {458752,524285},
                                                                        new int[] {524288,589821},
                                                                        new int[] {589824,655357},
                                                                        new int[] {655360,720893},
                                                                        new int[] {720896,786429},
                                                                        new int[] {786432,851965},
                                                                        new int[] {851968,917501},
                                                                        new int[] {917506,917535},
                                                                        new int[] {917632,983037}};

        #endregion


        #region Java Funktionen

        private static int CodePointAt(string input, int index)
        {
            /* From JAVA Documentation
             * 
             * public int codePointAt(int index)
             * 
             * Returns the character (Unicode code point) at the specified index. The index refers to char values (Unicode code units) and 
             * ranges from 0 to length() - 1.
             * 
             * If the char value specified at the given index is in the high-surrogate range, the following index is less than the length 
             * of this String, and the char value at the following index is in the low-surrogate range, then the supplementary code point 
             * corresponding to this surrogate pair is returned. Otherwise, the char value at the given index is returned.
             */

            if (char.IsHighSurrogate(input[index]) && char.IsLowSurrogate(input[index + 1]) && (index + 1 < input.Length))
                return char.ConvertToUtf32(input, index);

            else
                return (int)input[index];

        }

        #endregion
                
    }
}
