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
    /// JIDs nach RFC 7622, gegen die beiden Beispieltabellen aus Abschnitt 3.5.
    /// </summary>
    /// <remarks>
    /// Fuenfzehn Zeichenketten, die JIDs sind, und acht, die keine sind. Der
    /// Abschnitt ist als Pruefstein gebaut: Fast jede Zeile trifft genau eine
    /// Regel, und mehrere davon sind solche, auf die man von selbst nicht
    /// kaeme - die Zerlegungsreihenfolge etwa, oder dass ein Zeichen mit
    /// Kompatibilitaetszerlegung im Localpart nichts zu suchen hat.
    ///
    /// Wie in der SASLprep-Sammlung steht jedes besondere Zeichen als benannte
    /// Konstante da statt als Literal.
    /// </remarks>
    [TestFixture]
    public class JidFormatTests
    {

        #region Data

        private const String SharpS       = "ß";   // LATIN SMALL LETTER SHARP S
        private const String Pi           = "π";   // GREEK SMALL LETTER PI
        private const String CapitalSigma = "Σ";   // GREEK CAPITAL LETTER SIGMA
        private const String SmallSigma   = "σ";   // GREEK SMALL LETTER SIGMA
        private const String FinalSigma   = "ς";   // GREEK SMALL LETTER FINAL SIGMA
        private const String ChessKing    = "♚";   // BLACK CHESS KING
        private const String RomanFour    = "Ⅳ";   // ROMAN NUMERAL FOUR
        private const String LigatureFi   = "ﬁ";   // LATIN SMALL LIGATURE FI

        #endregion

        #region Rfc7622_Table1_AreAllJids()

        /// <summary>
        /// Tabelle 1: fuenfzehn gueltige JIDs.
        /// </summary>
        [Test]
        public void Rfc7622_Table1_AreAllJids()
        {

            var gueltig = new (Int32 Nr, String Jid, String Warum)[]
            {
                ( 1, "juliet@example.com",              "Bare-JID"),
                ( 2, "juliet@example.com/foo",          "Full-JID"),
                ( 3, "juliet@example.com/foo bar",      "Leerzeichen im Resourcepart"),
                ( 4, "juliet@example.com/foo@bar",      "At-Zeichen im Resourcepart"),
                ( 5, "foo\\20bar@example.com",          "XEP-0106-Umschreibung im Localpart"),
                ( 6, "fussball@example.com",            "Bare-JID"),
                ( 7, "fu" + SharpS + "ball@example.com","Esszett im Localpart"),
                ( 8, Pi + "@example.com",               "Localpart aus einem griechischen Pi"),
                ( 9, CapitalSigma + "@example.com/foo", "Localpart aus einem grossen Sigma"),
                (10, SmallSigma + "@example.com/foo",   "Localpart aus einem kleinen Sigma"),
                (11, FinalSigma + "@example.com/foo",   "Localpart aus einem End-Sigma"),
                (12, "king@example.com/" + ChessKing,   "Symbol im Resourcepart"),
                (13, "example.com",                     "nur ein Domainpart"),
                (14, "example.com/foobar",              "Domainpart und Resourcepart"),
                (15, "a.example.com/b@example.net",     "Resourcepart mit At-Zeichen")
            };

            Assert.Multiple(() =>
            {
                foreach (var (nr, jid, warum) in gueltig)
                    Assert.That(JidUtilities.TryParse(jid, out _), Is.True,
                                $"Beispiel {nr} ({warum}) muss ein JID sein.");
            });

        }

        #endregion

        #region Rfc7622_Table2_AreNoJids()

        /// <summary>
        /// Tabelle 2: Zeichenketten, die keine JIDs sind.
        /// </summary>
        /// <remarks>
        /// Beispiel 18 fehlt hier mit Absicht und wird gleich darunter fuer
        /// sich behandelt.
        /// </remarks>
        [Test]
        public void Rfc7622_Table2_AreNoJids()
        {

            var ungueltig = new (Int32 Nr, String Jid, String Warum)[]
            {
                (16, "\"juliet\"@example.com",           "Anfuehrungszeichen im Localpart"),
                (17, "foo bar@example.com",              "Leerzeichen im Localpart"),
                (19, "@example.com/",                    "Local- und Resourcepart leer"),
                (20, "henry" + RomanFour + "@example.com", "roemische Vier im Localpart"),
                (21, ChessKing + "@example.com",         "Symbol im Localpart"),
                (22, "juliet@",                          "Localpart ohne Domainpart"),
                (23, "/foobar",                          "Resourcepart ohne Domainpart")
            };

            Assert.Multiple(() =>
            {
                foreach (var (nr, jid, warum) in ungueltig)
                    Assert.That(JidUtilities.TryParse(jid, out _), Is.False,
                                $"Beispiel {nr} ({warum}) darf kein JID sein.");
            });

        }

        #endregion

        #region Rfc7622_Example18_LeadingSpaceInResource_IsAccepted()

        /// <summary>
        /// Beispiel 18 - ein fuehrendes Leerzeichen im Resourcepart - wird
        /// hier <b>angenommen</b>, entgegen der Tabelle.
        /// </summary>
        /// <remarks>
        /// Das ist eine bewusste Abweichung und keine Luecke. RFC 7622 fuehrt
        /// die Zeichenkette als Nicht-JID auf, aber die Regel dazu fehlt: Der
        /// Resourcepart ist eine Instanz des OpaqueString-Profils, und das
        /// laesst Leerzeichen ausdruecklich zu (RFC 8265, Abschnitt 4.2.2,
        /// Regel 2 bildet lediglich Leerzeichen ausserhalb von ASCII auf
        /// U+0020 ab). Ein Verbot fuehrender Leerzeichen steht weder dort noch
        /// sonstwo im Regelteil.
        ///
        /// Fuer einen Router ist Annehmen ausserdem die vorsichtigere Wahl:
        /// Eine Adresse zurueckzuweisen, die andere Server fuer gueltig
        /// halten, verliert Nachrichten - und zwar unsere.
        ///
        /// Der Test steht hier, damit die Abweichung eine Stelle hat, an der
        /// sie auffaellt, wenn jemand sie spaeter anders entscheidet.
        /// </remarks>
        [Test]
        public void Rfc7622_Example18_LeadingSpaceInResource_IsAccepted()
        {

            Assert.That(JidUtilities.TryParse("juliet@example.com/ foo", out var teile), Is.True);
            Assert.That(teile.Resourcepart, Is.EqualTo(" foo"));

        }

        #endregion

        #region CompatibilityCharacters_AreRefusedInLocalpart()

        /// <summary>
        /// Zeichen mit Kompatibilitätszerlegung gehören nicht in einen
        /// Localpart (HasCompat, RFC 8264, Abschnitt 9.6).
        /// </summary>
        /// <remarks>
        /// Beispiel 20 aus RFC 7622 - die römische Vier - fällt schon über die
        /// Kategorie: Sie ist eine Zahl-als-Buchstabe (Nl) und damit ohnehin
        /// kein Buchstabe im Sinne der IdentifierClass. Die HasCompat-Regel
        /// bleibt dabei ungeprüft.
        ///
        /// Die Ligatur ﬁ trifft sie dagegen genau: Sie ist ein
        /// Kleinbuchstabe, kommt also durch die Kategorieprüfung, und zerfällt
        /// kompatibel in „fi". Ohne die Regel wären <c>ﬁle@example.com</c> und
        /// <c>file@example.com</c> zwei Konten, die für das Auge dasselbe
        /// sind - genau die Verwechslung, gegen die PRECIS gebaut ist.
        /// </remarks>
        [Test]
        public void CompatibilityCharacters_AreRefusedInLocalpart()
        {

            Assert.Multiple(() =>
            {

                Assert.That(JidUtilities.TryParse(LigatureFi + "le@example.com", out _),
                            Is.False,
                            "Die Ligatur hat eine Kompatibilitätszerlegung.");

                Assert.That(JidUtilities.TryParse("file@example.com", out _),
                            Is.True,
                            "Die ausgeschriebene Fassung ist selbstverständlich zulässig.");

                // Im Resourcepart ist sie dagegen erlaubt: Die FreeformClass
                // schliesst HasCompat nicht aus.
                Assert.That(JidUtilities.TryParse("juliet@example.com/" + LigatureFi, out _),
                            Is.True);

            });

        }

        #endregion

        #region EmptyParts_AreRefusedEachOnTheirOwn()

        /// <summary>
        /// Local- und Resourcepart dürfen, wenn ihr Trennzeichen dasteht,
        /// nicht leer sein - jeder für sich.
        /// </summary>
        /// <remarks>
        /// Beispiel 19 aus der Tabelle (<c>@example.com/</c>) hat beide Fehler
        /// zugleich und belegt deshalb keinen von beiden: Es genügt die erste
        /// Prüfung, die zuschlägt, und die zweite bleibt ungelaufen.
        /// </remarks>
        [Test]
        public void EmptyParts_AreRefusedEachOnTheirOwn()
        {

            Assert.Multiple(() =>
            {

                Assert.That(JidUtilities.TryParse("juliet@example.com/", out _), Is.False,
                            "Ein Schrägstrich ohne Resource dahinter.");

                Assert.That(JidUtilities.TryParse("@example.com", out _), Is.False,
                            "Ein At-Zeichen ohne Localpart davor.");

            });

        }

        #endregion

        #region TheSplitOrderMatters()

        /// <summary>
        /// Erst am <c>/</c> trennen, dann am <c>@</c> - Beispiel 15.
        /// </summary>
        /// <remarks>
        /// Andersherum ergaebe <c>a.example.com/b@example.net</c> einen
        /// Localpart <c>a.example.com/b</c>, und der enthielte ein <c>/</c>,
        /// das dort ausgeschlossen ist. Aus einem gueltigen JID wuerde ein
        /// ungueltiger.
        /// </remarks>
        [Test]
        public void TheSplitOrderMatters()
        {

            var beispiel15 = JidUtilities.Parse("a.example.com/b@example.net");

            // RFC 7622, Abschnitt 3.4: Ein zweiter Schrägstrich gehört zur
            // Resource - JIDs sind nicht hierarchisch. Getrennt wird am
            // *ersten*, nicht am letzten.
            var zweiSchraege = JidUtilities.Parse("juliet@example.com/foo/bar");

            Assert.Multiple(() =>
            {

                Assert.That(beispiel15.Localpart,    Is.Null);
                Assert.That(beispiel15.Domainpart,   Is.EqualTo("a.example.com"));
                Assert.That(beispiel15.Resourcepart, Is.EqualTo("b@example.net"));

                Assert.That(zweiSchraege.Localpart,    Is.EqualTo("juliet"));
                Assert.That(zweiSchraege.Domainpart,   Is.EqualTo("example.com"));
                Assert.That(zweiSchraege.Resourcepart, Is.EqualTo("foo/bar"),
                            "Der zweite Schrägstrich gehört in die Resource.");

            });

        }

        #endregion

        #region TheResourcepartKeepsItsCase()

        /// <summary>
        /// Der Kern: Local- und Domainpart sind von der Schreibweise
        /// unabhaengig, der Resourcepart nicht (RFC 7622, Abschnitt 3.4).
        /// </summary>
        [Test]
        public void TheResourcepartKeepsItsCase()
        {

            var teile = JidUtilities.Parse("Juliet@Example.COM/Balcony");

            Assert.Multiple(() =>
            {

                Assert.That(teile.Localpart,    Is.EqualTo("juliet"));
                Assert.That(teile.Domainpart,   Is.EqualTo("example.com"));
                Assert.That(teile.Resourcepart, Is.EqualTo("Balcony"),
                            "Der Resourcepart darf nicht kleingeschrieben werden.");

                Assert.That(JidUtilities.AreEqual("juliet@example.com/Balcony",
                                                  "JULIET@EXAMPLE.COM/Balcony"),
                            Is.True,
                            "Local- und Domainpart ohne Ruecksicht auf die Schreibweise.");

                Assert.That(JidUtilities.AreEqual("juliet@example.com/Balcony",
                                                  "juliet@example.com/balcony"),
                            Is.False,
                            "Zwei Resourcen, die sich nur in der Schreibweise " +
                            "unterscheiden, sind zwei Geraete.");

            });

        }

        #endregion

        #region Rfc7622_CaseMappingNotes()

        /// <summary>
        /// Die Anmerkungen zu den Beispielen 6/7 und 9/10/11.
        /// </summary>
        /// <remarks>
        /// Zwei Feinheiten, die der Text eigens hervorhebt. Erstens: Esszett
        /// und „ss" bleiben verschieden - die Regel ist Kleinschreibung
        /// (toLowerCase), nicht Case Folding, das <c>ss</c> daraus machte.
        /// Zweitens: Grosses Sigma wird zu kleinem, das End-Sigma bleibt
        /// dagegen es selbst.
        /// </remarks>
        [Test]
        public void Rfc7622_CaseMappingNotes()
        {

            Assert.Multiple(() =>
            {

                Assert.That(JidUtilities.AreEqual("fu" + SharpS + "ball@example.com",
                                                  "fussball@example.com"),
                            Is.False,
                            "Esszett und ss sind zwei verschiedene Localparts.");

                Assert.That(JidUtilities.AreEqual(CapitalSigma + "@example.com",
                                                  SmallSigma   + "@example.com"),
                            Is.True,
                            "Grosses und kleines Sigma fallen zusammen.");

                Assert.That(JidUtilities.AreEqual(FinalSigma + "@example.com",
                                                  SmallSigma + "@example.com"),
                            Is.False,
                            "Das End-Sigma bleibt ein eigenes Zeichen.");

            });

        }

        #endregion

        #region PartsLongerThan1023Octets_AreRefused()

        /// <summary>
        /// RFC 7622: Jeder Teil ist auf 1023 Oktette begrenzt - gemessen nach
        /// der Vorbereitung und an der UTF-8-Kodierung.
        /// </summary>
        /// <remarks>
        /// Der Unterschied zwischen Zeichen und Oktetten ist hier keine
        /// Feinheit: Ein Localpart aus 600 griechischen Buchstaben hat 600
        /// Zeichen und 1200 Oktette.
        /// </remarks>
        [Test]
        public void PartsLongerThan1023Octets_AreRefused()
        {

            Assert.Multiple(() =>
            {

                Assert.That(JidUtilities.TryParse(new String('a', 1023) + "@example.com", out _),
                            Is.True,
                            "1023 Oktette sind erlaubt.");

                Assert.That(JidUtilities.TryParse(new String('a', 1024) + "@example.com", out _),
                            Is.False);

                // 600 Zeichen, aber 1200 Oktette.
                Assert.That(JidUtilities.TryParse(String.Concat(Enumerable.Repeat(Pi, 600)) +
                                                  "@example.com", out _),
                            Is.False,
                            "Gemessen wird in Oktetten, nicht in Zeichen.");

            });

        }

        #endregion

        #region Bare_NeverThrows()

        /// <summary>
        /// <c>Bare</c> laeuft ueber alles, was von der Leitung kommt, und darf
        /// deshalb an keiner Eingabe scheitern.
        /// </summary>
        /// <remarks>
        /// Eine Ausnahme mitten in der Stanza-Behandlung waere der schlechteste
        /// aller Ausgaenge: Ein Absender, der Unsinn schickt, brächte damit die
        /// Verbindung zu Fall. Unbrauchbares soll auf nichts passen, nicht
        /// alles anhalten.
        /// </remarks>
        [Test]
        public void Bare_NeverThrows()
        {

            var unsinn = new[] { "", "@", "/", "@/", "juliet@", "/foobar",
                                 "\"juliet\"@example.com", "a@b@c" };

            Assert.Multiple(() =>
            {
                foreach (var eingabe in unsinn)
                    Assert.That(() => JidUtilities.Bare(eingabe), Throws.Nothing,
                                $"Ist gestolpert ueber: '{eingabe}'");
            });

        }

        #endregion

    }

}
