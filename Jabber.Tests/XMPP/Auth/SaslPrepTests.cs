/*
 * Copyright (c) 2010-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of Hermod <https://www.github.com/Vanaheimr/Hermod>
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

#region Usings

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// SASLprep (RFC 4013), zuerst gegen die Beispieltabelle aus Abschnitt 3.
    /// </summary>
    /// <remarks>
    /// Sieben Zeilen, die alle vier Schritte des Profils abdecken: Abbildung,
    /// Normalisierung, Verbote und die Bidi-Pruefung. Sie sind der Grund, warum
    /// sich diese Umsetzung nicht selbst benotet - die Tabellen dahinter
    /// stammen aus RFC 3454 und sind von <c>tools/stringprep/generate.py</c>
    /// daraus erzeugt.
    ///
    /// Jedes geprüfte Zeichen steht als Escape-Sequenz da und nicht als es
    /// selbst. Die halbe Sammlung besteht aus Zeichen, die unsichtbar sind oder
    /// die Schreibrichtung umkehren; als Literal im Quelltext wäre nicht zu
    /// sehen, was eigentlich geprüft wird - und beim Bearbeiten ginge es
    /// unbemerkt verloren. (Diese Datei hat genau das einmal vorgeführt.)
    /// </remarks>
    [TestFixture]
    public class SaslPrepTests
    {

        #region Data

        // Die Zeichen, um die es geht - benannt statt eingesetzt.
        private const String SoftHyphen        = "\u00AD";
        private const String FeminineOrdinal   = "ª";
        private const String RomanNine         = "Ⅸ";
        private const String Bell              = "\u0007";
        private const String NoBreakSpace      = "\u00A0";
        private const String OghamSpace        = "\u1680";
        private const String IdeographicSpace  = "\u3000";
        private const String ArabicAlef        = "ا";
        private const String ArabicBeh         = "ب";
        private const String HebrewAlef        = "א";
        private const String Unassigned32      = "ȡ";

        #endregion

        #region Rfc4013_ExampleTable()

        /// <summary>
        /// Die Beispieltabelle aus RFC 4013, Abschnitt 3, Zeile für Zeile.
        /// </summary>
        [Test]
        public void Rfc4013_ExampleTable()
        {

            Assert.Multiple(() =>
            {

                // 1: SOFT HYPHEN faellt weg
                Assert.That(SaslPrep.Prepare("I" + SoftHyphen + "X"), Is.EqualTo("IX"));

                // 2: unveraendert
                Assert.That(SaslPrep.Prepare("user"), Is.EqualTo("user"));

                // 3: Gross- und Kleinschreibung bleibt - passt also nicht auf 2
                Assert.That(SaslPrep.Prepare("USER"), Is.EqualTo("USER"));

                // 4: NFKC bildet das feminine Ordinalzeichen auf ein a ab
                Assert.That(SaslPrep.Prepare(FeminineOrdinal), Is.EqualTo("a"));

                // 5: NFKC zerlegt die roemische Neun - passt danach auf 1
                Assert.That(SaslPrep.Prepare(RomanNine), Is.EqualTo("IX"));

                // 6: verbotenes Zeichen
                Assert.That(() => SaslPrep.Prepare(Bell),
                            Throws.TypeOf<AuthenticationException>());

                // 7: Bidi - arabisches Alef, dann die Ziffer Eins
                Assert.That(() => SaslPrep.Prepare(ArabicAlef + "1"),
                            Throws.TypeOf<AuthenticationException>());

            });

        }

        #endregion

        #region NonAsciiSpaces_BecomeAnOrdinarySpace()

        /// <summary>
        /// RFC 4013, Abschnitt 2.1: Leerzeichen ausserhalb von ASCII werden zu
        /// U+0020.
        /// </summary>
        /// <remarks>
        /// Der Fall aus dem Alltag: Ein geschütztes Leerzeichen sieht aus wie
        /// ein gewöhnliches und entsteht auf manchen Tastaturen von selbst.
        /// Ohne diese Abbildung wären es zwei verschiedene Passwörter.
        /// </remarks>
        [Test]
        public void NonAsciiSpaces_BecomeAnOrdinarySpace()
        {

            Assert.Multiple(() =>
            {
                Assert.That(SaslPrep.Prepare("a" + NoBreakSpace     + "b"), Is.EqualTo("a b"));
                Assert.That(SaslPrep.Prepare("a" + OghamSpace       + "b"), Is.EqualTo("a b"));
                Assert.That(SaslPrep.Prepare("a" + IdeographicSpace + "b"), Is.EqualTo("a b"));
            });

        }

        #endregion

        #region UnassignedCodePoints_AreRefused()

        /// <summary>
        /// RFC 4013, Abschnitt 2.5: Was in Unicode 3.2 nicht zugewiesen war,
        /// gehört nicht in ein gespeichertes Passwort.
        /// </summary>
        /// <remarks>
        /// Der Grund ist nicht Pedanterie: Ein Codepoint ohne festgelegte
        /// Bedeutung kann später eine bekommen, und dann normalisieren ihn zwei
        /// Gegenstellen verschieden. Wer ihn heute in sein Passwort nimmt, hat
        /// morgen ein anderes.
        ///
        /// U+0221 belegt zugleich, dass die Tabelle wirklich auf Unicode 3.2
        /// festgeschrieben ist und nicht aus der laufenden .NET-Fassung stammt:
        /// Dort ist er längst ein lateinischer Kleinbuchstabe.
        /// </remarks>
        [Test]
        public void UnassignedCodePoints_AreRefused()
        {

            Assert.Multiple(() =>
            {

                Assert.That(() => SaslPrep.Prepare("a" + Unassigned32 + "b"),
                            Throws.TypeOf<AuthenticationException>());

                // Als Abfrage-Zeichenkette ist er zulässig.
                Assert.That(SaslPrep.Prepare("a" + Unassigned32 + "b", AllowUnassigned: true),
                            Is.EqualTo("a" + Unassigned32 + "b"));

            });

        }

        #endregion

        #region ProhibitedCharacters_AreRefused()

        /// <summary>
        /// Ein Querschnitt durch die Verbotstabellen C.2 bis C.9.
        /// </summary>
        [Test]
        public void ProhibitedCharacters_AreRefused()
        {

            var verboten = new (String Name, String Eingabe)[]
            {
                ("ASCII-Steuerzeichen (C.2.1)",     "a\u0000b"),
                ("Steuerzeichen (C.2.2)",           "a\u0080b"),
                ("Private Use (C.3)",               "a\uE000b"),
                ("Nicht-Zeichen (C.4)",             "a\uFDD0b"),
                ("fuer Klartext ungeeignet (C.6)",  "a\uFFFCb"),
                ("kanonisch ungeeignet (C.7)",      "a\u2FF0b"),
                ("Darstellung aendernd (C.8)",      "a\u202Ab"),
                ("Tagging (C.9)",                   "a\U000E0001b")
            };

            Assert.Multiple(() =>
            {
                foreach (var (name, eingabe) in verboten)
                    Assert.That(() => SaslPrep.Prepare(eingabe),
                                Throws.TypeOf<AuthenticationException>(),
                                $"Durchgelassen: {name}.");
            });

        }

        #endregion

        #region ALoneSurrogate_IsRefused()

        /// <summary>
        /// Ein alleinstehendes Surrogat ist ein halbes Zeichen (Tabelle C.5).
        /// </summary>
        /// <remarks>
        /// Der Weg über <c>EnumerateRunes</c> hätte es stillschweigend durch
        /// U+FFFD ersetzt - und damit zwei verschiedene Eingaben auf dasselbe
        /// Passwort geführt.
        /// </remarks>
        [Test]
        public void ALoneSurrogate_IsRefused()
        {

            Assert.Multiple(() =>
            {

                Assert.That(() => SaslPrep.Prepare("a\uD800b"),
                            Throws.TypeOf<AuthenticationException>());

                Assert.That(() => SaslPrep.Prepare("a\uDC00b"),
                            Throws.TypeOf<AuthenticationException>());

                // Das vollstaendige Paar dagegen ist ein gewoehnliches Zeichen.
                Assert.That(SaslPrep.Prepare("a\U00010330b"),
                            Is.EqualTo("a\U00010330b"));

            });

        }

        #endregion

        #region BidiRules()

        /// <summary>
        /// RFC 3454, Abschnitt 6: die beiden Regeln für Schreibrichtungen.
        /// </summary>
        /// <remarks>
        /// Eine Zeichenkette aus beiden Richtungen wird je nach Umgebung
        /// verschieden angezeigt - wer sie liest, sieht nicht zwingend, was
        /// darin steht.
        /// </remarks>
        [Test]
        public void BidiRules()
        {

            Assert.Multiple(() =>
            {

                // Durchgehend rechtslaeufig: zulaessig.
                Assert.That(SaslPrep.Prepare(ArabicAlef + ArabicBeh),
                            Is.EqualTo(ArabicAlef + ArabicBeh));

                // Durchgehend linkslaeufig: zulaessig.
                Assert.That(SaslPrep.Prepare("abc"), Is.EqualTo("abc"));

                // Regel 2: beide Richtungen zusammen.
                Assert.That(() => SaslPrep.Prepare(ArabicAlef + "a" + ArabicBeh),
                            Throws.TypeOf<AuthenticationException>(),
                            "Arabisch mit lateinischem Buchstaben dazwischen.");

                Assert.That(() => SaslPrep.Prepare(HebrewAlef + "a" + HebrewAlef),
                            Throws.TypeOf<AuthenticationException>(),
                            "Hebraeisch mit lateinischem Buchstaben dazwischen.");

                // Regel 3: beginnt rechtslaeufig, endet nicht rechtslaeufig.
                Assert.That(() => SaslPrep.Prepare(ArabicAlef + "1"),
                            Throws.TypeOf<AuthenticationException>());

                // Und umgekehrt.
                Assert.That(() => SaslPrep.Prepare("1" + ArabicAlef),
                            Throws.TypeOf<AuthenticationException>());

                // Ziffern zwischen rechtslaeufigen Zeichen sind dagegen in
                // Ordnung: Sie stehen weder in D.1 noch in D.2, und Anfang und
                // Ende stimmen.
                Assert.That(SaslPrep.Prepare(ArabicAlef + "1" + ArabicBeh),
                            Is.EqualTo(ArabicAlef + "1" + ArabicBeh));

            });

        }

        #endregion

        #region Prepare_IsIdempotent()

        /// <summary>
        /// Zweimal vorbereiten ändert nichts mehr.
        /// </summary>
        /// <remarks>
        /// Das ist die Eigenschaft, von der alles Übrige abhängt: Der Server
        /// legt den Schlüssel einer vorbereiteten Zeichenkette ab, der Client
        /// bereitet bei jeder Anmeldung neu vor. Wäre das Verfahren nicht
        /// idempotent, liefen die beiden mit jedem Durchgang weiter
        /// auseinander.
        /// </remarks>
        [Test]
        public void Prepare_IsIdempotent()
        {

            var eingaben = new[] {
                "user",
                "I" + SoftHyphen + "X",
                RomanNine,
                "a" + NoBreakSpace + "b",
                "Zwiebelfisch",
                ArabicAlef + ArabicBeh,
                "groß"
            };

            Assert.Multiple(() =>
            {
                foreach (var eingabe in eingaben)
                {
                    var einmal = SaslPrep.Prepare(eingabe);
                    Assert.That(SaslPrep.Prepare(einmal), Is.EqualTo(einmal),
                                $"Nicht idempotent: {eingabe}");
                }
            });

        }

        #endregion

        #region TheEmptyString_StaysEmpty()

        /// <summary>
        /// Die leere Zeichenkette geht unbeanstandet durch - insbesondere
        /// stolpert die Bidi-Prüfung nicht über das fehlende erste Zeichen.
        /// </summary>
        [Test]
        public void TheEmptyString_StaysEmpty()
        {
            Assert.That(SaslPrep.Prepare(""), Is.EqualTo(""));
        }

        #endregion

    }

}
