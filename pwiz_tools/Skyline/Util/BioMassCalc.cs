/*
 * Original author: Brendan MacLean <brendanx .at. u.washington.edu>,
 *                  MacCoss Lab, Department of Genome Sciences, UW
 *
 * Copyright 2009 University of Washington - Seattle, WA
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using pwiz.Common.Chemistry;
using pwiz.Skyline.Model.DocSettings;
using pwiz.Skyline.Properties;

namespace pwiz.Skyline.Util
{
    /// <summary>
    /// Enum used to specify the use of monoisotopic or average
    /// masses when calculating molecular masses.
    /// </summary>
    [Flags]
    public enum MassType
    {
// ReSharper disable InconsistentNaming
        Monoisotopic = 0, 
        Average = 1,
        bMassH = 2, // As with peptides, where masses are traditionally given as massH
        bHeavy = 4, // As with small molecules described by mass only, which have already been processed by isotope-declaring adducts
        MonoisotopicMassH = Monoisotopic | bMassH, 
        AverageMassH = Average | bMassH,
        MonoisotopicHeavy = Monoisotopic | bHeavy, 
        AverageHeavy = Average | bHeavy
// ReSharper restore InconsistentNaming
    }
    public static class MassTypeExtension
    {
        private static string[] LOCALIZED_VALUES
        {
            get
            {
                return new[]
                {
                    Resources.ExportStrategyExtension_LOCALIZED_VALUES_Monoisotopic,
                    Resources.ExportStrategyExtension_LOCALIZED_VALUES_Average
                };
            }
        }
        public static string GetLocalizedString(this MassType val)
        {
            return LOCALIZED_VALUES[(int)val & (int)MassType.Average]; // Strip off bMassH, bHeavy
        }

        public static MassType GetEnum(string enumValue)
        {
            return Helpers.EnumFromLocalizedString<MassType>(enumValue, LOCALIZED_VALUES);
        }

        public static MassType GetEnum(string enumValue, MassType defaultValue)
        {
            return Helpers.EnumFromLocalizedString(enumValue, LOCALIZED_VALUES, defaultValue);
        }
        [Pure]
        public static bool IsMonoisotopic(this MassType val)
        {
            return !val.IsAverage();
        }

        [Pure]
        public static bool IsAverage(this MassType val)
        {
            return (val & MassType.Average) != 0;
        }

        [Pure]
        public static bool IsMassH(this MassType val)
        {
            return (val & MassType.bMassH) != 0;
        }
        
        // For small molecule use: distinguishes a mass calculated from an isotope-specifying adduct
        [Pure]
        public static bool IsHeavy(this MassType val)
        {
            return (val & MassType.bHeavy) != 0;  
        }
    }

    /// <summary>
    /// There are many places where we carry a mass or massH and also need to track how it was derived
    /// </summary>
    public struct TypedMass :  IComparable<TypedMass>, IEquatable<TypedMass>, IFormattable
    {
        public static TypedMass ZERO_AVERAGE_MASSNEUTRAL = new TypedMass(0.0, MassType.Average);
        public static TypedMass ZERO_MONO_MASSNEUTRAL = new TypedMass(0.0, MassType.Monoisotopic);

        public static TypedMass ZERO_AVERAGE_MASSH = new TypedMass(0.0, MassType.AverageMassH);
        public static TypedMass ZERO_MONO_MASSH = new TypedMass(0.0, MassType.MonoisotopicMassH);

        private readonly double _value;
        private readonly MassType _massType;

        public double Value { get { return _value; } }
        public MassType MassType { get { return _massType; } }
        [Pure]
        public bool IsMassH() { return _massType.IsMassH();  }
        [Pure]
        public bool IsMonoIsotopic() { return _massType.IsMonoisotopic(); }
        [Pure]
        public bool IsAverage() { return _massType.IsAverage(); }
        [Pure]
        public bool IsHeavy() { return _massType.IsHeavy(); }

        public TypedMass(double value, MassType t)
        {
            _value = value;
            _massType = t;
        }

        [Pure]
        public bool Equivalent(TypedMass other)
        {
            if (IsMassH() != other.IsMassH())
            {
                var adjust = IsMassH() ? -BioMassCalc.MassProton : BioMassCalc.MassProton;
                return Math.Abs(_value + adjust - other.Value) < BioMassCalc.MassElectron;
            }
            return Equals(other); // Can't lead with this, as it will throw if IsMassH doesn't agree
        }

        public TypedMass ChangeIsMassH(bool newIsMassH)
        {
            if (Equals(newIsMassH, IsMassH()))
            {
                return this;
            }
            return new TypedMass(_value, newIsMassH ? _massType | MassType.bMassH : _massType & ~MassType.bMassH);
        }

        public static implicit operator double(TypedMass d)
        {
            return d.Value;
        }

        public static TypedMass operator +(TypedMass tm, double step)
        {
            return new TypedMass(tm.Value + step, tm._massType);
        }

        public static TypedMass operator -(TypedMass tm, double step)
        {
            return new TypedMass(tm.Value - step, tm._massType);
        }

        public int CompareTo(TypedMass other)
        {
            Assume.IsTrue(_massType == other._massType);  // It's a mistake to mix these types
            return Value.CompareTo(other.Value);
        }

        public bool Equals(TypedMass other)
        {
            return CompareTo(other) == 0;
        }

        public bool Equals(TypedMass other, double tolerance)
        {
            return CompareTo(other) == 0 || Math.Abs(Value - other.Value) <= tolerance;
        }

        public override int GetHashCode()
        {
            var result = Value.GetHashCode();
            result = (result * 397) ^ _massType.GetHashCode();
            return result;
        }

        public override string ToString()
        {
            return Value.ToString(CultureInfo.CurrentCulture);
        }

        public string ToString(CultureInfo ci)
        {
            return Value.ToString(ci);
        }

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return Value.ToString(format, formatProvider);
        }
    }

    /// <summary>
    /// Calculates molecular masses based on atomic masses.
    /// Atomic masses come from http://www.unimod.org/unimod_help.html.
    /// Some heavy isotopes come from pwiz/utility/chemistry/isotopes.text, which
    /// comes from http://physics.nist.gov/PhysRefData/Compositions/index.html
    /// The average mass of Carbon comes from Michael MacCoss, which he claims
    /// was derived by Dwight Matthews and John Hayes in the 70s.  It takes into
    /// account carbon 12 enrichment in living organisms:
    /// 
    /// http://www.madsci.org/posts/archives/2003-06/1055532737.Bc.r.html
    /// 
    /// But at 12.01085 is slightly higher than the current Unimod standard
    /// of 12.0107.
    ///  </summary>
    public class BioMassCalc
    {
        public static readonly BioMassCalc MONOISOTOPIC = new BioMassCalc(MassType.Monoisotopic);
        public static readonly BioMassCalc AVERAGE = new BioMassCalc(MassType.Average);

        public static readonly IsotopeAbundances DEFAULT_ABUNDANCES =
            MONOISOTOPIC.SynchMasses(IsotopeAbundances.Default);

        
// ReSharper disable NonLocalizedString
        public const string H = "H";    // Hydrogen
        public const string H2 = "H'";  // Deuterium
        public const string H3 = "H\""; // Tritium
        public const string D = "D";  // Deuterium - IUPAC standard
        public const string T = "T";  // Tritium - IUPAC standard
        public const string C = "C";    // Carbon
        public const string C13 = "C'"; // Carbon13
        public const string N = "N";    // Nitrogen
        public const string N15 = "N'"; // Nitrogen15
        public const string O = "O";    // Oxygen
        public const string O17 = "O\"";// Oxygen17
        public const string O18 = "O'"; // Oxygen18
        public const string P = "P";    // Phosphorus
        public const string P32 = "P'";    // Phosphorus32 (radioisotope, trace natural abundance)
        public const string S = "S";    // Sulfur
        public const string S34 = "S'";    // Sulfur34 (4.2% natural abundance)
        public const string S33 = "S\"";    // Sulfur33 (0.75% natural abundance)
// ReSharper disable InconsistentNaming
        public const string Se = "Se";  // Selenium
        public const string Li = "Li";  // Lithium
        public const string F = "F";    // Fluorine
        public const string Na = "Na";  // Sodium
        public const string Cl = "Cl";  // Chlorine
        public const string Cl37 = "Cl'";  // Chlorine37
        public const string K = "K";    // Potassium
        public const string Ca = "Ca";  // Calcium
        public const string Fe = "Fe";  // Iron
        public const string Ni = "Ni";  // Nickle
        public const string Cu = "Cu";  // Copper
        public const string Zn = "Zn";  // Zinc
        public const string Br = "Br";  // Bromine
        public const string Br81 = "Br'";  // Bromine81
        public const string Mo = "Mo";  // Molybdenum
        public const string Ag = "Ag";  // Silver
        public const string I = "I";    // Iodine
        public const string Au = "Au";  // Gold
        public const string Hg = "Hg";  // Mercury
        public const string B = "B";    // Boron
        public const string As = "As";  // Arsenic
        public const string Cd = "Cd";  // Cadmium
        public const string Cr = "Cr";  // Chromium
        public const string Co = "Co";  // Cobalt
        public const string Mn = "Mn";  // Manganese
        public const string Mg = "Mg";  // Magnesium
        public const string Si = "Si";  //Silicon
        // ReSharper restore NonLocalizedString
// ReSharper restore InconsistentNaming


        /// <summary>
        /// A dictionary mapping heavy isotope symbols to their corresponding monoisotopic element.
        /// This dictionary contains entries for Skyline-style isotope symbols (e.g. H' for Deuterium -> H)
        /// as well as common synonyms (e.g. D for Deuterium -> H)
        /// </summary>
        public static readonly Dictionary<string, string> DICT_SYMBOL_TO_MONOISOTOPE; // Map Cl' to Cl, D to H etc

        /// <summary>
        /// A dictionary mapping heavy isotope symbols to their correspoding
        /// indices within the mass distributions of <see cref="IsotopeAbundances.Default"/>,
        /// and default atom percent enrichment for <see cref="IsotopeEnrichmentItem"/>.
        /// This dictionary contains entries for Skyline-style isotope symbols (e.g. H' for Deuterium)
        /// as well as common synonyms (e.g. D for Deuterium)
        /// </summary>
        private static readonly IDictionary<string, KeyValuePair<int, double>> DICT_SYMBOL_TO_ISOTOPE_INDEX;
        private static readonly IDictionary<string, KeyValuePair<int, double>> DICT_NATIVE_SYMBOL_TO_ISOTOPE_INDEX; // Containing only the H', O" varieties - not D or T

        static BioMassCalc()
        {
            DICT_NATIVE_SYMBOL_TO_ISOTOPE_INDEX =
            new Dictionary<string, KeyValuePair<int, double>>
                {
                    { H2, new KeyValuePair<int, double>(1, 0.98) },
                    { C13, new KeyValuePair<int, double>(1, 0.995) },
                    { N15, new KeyValuePair<int, double>(1, 0.995) },
                    { O17, new KeyValuePair<int, double>(1, 0.99) },
                    { O18, new KeyValuePair<int, double>(2, 0.99) },
                    { Cl37, new KeyValuePair<int, double>(1, 0.99) },  // N.B. No idea if this is a realistic value
                    { Br81, new KeyValuePair<int, double>(1, 0.99) },  // N.B. No idea if this is a realistic value 
                    { P32, new KeyValuePair<int, double>(1, 0.99) },  // N.B. No idea if this is a realistic value 
                    { S33, new KeyValuePair<int, double>(1, 0.99) },  // N.B. No idea if this is a realistic value 
                    { S34, new KeyValuePair<int, double>(2, 0.99) },  // N.B. No idea if this is a realistic value 
                    { H3, new KeyValuePair<int, double>(2, 0.99) },  // N.B. No idea if this is a realistic value 
                };
            DICT_SYMBOL_TO_ISOTOPE_INDEX = new Dictionary<string, KeyValuePair<int, double>>();
            foreach (var kvp in DICT_NATIVE_SYMBOL_TO_ISOTOPE_INDEX)
            {
                DICT_SYMBOL_TO_ISOTOPE_INDEX.Add(kvp.Key, kvp.Value);
            }

            // Map Cl' to Cl etc
            DICT_SYMBOL_TO_MONOISOTOPE = new Dictionary<string, string>();
            foreach (var key in DICT_SYMBOL_TO_ISOTOPE_INDEX.Keys) // For each Skyline-style isotope (e.g. N' or O")
            {
                DICT_SYMBOL_TO_MONOISOTOPE.Add(key,
                    key.Replace("'", string.Empty).Replace("\"", string.Empty)); // Not L10N
            }
            // Extend isotope symbol dicts to include supported synonyms like D for H'
            foreach (var kvp in GetDictIsotopeNicknames()) 
            {
                DICT_SYMBOL_TO_MONOISOTOPE.Add(kvp.Key, DICT_SYMBOL_TO_MONOISOTOPE[kvp.Value]);
                DICT_SYMBOL_TO_ISOTOPE_INDEX.Add(kvp.Key, DICT_SYMBOL_TO_ISOTOPE_INDEX[kvp.Value]);
            }
        }

        /// <summary>
        // Returns a dictionary of common isotope representations (e.g. IUPAC's D for Deuterium) to Skyline's representation.
        // CONSIDER(bspratt) would be trivial to add support for pwiz-style _2H -> H' _37Cl-> CL' etc
        // NB if you do so, make sure to update BiblioSpec BuildParser.cpp which explicitly rejects '_' in formulas
        /// </summary>
        private static IDictionary<string, string> GetDictIsotopeNicknames()
        {
            return new Dictionary<string, string>
            {
                {D, H2}, // IUPAC Deuterium
                {T, H3}  // IUPAC Tritium
            };
        }

        public static IEnumerable<string> HeavySymbols { get { return DICT_NATIVE_SYMBOL_TO_ISOTOPE_INDEX.Keys; } }

        /// <summary>
        /// Returns the index of an atomic symbol the mass distribution
        /// from <see cref="IsotopeAbundances.Default"/>.
        /// </summary>
        public static int GetIsotopeDistributionIndex(string symbol)
        {
            KeyValuePair<int, double> pair;
            if (DICT_SYMBOL_TO_ISOTOPE_INDEX.TryGetValue(symbol, out pair))
                return pair.Key;
            return 0;
        }

        /// <summary>
        /// Returns the default atom percent enrichment for a heavy labeled atom.
        /// </summary>
        public static double GetIsotopeEnrichmentDefault(string symbol)
        {
            KeyValuePair<int, double> pair;
            if (DICT_SYMBOL_TO_ISOTOPE_INDEX.TryGetValue(symbol, out pair))
                return pair.Value;
            return 0;
        }

        /// <summary>
        /// Returns the monoisotopic symbol for the atomic symbols associated
        /// with <see cref="BioMassCalc"/>.
        /// </summary>
        public static string GetMonoisotopicSymbol(string symbol)
        {
            string monoisotopicSymbol;
            return DICT_SYMBOL_TO_MONOISOTOPE.TryGetValue(symbol, out monoisotopicSymbol) ? monoisotopicSymbol : symbol;
        }

        public static double MassProton
        {
            //get { return AminoAcidFormulas.ProtonMass; } // was "1.007276" here, let's be consistent and use the higher precision value
            get { return 1.007276; }
        }

        public static double MassElectron
        {
          //get { return 0.000548579909070; }  // per http://physics.nist.gov/cgi-bin/cuu/Value?meu|search_for=electron+mass 12/18/2016
            get { return 0.00054857990946; } // per http://physics.nist.gov/cgi-bin/cuu/Value?meu|search_for=electron+mass
        }

        public readonly double MassH; // For dealing with non-protonated ionization in peptides

        /// <summary>
        /// Regular expression for possible characters that end an atomic
        /// symbol: capital letters, numbers or a space.
        /// </summary>
        private static readonly Regex REGEX_END_SYM = new Regex(@"[A-Z0-9 \-]");  // Not L10N

        /// <summary>
        /// Find the first atomic symbol in a given expression.
        /// </summary>
        /// <param name="expression">The expression to search</param>
        /// <returns>The first atomic symbol</returns>
        private static string NextSymbol(string expression)
        {
            // Skip the first character, since it is always the start of
            // the symbol, and then look for the end of the symbol.
            Match match = REGEX_END_SYM.Match(expression, 1);
            if (!match.Success)
                return expression;
            return expression.Substring(0, match.Index);
        }

        private readonly Dictionary<string, double> _atomicMasses =
            new Dictionary<string, double>();

        /// <summary>
        /// Create a simple mass calculator for use in calculating
        /// protein, peptide and fragment masses.
        /// </summary>
        /// <param name="type">Monoisotopic or average mass calculations</param>
        public BioMassCalc(MassType type)
        {
            MassType = type;
            AddMass(H, 1.007825035, 1.00794); //Unimod
            MassH = _atomicMasses[H]; // For dealing with non-prononated ioninzation in peptides
            AddMass(H2, 2.014101779, 2.014101779); //Unimod
            var massH3 = IsotopeAbundances.Default[H].Keys[2]; // Just be consistent with the isotope masses we already use
            AddMass(H3, massH3, massH3);
            AddMass(O, 15.99491463, 15.9994); //Unimod
            AddMass(O17, 16.9991315, 16.9991315); //NIST
            AddMass(O18, 17.9991604, 17.9991604); //NIST, Unimod=17.9991603
            AddMass(N, 14.003074, 14.0067); //Unimod
            AddMass(N15, 15.0001088984, 15.0001088984); //NIST, Unimod=15.00010897
            AddMass(C, 12.0, 12.01085); //MacCoss average
            AddMass(C13, 13.0033548378, 13.0033548378); //NIST, Unimod=13.00335483
            AddMass(S, 31.9720707, 32.065); //Unimod
            AddMass(P, 30.973762, 30.973761); //Unimod
            AddMass(P32, 31.973907274, 31.973907274); // Wikipedia and http://periodictable.com/Isotopes/015.32/index3.p.full.html using Wolfram
            AddMass(Se, 79.9165196, 78.96); //Unimod, Most abundant Se isotope is 80
            AddMass(Li, 7.016003, 6.941); //Unimod
            AddMass(F, 18.99840322, 18.9984032); //Unimod
            AddMass(Na, 22.9897677, 22.98977); //Unimod
            AddMass(S, 31.9720707, 32.065); //Unimod
            var massS33 = IsotopeAbundances.Default[S].Keys[1]; // Just be consistent with the isotope masses we already use
            AddMass(S33, massS33, massS33);
            var massS34 = IsotopeAbundances.Default[S].Keys[2];  // Just be consistent with the isotope masses we already use
            AddMass(S34, massS34, massS34);
            AddMass(Cl, 34.96885272, 35.453); //Unimod
            AddMass(Cl37, 36.965902602, 36.965902602); //NIST
            AddMass(K, 38.9637074, 39.0983); //Unimod
            AddMass(Ca, 39.9625906, 40.078); //Unimod
            AddMass(Fe, 55.9349393, 55.845); //Unimod
            AddMass(Ni, 57.9353462, 58.6934); //Unimod
            AddMass(Cu, 62.9295989, 63.546); //Unimod
            AddMass(Zn, 63.9291448, 65.409); //Unimod
            AddMass(Br, 78.9183361, 79.904); //Unimod
            AddMass(Br81, 80.9162897, 80.9162897); //NIST
            AddMass(Mo, 97.9054073, 95.94); //Unimod
            AddMass(Ag, 106.905092, 107.8682); //Unimod
            AddMass(I, 126.904473, 126.90447); //Unimod
            AddMass(Au, 196.966543, 196.96655); //Unimod
            AddMass(Hg, 201.970617, 200.59); //Unimod
            AddMass(B, 11.0093055, 10.811);
            AddMass(As, 74.9215942, 74.9215942);
            AddMass(Cd, 113.903357, 112.411);
            AddMass(Cr, 51.9405098, 51.9961);
            AddMass(Co, 58.9331976, 58.933195);
            AddMass(Mn, 54.9380471, 54.938045);
            AddMass(Mg, 23.9850423, 24.305);
            AddMass(Si, 27.9769265, 28.085); // Per Wikipedia

            // Add entries for isotope synonyms like D (H') and T (H")
            foreach (var kvp in GetDictIsotopeNicknames()) 
            {
                _atomicMasses.Add(kvp.Key, _atomicMasses[kvp.Value]);
            }
        }

        public MassType MassType { get; private set; }

        public string FormatArgumentExceptionMessage(string desc)
        {
            string errmsg =
                string.Format(
                    Resources.BioMassCalc_CalculateMass_The_expression__0__is_not_a_valid_chemical_formula, desc) +
                Resources.BioMassCalc_FormatArgumentException__Supported_chemical_symbols_include__;
            foreach (var key in _atomicMasses.Keys)
                errmsg += key + " "; // Not L10N
            return errmsg;
        }

        public void ThrowArgumentException(string desc)
        {
            throw new ArgumentException(FormatArgumentExceptionMessage(desc));
        }

        public static bool ContainsIsotopicElement(string desc)
        {
            return DICT_SYMBOL_TO_MONOISOTOPE.Keys.Any(desc.Contains); // Look for Cl', O", D, T etc
        }

        /// <summary>
        /// Calculate the mass of a molecule specified as a character
        /// string like "C6H11ON", or "[{atom}[count][spaces]]*", where the
        /// atoms are chemical symbols like H, or C, or C' etc.
        /// </summary>
        /// <param name="desc">The molecule description string</param>
        /// <returns>The mass of the specified molecule</returns>
        public TypedMass CalculateMassFromFormula(string desc)
        {
            string parse = desc;
            double totalMass = ParseMassExpression(ref parse);

            if (totalMass == 0.0 || parse.Length > 0)
                ThrowArgumentException(desc);
            var massType = ContainsIsotopicElement(desc) ?
                MassType.IsAverage() ? MassType.AverageHeavy : MassType.MonoisotopicHeavy : // Formula contained isotope declaration
                MassType.IsAverage() ? MassType.Average : MassType.Monoisotopic;

            return new TypedMass(totalMass, massType);
        }

        public TypedMass CalculateMassFromFormula(IDictionary<string, int> desc)
        {
            double totalMass = ParseMass(desc);

            if (desc.Count > 0 && totalMass == 0.0) // Non-empty description should produce a mass
            {
                ThrowArgumentException(desc.ToString());
            }

            return new TypedMass(totalMass, MassType);
        }

        /// <summary>
        /// Turn a formula like C5H9H'3NO2S into C5H12NO2S
        /// </summary>
        public string StripLabelsFromFormula(string desc)
        {
            if (string.IsNullOrEmpty(desc))
                return null;
            var parse = DICT_SYMBOL_TO_MONOISOTOPE.Aggregate(desc, (current, kvp) => current.Replace(kvp.Key, kvp.Value));
            var dictAtomCounts = new Dictionary<string, int>();
            ParseCounts(ref parse, dictAtomCounts, false);
            if (!string.IsNullOrEmpty(parse))
            {
                return desc; // That wasn't understood as a formula
            }
            return dictAtomCounts.Aggregate(string.Empty, (current, pair) => current + string.Format(CultureInfo.InvariantCulture, "{0}{1}", pair.Key, (pair.Value>1) ? pair.Value.ToString() : string.Empty)); // Not L10N
        }

        /// <summary>
        /// Find the C'3H'3 in  C'3C2H9H'3NO2S
        /// </summary>
        public IDictionary<string, int> FindIsotopeLabelsInFormula(string desc)
        {
            if (string.IsNullOrEmpty(desc))
                return null;
            var parse = desc;
            var dictAtomCounts = new Dictionary<string, int>();
            ParseCounts(ref parse, dictAtomCounts, false);
            return dictAtomCounts.Where(pair => DICT_SYMBOL_TO_MONOISOTOPE.ContainsKey(pair.Key)).ToDictionary(p => p.Key, p => p.Value); 
        }

        /// <summary>
        // Find the intersection of a list of formulas, ignoring labels
        // e.g. for C12H3H'2S2, C10H5, and C10H4Nz, return C10H4
        /// </summary>
        public string FindFormulaIntersectionUnlabeled(IEnumerable<string> formulas)
        {
            var unlabeled = formulas.Select(f => MONOISOTOPIC.StripLabelsFromFormula(f)).ToList();
            return FindFormulaIntersection(unlabeled);
        }

        /// <summary>
        // Find the intersection of a list of formulas
        // e.g. for C12H5S2, C10H5, and C10H4Nz, return C10H4
        /// </summary>
        public string FindFormulaIntersection(IList<string> formulas)
        {
            if (formulas.Count == 0)
                return string.Empty;
            if (formulas.Count == 1)
                return formulas[0];
            if (formulas.Count == 2 && string.Equals(formulas[0], formulas[1]))
                return formulas[0];
            var common = Molecule.ParseExpressionToDictionary(formulas[0]);
            for (var i = 1; i < formulas.Count; i++)
            {
                var next = Molecule.ParseExpression(formulas[i]);
                foreach (var kvp in next)
                {
                    int count;
                    if (common.TryGetValue(kvp.Key, out count))
                    {
                        common[kvp.Key] = Math.Min(count, kvp.Value);
                    }
                }
                foreach (var kvp in common)
                {
                    if (!next.ContainsKey(kvp.Key) || next[kvp.Key] == 0)
                    {
                        common[kvp.Key] = 0;
                    }
                }
            }
            return Molecule.FromDict(common).ToString();
        }

        /// <summary>
        /// For test purposes
        /// </summary>
        public double CalculateIonMz(string desc, Adduct adduct)
        {
            var mass = CalculateMassFromFormula(desc);
            return adduct.MzFromNeutralMass(mass);
        }

        /// <summary>
        /// For test purposes
        /// </summary>
        public static double CalculateIonMz(TypedMass mass, Adduct adduct)
        {
            return adduct.MzFromNeutralMass(mass);
        }

        /// <summary>
        /// For test purposes
        /// </summary>
        public static double CalculateIonMass(TypedMass mass, Adduct adduct)
        {
            return adduct.ApplyToMass(mass);
        }

        /// <summary>
        /// For fixing up old custom ion formulas in which we artificially
        /// reduced the hydrogen count by one, in anticipation of our
        /// calculations adding it back in because they thought that was
        /// the only kind of ionization.  Now we assume that the formula is that
        /// of the ion, and don't perform protonation by adding a hydrogen mass
        /// </summary>
        /// <param name="formula">the formula that needs an H added</param>
        /// <returns></returns>
        static public string AddH(string formula)
        {
            bool foundH = false;
            string result = string.Empty;
            string desc = formula;
            desc = desc.Trim();
            while (desc.Length > 0)
            {
                string sym = NextSymbol(desc);
                double massAtom = AVERAGE.GetMass(sym);

                // Stop if unrecognized atom found.
                if (massAtom == 0)
                {
                    // CONSIDER: Throw with a useful message?
                    break;
                }
                result += sym;
                desc = desc.Substring(sym.Length);
                int endCount = 0;
                while (endCount < desc.Length && Char.IsDigit(desc[endCount]))
                    endCount++;

                if (sym == H)
                {
                    foundH = true;
                    int count = 1;
                    if (endCount > 0)
                        count = int.Parse(desc.Substring(0, endCount), CultureInfo.InvariantCulture);
                    result += (count + 1).ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    result += desc.Substring(0, endCount);
                }
                desc = desc.Substring(endCount).TrimStart();
            }
            if (!foundH)  // CONSIDER: bspratt is this really what we want?  CO2 -> CO2H?
                result +=  H; 
            return result;
        }

        /// <summary>
        /// Parses a chemical formula expressed as "[{atom}[count][spaces]]*",
        /// e.g. "C6H11ON", where supported atoms are H, O, N, C, S or P, etc.
        /// returning the total mass for the formula.
        /// 
        /// The parser removes atoms and counts until it encounters a character
        /// it does not understand as being part of the chemical formula.
        /// The remainder is returned in the desc parameter.
        /// 
        /// This parser will stop at the first minus sign. If you need to parse
        /// an expression that might contain a minus sign, use <see cref="ParseMassExpression"/>.
        /// </summary>
        /// <param name="desc">Input description, and remaining string after parsing</param>
        /// <returns>Total mass of formula parsed</returns>
        public double ParseMass(ref string desc)
        {
            double totalMass = 0.0;
            desc = desc.Trim();
            Molecule mol;
            Adduct adduct;
            string neutralFormula;
            if (IonInfo.IsFormulaWithAdduct(desc, out mol, out adduct, out neutralFormula))
            {
                totalMass += mol.Sum(p => p.Value*GetMass(p.Key));
                desc = string.Empty; // Signal that we parsed the whole thing
                return totalMass;
            }
            while (desc.Length > 0)
            {
                string sym = NextSymbol(desc);
                double massAtom = GetMass(sym);

                // Stop if unrecognized atom found.
                if (massAtom == 0)
                {
                    // CONSIDER: Throw with a useful message?
                    break;
                }

                desc = desc.Substring(sym.Length);
                int endCount = 0;
                while (endCount < desc.Length && Char.IsDigit(desc[endCount]))
                    endCount++;

                long count = 1;
                if (endCount > 0)
                {
                    if (!long.TryParse(desc.Substring(0, endCount), out count))
                        count = long.MaxValue; // We know at this point that it should parse, so it's probably just too big
                }
                totalMass += massAtom * count;

                desc = desc.Substring(endCount).TrimStart();
            }

            return totalMass;            
        }

        /// <summary>
        /// Parse a formula which may contain both positive and negative parts (e.g. "C'4-C4").
        /// </summary>
        public double ParseMassExpression(ref string desc)
        {
            double totalMass = ParseMass(ref desc);
            if (desc.StartsWith("-")) // Not L10N
            {
                // As is deprotonation description ie C12H8O2-H (=C12H7O2) or even C12H8O2-H2O (=C12H6O)
                desc = desc.Substring(1);
                totalMass -= ParseMass(ref desc);
            }
            return totalMass;
        }

        public double ParseMass(IDictionary<string, int> desc)
        {
            double totalMass = 0;
            foreach (var elementCount in desc)
            {
                double massAtom = GetMass(elementCount.Key);

                // Stop if unrecognized atom found.
                if (massAtom == 0)
                {
                    // CONSIDER: Throw with a useful message?
                    break;
                }
                totalMass += massAtom * elementCount.Value;
            }
            return totalMass;
        }

        /// <summary>
        /// Add or subtract the atom counts from a molecular formula to a <see cref="IDictionary{TKey,TValue}"/>
        /// of atomic symbols and counts.
        /// </summary>
        /// <param name="desc">Molecular formula</param>
        /// <param name="dictAtomCounts">Dictionary of atomic symbols and counts (may already contain counts from other formulas)</param>
        /// <param name="negative">True if counts should be subtracted</param>
        public void ParseCounts(ref string desc, IDictionary<string, int> dictAtomCounts, bool negative)
        {
            if (string.IsNullOrEmpty(desc))
            {
                return;
            }
            desc = desc.Trim();
            while (desc.Length > 0)
            {
                if (desc.StartsWith("-")) // Not L10N
                {
                    // As is deprotonation description ie C12H8O2-H (=C12H7O2) or even C12H8O2-H2O (=C12H6O)
                    desc = desc.Substring(1);
                    ParseCounts(ref desc, dictAtomCounts, !negative);
                    break;
                }
                string sym = NextSymbol(desc);
                double massAtom = GetMass(sym);

                // Stop if unrecognized atom found.
                if (massAtom == 0)
                {
                    // CONSIDER: Throw with a useful message?
                    break;
                }

                desc = desc.Substring(sym.Length);
                int endCount = 0;
                while (endCount < desc.Length && Char.IsDigit(desc[endCount]))
                    endCount++;

                int count = 1;
                if (endCount > 0)
                    count = int.Parse(desc.Substring(0, endCount), CultureInfo.InvariantCulture);

                if (negative)
                    count = -count;

                if (dictAtomCounts.ContainsKey(sym))
                    dictAtomCounts[sym] += count;
                else
                    dictAtomCounts.Add(sym, count);

                if (dictAtomCounts[sym] == 0)
                    dictAtomCounts.Remove(sym);

                desc = desc.Substring(endCount).TrimStart();
            }
        }

        /// <summary>
        /// Get the mass of a single atom.
        /// </summary>
        /// <param name="sym">Character specifying the atom</param>
        /// <returns>The mass of the single atom</returns>
        public double GetMass(string sym)
        {
            double mass;
            if (_atomicMasses.TryGetValue(sym, out mass))
                return mass;
            return 0;
        }

        /// <summary>
        /// Adds atomic masses for a symbol character to a look-up table.
        /// </summary>
        /// <param name="sym">Atomic symbol character</param>
        /// <param name="mono">Monoisotopic mass</param>
        /// <param name="ave">Average mass</param>
        private void AddMass(string sym, double mono, double ave)
        {
            _atomicMasses[sym] = MassType.IsMonoisotopic() ? mono : ave;
        }

        /// <summary>
        /// Return true if symbol is found in mass table
        /// </summary>
        /// <param name="sym"></param>
        /// <returns></returns>
        public bool IsKnownSymbol(string sym)
        {
            return _atomicMasses.ContainsKey(sym);
        }

        /// <summary>
        /// Synchronizes the masses of an <see cref="IsotopeAbundances"/> object with
        /// the masses of this <see cref="BioMassCalc"/>, ensuring compatible mass calculations
        /// using the different classes.  This is only allowed with a monoisotopic mass calculator.
        /// </summary>
        /// <param name="abundances">An existing <see cref="IsotopeAbundances"/> object to be synchronized</param>
        /// <returns>An <see cref="IsotopeAbundances"/> object synchronized with this <see cref="BioMassCalc"/></returns>
        public IsotopeAbundances SynchMasses(IsotopeAbundances abundances)
        {
            if (!MassType.IsMonoisotopic())
                throw new InvalidOperationException(
                    Resources.
                        BioMassCalc_SynchMasses_Fixing_isotope_abundance_masses_requires_a_monoisotopic_mass_calculator);

            var dictFixes = new Dictionary<string, MassDistribution>();
            foreach (var atomAbundance in abundances)
            {
                double monoMassCalc;
                if (!_atomicMasses.TryGetValue(atomAbundance.Key, out monoMassCalc))
                    continue;
                double secondMassCalc, thirdMassCalc;
                _atomicMasses.TryGetValue(atomAbundance.Key + "'", out secondMassCalc); // Not L10N
                _atomicMasses.TryGetValue(atomAbundance.Key + "\"", out thirdMassCalc); // Not L10N
                var massDist = atomAbundance.Value;
                var massDistFixed = SynchDist(massDist, monoMassCalc, secondMassCalc, thirdMassCalc);
                if (!ReferenceEquals(massDist, massDistFixed))
                    dictFixes.Add(atomAbundance.Key, massDistFixed);
            }
            return abundances.SetAbundances(dictFixes);
        }

        /// <summary>
        /// Synchronizes a single <see cref="MassDistribution"/> object with corresponding
        /// masses from a <see cref="BioMassCalc"/>.
        /// </summary>
        private static MassDistribution SynchDist(MassDistribution massDist,
            double monoMassCalc, double secondMassCalc, double thirdMassCalc)
        {
            var massDistOrdered = massDist.MassesSortedByAbundance();
            if (EqualDistMasses(massDistOrdered, monoMassCalc, secondMassCalc, thirdMassCalc))
                return massDist;
            var dictFixDist = massDist.ToDictionary();
            ReplaceMass(dictFixDist, massDistOrdered, 0, monoMassCalc);
            ReplaceMass(dictFixDist, massDistOrdered, 1, secondMassCalc);
            ReplaceMass(dictFixDist, massDistOrdered, 2, thirdMassCalc);
            return MassDistribution.NewInstance(dictFixDist, 0, 0);
        }

        /// <summary>
        /// Returns true if an ordered list of mass-distribution pairs are all
        /// equal to corresponding masses from a <see cref="BioMassCalc"/>.
        /// </summary>
        private static bool EqualDistMasses(IList<KeyValuePair<double, double>> massDistOrdered,
            double monoMassCalc, double secondMassCalc, double thirdMassCalc)
        {
            if (monoMassCalc != massDistOrdered[0].Key)
                return false;
            if (secondMassCalc != 0 && secondMassCalc != massDistOrdered[1].Key)
                return false;
            if (thirdMassCalc != 0 && thirdMassCalc != massDistOrdered[2].Key)
                return false;
            return true;
        }

        /// <summary>
        /// Replaces a mass from a <see cref="MassDistribution"/> with a corresponding
        /// mass from a <see cref="BioMassCalc"/>.
        /// </summary>
        /// <param name="dictFixDist">A mass-distribution dictionary in which to replace the mass</param>
        /// <param name="massDistOrdered">A distribution ordered list of masses-distribution pairs</param>
        /// <param name="massIndex">The index of the mass in the ordered list which should be replaced</param>
        /// <param name="massCalc">The mass to use as the replacement</param>
        private static void ReplaceMass(IDictionary<double, double> dictFixDist,
            IList<KeyValuePair<double, double>> massDistOrdered, int massIndex, double massCalc)
        {
            if (massCalc == 0 || massCalc == massDistOrdered[massIndex].Key)
                return;
            dictFixDist[massCalc] = massDistOrdered[massIndex].Value;
            dictFixDist.Remove(massDistOrdered[massIndex].Key);
        }
    }
}