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
    /// Die Bidi-Regel aus RFC 5893, Abschnitt 2.
    /// </summary>
    /// <remarks>
    /// <b>Die Regel gilt nicht immer, sondern ansteckend.</b> Sobald ein
    /// einziges Label eines Domainnamens rechtsläufige Zeichen trägt, ist der
    /// ganze Name ein „Bidi domain name" - und dann müssen <i>alle</i> Labels
    /// die sechs Bedingungen erfüllen, auch die aus reinem ASCII. Genau das ist
    /// der Teil, den man beim Lesen überliest und beim Umsetzen vergisst:
    /// <c>9abc.example</c> ist ein gültiger Domainname, <c>9abc.אבג</c> ist
    /// keiner.
    ///
    /// Die Bidi-Klassen kommen aus <c>BidiClasses</c>, erzeugt aus
    /// <c>DerivedBidiClass.txt</c>. Ohne diese Tabelle wäre die Regel nicht
    /// umsetzbar: Ob ein Buchstabe R, AL oder L ist, hängt an seiner Schrift
    /// und ist aus keiner Eigenschaft ableitbar, die .NET ausliefert.
    /// </remarks>
    [TestFixture]
    public class IdnaBidiTests
    {

        #region Data

        private const String Hebraeisch     = "אבג";   // ALEF BET GIMEL, Klasse R
        private const String Arabisch       = "مثال";  // Klasse AL
        private const String ArabischeZiffer = "٢";    // ARABIC-INDIC DIGIT TWO, Klasse AN

        private static Boolean Gueltig(String domain)
            => Idna.IsValidDomain(domain, out _);

        private static String? Grund(String domain)
        {
            Idna.IsValidDomain(domain, out var grund);
            return grund;
        }

        #endregion


        #region WithoutAnRtlLabel_TheRuleDoesNotApply()

        /// <summary>
        /// Die Gegenprobe zuerst, und sie ist die wichtigere Hälfte: In einem
        /// Namen ohne rechtsläufiges Label gilt die Regel nicht.
        /// </summary>
        /// <remarks>
        /// <c>9abc</c> beginnt mit einer europäischen Ziffer (EN) und verstösst
        /// damit gegen Bedingung 1 - aber nur, wenn die Regel überhaupt gilt.
        /// Wer sie immer anwendet, weist reihenweise Domainnamen ab, die es seit
        /// dreissig Jahren gibt.
        /// </remarks>
        [Test]
        public void WithoutAnRtlLabel_TheRuleDoesNotApply()
        {

            Assert.Multiple(() =>
            {
                Assert.That(Gueltig("9abc.example"),  Is.True, Grund("9abc.example"));
                Assert.That(Gueltig("example.com"),   Is.True, Grund("example.com"));
                Assert.That(Gueltig("3com.example"),  Is.True, Grund("3com.example"));
            });

        }

        #endregion

        #region TheRuleIsCatching()

        /// <summary>
        /// Ein einziges rechtsläufiges Label macht den ganzen Namen zu einem
        /// Bidi-Namen - und dann gilt die Regel auch für die anderen.
        /// </summary>
        [Test]
        public void TheRuleIsCatching()
        {

            Assert.Multiple(() =>
            {

                Assert.That(Gueltig($"{Hebraeisch}.example"), Is.True,
                            Grund($"{Hebraeisch}.example"));

                Assert.That(Gueltig($"9abc.{Hebraeisch}"), Is.False,
                            "Dasselbe Label, das ohne Nachbarn zulässig ist.");

                Assert.That(Grund($"9abc.{Hebraeisch}"), Does.Contain("9abc"),
                            "Und der Grund nennt das Label, an dem es liegt.");

                // Ein Label aus lauter arabischen Ziffern macht den Namen
                // ebenfalls zu einem Bidi-Namen (AN zählt mit) - und
                // verstösst dann selbst gegen Bedingung 1.
                Assert.That(Gueltig($"{ArabischeZiffer}.example"), Is.False,
                            "Ein Label aus lauter arabischen Ziffern.");

                // Das rechtsläufige Label steckt hier in seiner
                // ASCII-Verpackung. Wer die Bidi-Regel über die Verpackung
                // laufen lässt, sieht lauter lateinische Buchstaben und findet
                // nichts.
                Assert.That(Gueltig("9abc.xn--4dbcagdahymbxekheh6e0a7fei0b"), Is.False,
                            "Hebräisch als A-Label, daneben ein Label mit Ziffer am Anfang.");

            });

        }

        #endregion

        #region AnRtlLabel_KeepsItsDirection()

        /// <summary>
        /// Bedingung 1, 2 und 5: Ein Label hat eine Richtung, und die bestimmt
        /// sein erstes Zeichen.
        /// </summary>
        /// <remarks>
        /// <c>a{Hebraeisch}</c> beginnt links und trägt rechtsläufige Zeichen:
        /// Nach Bedingung 1 ist es ein LTR-Label, und Bedingung 5 lässt darin
        /// kein R zu. Andersherum darf ein RTL-Label keine lateinischen
        /// Buchstaben tragen (Bedingung 2).
        /// </remarks>
        [Test]
        public void AnRtlLabel_KeepsItsDirection()
        {

            Assert.Multiple(() =>
            {

                Assert.That(Gueltig($"a{Hebraeisch}.example"), Is.False,
                            "Ein LTR-Label mit hebräischen Zeichen.");

                Assert.That(Gueltig($"{Hebraeisch}a.example"), Is.False,
                            "Ein RTL-Label mit lateinischen Zeichen.");

                // Dieselben beiden Fälle, aber mit dem fremden Zeichen in der
                // Mitte statt am Ende. Das ist kein Feinschliff: Am Ende
                // scheitern sie schon an Bedingung 3 bzw. 6 - dass auch
                // Bedingung 2 und 5 etwas tun, zeigt erst diese Form.
                Assert.That(Gueltig($"אaב.example"), Is.False,
                            "Bedingung 2: ein L mitten in einem rechtsläufigen Label.");

                Assert.That(Gueltig($"aאb.example"), Is.False,
                            "Bedingung 5: ein R mitten in einem linksläufigen Label.");

                Assert.That(Gueltig($"{Arabisch}.example"),    Is.True,
                            Grund($"{Arabisch}.example"));

            });

        }

        #endregion

        #region AnRtlLabel_DoesNotMixTheTwoKindsOfDigits()

        /// <summary>
        /// Bedingung 4: In einem rechtsläufigen Label stehen europäische und
        /// arabische Ziffern nicht nebeneinander.
        /// </summary>
        /// <remarks>
        /// Das ist eine andere Regel als A.8/A.9 aus RFC 5892 und trifft ein
        /// anderes Paar: Dort ging es um die beiden <i>arabischen</i>
        /// Ziffernreihen, hier um arabische neben europäischen. Beide sagen
        /// dasselbe darüber, warum: Zwei Ziffernfolgen nebeneinander, die
        /// verschieden herum gelesen werden, ergeben eine Adresse, die niemand
        /// sicher vorlesen kann.
        /// </remarks>
        [Test]
        public void AnRtlLabel_DoesNotMixTheTwoKindsOfDigits()
        {

            Assert.Multiple(() =>
            {

                Assert.That(Gueltig($"{Hebraeisch}1.example"),  Is.True,
                            Grund($"{Hebraeisch}1.example"));

                Assert.That(Gueltig($"{Arabisch}{ArabischeZiffer}.example"), Is.True,
                            Grund($"{Arabisch}{ArabischeZiffer}.example"));

                Assert.That(Gueltig($"{Arabisch}1{ArabischeZiffer}.example"), Is.False,
                            "Europäische und arabische Ziffer im selben Label.");

            });

        }

        #endregion

        #region TheEndOfALabel()

        /// <summary>
        /// Bedingung 3 und 6: Woran ein Label enden darf.
        /// </summary>
        /// <remarks>
        /// Diese beiden Bedingungen sind über <see cref="Idna.IsValidDomain"/>
        /// nicht erreichbar - die Zeichen, mit denen ein Label falsch enden
        /// könnte (Trenn- und Sonderzeichen), sind schon auf der Codepoint-Ebene
        /// abgewiesen. Die Regel prüft sie trotzdem, denn sie ist die Regel aus
        /// dem RFC und nicht die Teilmenge, die dieser Aufrufer gerade
        /// durchlässt. Also wird sie hier unmittelbar gefragt.
        /// </remarks>
        [Test]
        public void TheEndOfALabel()
        {

            const String Punkt      = "·";  // MIDDLE DOT, Klasse ON
            const String Nsm        = "֑";  // HEBREW ACCENT ETNAHTA, Klasse NSM

            Assert.Multiple(() =>
            {

                Assert.That(Idna.SatisfiesBidiRule(Hebraeisch + Nsm, out _), Is.True,
                            "Bedingung 3: nach dem letzten R dürfen NSM folgen.");

                Assert.That(Idna.SatisfiesBidiRule(Hebraeisch + Punkt, out _), Is.False,
                            "Bedingung 3: ein ON am Ende ist keines der erlaubten Zeichen.");

                Assert.That(Idna.SatisfiesBidiRule("abc" + Punkt, out _), Is.False,
                            "Bedingung 6: dasselbe für ein LTR-Label.");

                Assert.That(Idna.SatisfiesBidiRule("abc1", out _), Is.True,
                            "Bedingung 6: eine europäische Ziffer darf ein LTR-Label beenden.");

            });

        }

        #endregion

        #region TheFirstCharacterDecides()

        /// <summary>
        /// Bedingung 1: Weder eine Ziffer noch ein neutrales Zeichen darf ein
        /// Label eröffnen.
        /// </summary>
        [Test]
        public void TheFirstCharacterDecides()
        {

            Assert.Multiple(() =>
            {
                Assert.That(Idna.SatisfiesBidiRule("1abc", out _),          Is.False, "EN am Anfang");
                Assert.That(Idna.SatisfiesBidiRule(ArabischeZiffer + "ب", out _), Is.False, "AN am Anfang");
                Assert.That(Idna.SatisfiesBidiRule("abc", out _),           Is.True);
                Assert.That(Idna.SatisfiesBidiRule(Hebraeisch, out _),      Is.True);
            });

        }

        #endregion

    }

}
