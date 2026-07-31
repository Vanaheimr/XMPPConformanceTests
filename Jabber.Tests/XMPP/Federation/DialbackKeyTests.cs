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

using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Der Dialback-Schlüssel gegen den veröffentlichten Vektor aus XEP-0220,
    /// Abschnitt 2.1.1.
    /// </summary>
    /// <remarks>
    /// Der Vektor ist hier nicht Zierde, sondern der Grund, warum die
    /// Implementierung stimmt. Zwei naheliegende Lesarten des Verfahrens
    /// liefern jeweils einen in sich stimmigen, aber falschen Schlüssel -
    /// <c>SHA256(Secret)</c> als Rohbytes statt als Hex-Zeichenkette, und die
    /// Domains in umgekehrter Reihenfolge. Beide fallen nur gegen einen
    /// fremden Vektor auf; gegen die eigene Gegenprobe wäre jede von ihnen
    /// grün.
    /// </remarks>
    [TestFixture]
    public class DialbackKeyTests
    {

        #region Data

        // XEP-0220, Abschnitt 2.1.1:
        //
        //   key = HMAC-SHA256(
        //           SHA256('s3cr3tf0rd14lb4ck'),
        //             { 'montague.example', ' ', 'capulet.example', ' ', 'D60000229F' }
        //         )
        //       = b4835385f37fe2895af6c196b59097b16862406db80559900d96bf6fa7d23df3
        //
        // montague.example ist dort der annehmende Server (Target Domain),
        // capulet.example der aufbauende (Sender Domain).

        private const String Secret        = "s3cr3tf0rd14lb4ck";
        private const String TargetDomain  = "montague.example";
        private const String SenderDomain  = "capulet.example";
        private const String StreamId      = "D60000229F";

        private const String ErwarteterSchluessel =
            "b4835385f37fe2895af6c196b59097b16862406db80559900d96bf6fa7d23df3";

        #endregion


        #region TheKeyMatchesThePublishedVector()

        /// <summary>
        /// Der Vektor aus dem XEP, exakt reproduziert.
        /// </summary>
        [Test]
        public void TheKeyMatchesThePublishedVector()
        {

            Assert.That(DialbackKey.Generate(Secret, TargetDomain, SenderDomain, StreamId),
                        Is.EqualTo(ErwarteterSchluessel));

        }

        #endregion

        #region TheDomainOrderMatters()

        /// <summary>
        /// Ziel- vor Absenderdomain. Vertauscht käme ebenfalls ein
        /// wohlgeformter Schlüssel heraus - nur eben ein anderer.
        /// </summary>
        /// <remarks>
        /// Verglichen werden zwei <b>erzeugte</b> Schlüssel und nicht einer
        /// gegen die veröffentlichte Konstante. Der Unterschied ist nicht
        /// kosmetisch: gegen die Konstante besteht so ein Test auch dann noch,
        /// wenn der geprüfte Bestandteil aus der Ableitung ganz herausfällt -
        /// das Ergebnis weicht dann ja erst recht ab. So gefragt beantwortet
        /// er dagegen genau das, was er behauptet: dass die Reihenfolge einen
        /// Unterschied macht.
        /// </remarks>
        [Test]
        public void TheDomainOrderMatters()
        {

            var herum       = DialbackKey.Generate(Secret, TargetDomain, SenderDomain, StreamId);
            var vertauscht  = DialbackKey.Generate(Secret, SenderDomain, TargetDomain, StreamId);

            Assert.That(vertauscht, Is.Not.EqualTo(herum));

        }

        #endregion

        #region TheStreamIdBindsTheKeyToOneConnection()

        /// <summary>
        /// Eine andere Stream-ID ergibt einen anderen Schlüssel - daran hängt,
        /// dass ein mitgeschnittener Schlüssel auf einer zweiten Verbindung
        /// nichts nützt.
        /// </summary>
        /// <remarks>
        /// Auch hier zwei erzeugte Schlüssel gegeneinander: gegen die
        /// Konstante geprüft blieb dieser Test grün, als die Stream-ID
        /// versuchsweise ganz aus der Ableitung genommen wurde - er hätte also
        /// genau den Fehler nicht bemerkt, gegen den er geschrieben ist.
        /// </remarks>
        [Test]
        public void TheStreamIdBindsTheKeyToOneConnection()
        {

            var eineVerbindung    = DialbackKey.Generate(Secret, TargetDomain, SenderDomain, StreamId);
            var andereVerbindung  = DialbackKey.Generate(Secret, TargetDomain, SenderDomain, "417GAF25");

            Assert.That(andereVerbindung, Is.Not.EqualTo(eineVerbindung));

        }

        #endregion

        #region AnotherSecretYieldsAnotherKey()

        /// <summary>
        /// Ohne das Geheimnis kommt niemand auf den Schlüssel - das ist der
        /// ganze Punkt von Dialback.
        /// </summary>
        [Test]
        public void AnotherSecretYieldsAnotherKey()
        {

            var eigen  = DialbackKey.Generate(Secret,          TargetDomain, SenderDomain, StreamId);
            var fremd  = DialbackKey.Generate("etwas anderes", TargetDomain, SenderDomain, StreamId);

            Assert.That(fremd, Is.Not.EqualTo(eigen));

        }

        #endregion

        #region VerifyAcceptsTheCorrectKey()

        /// <summary>
        /// Die Gegenrichtung: der autoritative Server rechnet nach und
        /// erkennt seinen eigenen Schlüssel wieder.
        /// </summary>
        [Test]
        public void VerifyAcceptsTheCorrectKey()
        {

            Assert.That(DialbackKey.Verify(Secret, TargetDomain, SenderDomain, StreamId, ErwarteterSchluessel),
                        Is.True);

        }

        #endregion

        #region VerifyIsCaseInsensitiveAboutHex()

        /// <summary>
        /// Hex in Grossbuchstaben ist derselbe Schlüssel. Ein Server, der ihn
        /// so schreibt, verstösst gegen nichts.
        /// </summary>
        [Test]
        public void VerifyIsCaseInsensitiveAboutHex()
        {

            Assert.That(DialbackKey.Verify(Secret, TargetDomain, SenderDomain, StreamId,
                                           ErwarteterSchluessel.ToUpperInvariant()),
                        Is.True);

        }

        #endregion

        #region VerifyIgnoresSurroundingWhitespace()

        /// <summary>
        /// Im XEP steht der Schlüssel eingerückt zwischen den Tags - der
        /// Zeilenumbruch davor und danach gehört nicht dazu.
        /// </summary>
        [Test]
        public void VerifyIgnoresSurroundingWhitespace()
        {

            Assert.That(DialbackKey.Verify(Secret, TargetDomain, SenderDomain, StreamId,
                                           $"\n        {ErwarteterSchluessel}\n      "),
                        Is.True);

        }

        #endregion

        #region VerifyRejectsAForgedKey()

        /// <summary>
        /// Ein Schlüssel, der nicht mit diesem Geheimnis entstanden ist, wird
        /// abgelehnt.
        /// </summary>
        [Test]
        public void VerifyRejectsAForgedKey()
        {

            var gefaelscht = DialbackKey.Generate("das Geheimnis des Angreifers",
                                                  TargetDomain, SenderDomain, StreamId);

            Assert.That(DialbackKey.Verify(Secret, TargetDomain, SenderDomain, StreamId, gefaelscht),
                        Is.False);

        }

        #endregion

        #region VerifyRejectsSomethingThatIsNotHexAtAll()

        /// <summary>
        /// Was gar kein Hex ist, führt zu einer Ablehnung und nicht zu einer
        /// Ausnahme - die Eingabe kommt von der Gegenstelle.
        /// </summary>
        [Test]
        public void VerifyRejectsSomethingThatIsNotHexAtAll()
        {

            Assert.That(DialbackKey.Verify(Secret, TargetDomain, SenderDomain, StreamId, "kein Schlüssel"),
                        Is.False);

        }

        #endregion

        #region EverySecretIsDifferent()

        /// <summary>
        /// <see cref="DialbackKey.NewSecret"/> liefert nicht zweimal dasselbe.
        /// </summary>
        [Test]
        public void EverySecretIsDifferent()
        {

            var a = DialbackKey.NewSecret();
            var b = DialbackKey.NewSecret();

            Assert.Multiple(() =>
            {
                Assert.That(a, Is.Not.EqualTo(b));
                Assert.That(a, Has.Length.EqualTo(64), "32 Bytes als Hex.");
            });

        }

        #endregion

    }

}
