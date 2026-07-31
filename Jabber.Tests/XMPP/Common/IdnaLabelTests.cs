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
    /// IDNA2008 auf Label-Ebene: RFC 5891, Abschnitt 4.2, und die Rückprobe
    /// eines A-Labels.
    /// </summary>
    /// <remarks>
    /// Ein Domainpart ist keine Zeichenkette, sondern eine Folge von Labels,
    /// und die Regeln greifen je Label. Zwei davon sind keine Formalien,
    /// sondern Schutz gegen zwei Adressen für dieselbe Sache:
    ///
    /// <list type="bullet">
    ///   <item>Ein A-Label muss sich zurückrechnen lassen <b>und</b> dabei
    ///         genau sich selbst ergeben. Sonst gäbe es zu einem Namen mehrere
    ///         gültige Schreibweisen.</item>
    ///   <item>Ein A-Label, das reines ASCII verpackt, ist keines: Dasselbe
    ///         Label stünde einmal als es selbst und einmal in Verpackung da.</item>
    /// </list>
    /// </remarks>
    [TestFixture]
    public class IdnaLabelTests
    {

        #region Hilfsfunktionen

        private static Boolean Gueltig(String domain)
            => Idna.IsValidDomain(domain, out _);

        private static String? Grund(String domain)
        {
            Idna.IsValidDomain(domain, out var grund);
            return grund;
        }

        #endregion


        #region OrdinaryNames_AreValid()

        /// <summary>
        /// Die Gegenprobe zuerst: Was ein Domainname ist, bleibt einer.
        /// </summary>
        [Test]
        public void OrdinaryNames_AreValid()
        {

            Assert.Multiple(() =>
            {
                Assert.That(Gueltig("example.com"),            Is.True, Grund("example.com"));
                Assert.That(Gueltig("localhost"),              Is.True, Grund("localhost"));
                Assert.That(Gueltig("a.example.com"),          Is.True, Grund("a.example.com"));
                Assert.That(Gueltig("xn--bcher-kva.example"),  Is.True, Grund("xn--bcher-kva.example"));
                Assert.That(Gueltig("bücher.example"),         Is.True, Grund("bücher.example"));
                Assert.That(Gueltig("a-b.example"),            Is.True, Grund("a-b.example"));
            });

        }

        #endregion

        #region TheAceLabel_IsCheckedByDecodingIt()

        /// <summary>
        /// Ein A-Label wird nicht geglaubt, sondern nachgerechnet.
        /// </summary>
        [Test]
        public void TheAceLabel_IsCheckedByDecodingIt()
        {

            Assert.Multiple(() =>
            {

                Assert.That(Punycode.Decode("bcher-kva"), Is.EqualTo("bücher"),
                            "Das bekannteste Beispiel überhaupt.");

                Assert.That(Gueltig("xn--nichtpunycode$.example"), Is.False,
                            "Was wie ein A-Label aussieht und keines ist, wird abgewiesen.");

                Assert.That(Gueltig("xn--abc-.example"), Is.False,
                            "Ein A-Label, das reines ASCII verpackt, ist keines.");

                Assert.That(Gueltig("xn--tda.example"), Is.True,
                            Grund("xn--tda.example"));

                Assert.That(Gueltig("xn--TDA.example"), Is.False,
                            "Dieselbe Bedeutung, andere Schreibweise: Punycode-Ziffern sind " +
                            "schreibweisenlos, das kanonische A-Label ist es nicht.");

            });

        }

        #endregion

        #region TheHyphenRules()

        /// <summary>
        /// RFC 5891, Abschnitt 4.2.3.1: kein Bindestrich am Rand, kein
        /// Doppelstrich an dritter und vierter Stelle.
        /// </summary>
        /// <remarks>
        /// Die zweite Regel hält die Stelle frei, an der das Präfix eines
        /// A-Labels steht. Ein U-Label, das dort <c>--</c> trägt, sähe aus wie
        /// eine Verpackung und wäre keine.
        /// </remarks>
        [Test]
        public void TheHyphenRules()
        {

            Assert.Multiple(() =>
            {
                Assert.That(Gueltig("-abc.example"),  Is.False, "Bindestrich am Anfang");
                Assert.That(Gueltig("abc-.example"),  Is.False, "Bindestrich am Ende");
                Assert.That(Gueltig("ab--cd.example"), Is.False, "'--' an dritter und vierter Stelle");
                Assert.That(Gueltig("a-b-c.example"),  Is.True,  "Einzelne Bindestriche sind in Ordnung.");
            });

        }

        #endregion

        #region ACombiningMarkAtTheStart_IsRefused()

        /// <summary>
        /// RFC 5891, Abschnitt 4.2.3.2: Ein Label beginnt nicht mit einem
        /// kombinierenden Zeichen - es hätte nichts, womit es sich verbinden
        /// könnte.
        /// </summary>
        [Test]
        public void ACombiningMarkAtTheStart_IsRefused()
        {

            Assert.That(Gueltig("́abc.example"), Is.False);

        }

        #endregion

        #region EmptyAndOverlongLabels_AreRefused()

        /// <summary>
        /// Ein leeres Label gibt es nicht - auch nicht als Punkt am Ende.
        /// </summary>
        [Test]
        public void EmptyAndOverlongLabels_AreRefused()
        {

            Assert.Multiple(() =>
            {
                Assert.That(Gueltig("a..example"),      Is.False, "zwei Punkte");
                Assert.That(Gueltig("example.com."),    Is.False, "Punkt am Ende");
                Assert.That(Gueltig(new String('a', 64) + ".example"), Is.False, "64 Zeichen");
                Assert.That(Gueltig(new String('a', 63) + ".example"), Is.True,  "63 sind erlaubt");
            });

        }

        #endregion

        #region WhatIsNoLabelCharacter()

        /// <summary>
        /// Die Codepoint-Ebene wirkt bis hierher durch: Unterstrich und
        /// Grossbuchstabe gehören in kein Label.
        /// </summary>
        /// <remarks>
        /// Am JID selbst fällt der Grossbuchstabe nicht auf - der Domainpart
        /// wird vorher kleingeschrieben. Hier steht er trotzdem, weil die
        /// Prüfung für sich stimmen muss: Sie wird auch von anderswoher
        /// aufgerufen werden.
        /// </remarks>
        [Test]
        public void WhatIsNoLabelCharacter()
        {

            Assert.Multiple(() =>
            {
                Assert.That(Gueltig("exam_ple.example"),  Is.False, "Unterstrich");
                Assert.That(Gueltig("EXAMPLE.com"),       Is.False, "Grossbuchstabe");
                Assert.That(Gueltig("exa mple.com"),      Is.False, "Leerzeichen");
                Assert.That(Gueltig("exa♚mple.com"),      Is.False, "Symbol");
            });

        }

        #endregion

        #region AddressLiterals_AreNotDomainNames()

        /// <summary>
        /// RFC 7622, Abschnitt 3.2 lässt neben dem Domainnamen ein
        /// IPv4-Literal und ein eingeklammertes IPv6-Literal zu.
        /// </summary>
        /// <remarks>
        /// Ohne diese Ausnahme fiele <c>127.0.0.1</c> über die Label-Regeln:
        /// Ein Label aus lauter Ziffern ist zwar zulässig, aber ein Literal ist
        /// gar kein Domainname und hat mit IDNA nichts zu tun. Bei
        /// <c>[::1]</c> wäre die Abweisung sogar sicher - Doppelpunkte sind
        /// keine Label-Zeichen.
        /// </remarks>
        [Test]
        public void AddressLiterals_AreNotDomainNames()
        {

            Assert.Multiple(() =>
            {
                Assert.That(Gueltig("127.0.0.1"),        Is.True,  Grund("127.0.0.1"));
                Assert.That(Gueltig("[::1]"),            Is.True,  Grund("[::1]"));
                Assert.That(Gueltig("[2001:db8::1]"),    Is.True,  Grund("[2001:db8::1]"));
                Assert.That(Gueltig("::1"),              Is.False, "Ohne Klammern ist es keines.");
            });

        }

        #endregion

        #region TheDomainpartOfAJid_GoesThroughTheseRules()

        /// <summary>
        /// Und das alles gilt für den Domainpart eines JIDs, nicht nur für sich.
        /// </summary>
        /// <remarks>
        /// Ohne diesen Test wäre die Verdrahtung ungeprüft: Eine Mutation, die
        /// das Ergebnis der Prüfung wegwirft und weitermacht, kam durch die
        /// ganze Sammlung. Die Prüfung für sich zu prüfen genügt nicht - es
        /// muss jemand hinsehen, ob sie auch <i>gefragt</i> wird.
        /// </remarks>
        [Test]
        public void TheDomainpartOfAJid_GoesThroughTheseRules()
        {

            Assert.Multiple(() =>
            {

                Assert.That(JidUtilities.TryParse("alice@exa_mple.com",   out _), Is.False, "Unterstrich");
                Assert.That(JidUtilities.TryParse("alice@-example.com",   out _), Is.False, "Bindestrich am Anfang");
                Assert.That(JidUtilities.TryParse("alice@a..example.com", out _), Is.False, "leeres Label");
                Assert.That(JidUtilities.TryParse("alice@xn--abc-.com",   out _), Is.False, "A-Label über ASCII");

                Assert.That(JidUtilities.TryParse("alice@bücher.example", out var buecher), Is.True);
                Assert.That(buecher.Domainpart, Is.EqualTo("bücher.example"),
                            "Ein U-Label bleibt ein U-Label - umgeschrieben wird hier nichts.");

                Assert.That(JidUtilities.TryParse("alice@[::1]",          out _), Is.True,  "IPv6-Literal");
                Assert.That(JidUtilities.TryParse("alice@127.0.0.1",      out _), Is.True,  "IPv4-Literal");

            });

        }

        #endregion

        #region TheReasonIsNamed()

        /// <summary>
        /// Die Begründung nennt das Label und die Regel - nicht nur „ungültig".
        /// </summary>
        /// <remarks>
        /// Eine abgewiesene Adresse ist für den Absender eine verlorene
        /// Nachricht. Wer sie zurückweist, sollte sagen können, woran es lag.
        /// </remarks>
        [Test]
        public void TheReasonIsNamed()
        {

            Assert.Multiple(() =>
            {
                Assert.That(Grund("exam_ple.example"), Does.Contain("U+005F"));
                Assert.That(Grund("-abc.example"),     Does.Contain("-abc"));
                Assert.That(Grund("a..example"),       Does.Contain("leer"));
            });

        }

        #endregion

    }

}
