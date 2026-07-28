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
    /// Der Content-Namensraum einer Stanza (RFC 6120, Abschnitt 4.8.1).
    /// </summary>
    /// <remarks>
    /// Zwei Fehler, die beide erst gegen Prosody sichtbar wurden und beide
    /// dieselbe Ursache haben: eine Stanza trug den Namensraum nicht, den der
    /// Stream unter ihr erwartete.
    ///
    /// Über TCP fällt das nie auf, weil der Namensraum einmal am
    /// <c>&lt;stream:stream&gt;</c> steht und für alles darin gilt. Über
    /// WebSocket gibt es dieses Element nicht (RFC 7395, Abschnitt 3.3.3), und
    /// über die Domain-Grenze wechselt er von <c>jabber:client</c> auf
    /// <c>jabber:server</c>.
    /// </remarks>
    [TestFixture]
    public class StanzaNamespaceTests
    {

        #region Apply_StampsStanzasThatCarryNone()

        /// <summary>
        /// Eine Stanza ohne eigenen Namensraum bekommt einen.
        /// </summary>
        /// <remarks>
        /// Unser eigener Server hat das nie bemängelt, weil er Stanzas am
        /// lokalen Namen erkennt. Prosody schon: es beantwortete das Bind-IQ
        /// mit <c>&lt;unsupported-stanza-type/&gt;</c> und schloss den Stream.
        /// Der Client konnte sich damit an keinem RFC-7395-konformen Server
        /// anmelden.
        /// </remarks>
        [Test]
        public void Apply_StampsStanzasThatCarryNone()
        {

            Assert.Multiple(() =>
            {

                Assert.That(StanzaNamespace.Apply("<presence/>", StanzaNamespace.Client),
                            Is.EqualTo("<presence xmlns='jabber:client'/>"));

                Assert.That(StanzaNamespace.Apply(
                                "<message to='bob@example.com' type='chat'><body>Hallo</body></message>",
                                StanzaNamespace.Client),
                            Is.EqualTo("<message xmlns='jabber:client' to='bob@example.com' " +
                                       "type='chat'><body>Hallo</body></message>"));

                // Der Namensraum des Kindelements ist nicht der der Stanza -
                // genau die Verwechslung, an der ein "steht da irgendwo ein
                // xmlns" scheitern würde.
                Assert.That(StanzaNamespace.Apply(
                                "<iq type='set' id='bind1'>" +
                                "<bind xmlns='urn:ietf:params:xml:ns:xmpp-bind'/></iq>",
                                StanzaNamespace.Client),
                            Is.EqualTo("<iq xmlns='jabber:client' type='set' id='bind1'>" +
                                       "<bind xmlns='urn:ietf:params:xml:ns:xmpp-bind'/></iq>"));

            });

        }

        #endregion

        #region Apply_ExchangesTheNamespaceAtTheDomainBoundary()

        /// <summary>
        /// Was von einem Client kam, geht als <c>jabber:server</c> hinaus.
        /// </summary>
        /// <remarks>
        /// Der zweite Fehler, und der Client-Fix hat ihn erst herausgeholt:
        /// solange die Stanza gar keinen Namensraum trug, erbte sie auf dem
        /// S2S-Stream stillschweigend den richtigen. Mit
        /// <c>jabber:client</c> darin ist sie dort keine gültige Stanza mehr -
        /// Prosody antwortete mit einem Fehler-IQ, und der Ping-Rundlauf
        /// scheiterte.
        /// </remarks>
        [Test]
        public void Apply_ExchangesTheNamespaceAtTheDomainBoundary()
        {

            Assert.Multiple(() =>
            {

                Assert.That(StanzaNamespace.Apply(
                                "<iq xmlns='jabber:client' from='alice@a.example' " +
                                "to='b.example' type='get' id='ping-1'>" +
                                "<ping xmlns='urn:xmpp:ping'/></iq>",
                                StanzaNamespace.Server),
                            Is.EqualTo("<iq xmlns='jabber:server' from='alice@a.example' " +
                                       "to='b.example' type='get' id='ping-1'>" +
                                       "<ping xmlns='urn:xmpp:ping'/></iq>"));

                // Auch in der anderen Schreibweise der Anführungszeichen.
                Assert.That(StanzaNamespace.Apply(
                                "<message xmlns=\"jabber:client\"><body>x</body></message>",
                                StanzaNamespace.Server),
                            Is.EqualTo("<message xmlns='jabber:server'><body>x</body></message>"));

            });

        }

        #endregion

        #region Apply_LeavesEverythingElseAlone()

        /// <summary>
        /// Angefasst wird nur, was eine Stanza ist und noch nicht stimmt.
        /// </summary>
        /// <remarks>
        /// Nonzas gehören in ihren eigenen Namensraum - ein
        /// <c>&lt;enable/&gt;</c> nach <c>jabber:client</c> umzuhängen machte
        /// es unlesbar. Und eine Stanza, die den gewünschten Namensraum schon
        /// trägt, käme sonst mit einer zweiten Deklaration zurück und wäre kein
        /// wohlgeformtes XML mehr.
        /// </remarks>
        [Test]
        public void Apply_LeavesEverythingElseAlone()
        {

            var unangetastet = new[] {
                "<enable xmlns='urn:xmpp:sm:3'/>",
                "<r xmlns='urn:xmpp:sm:3'/>",
                "<auth xmlns='urn:ietf:params:xml:ns:xmpp-sasl' mechanism='SCRAM-SHA-1'>abc</auth>",
                "<open xmlns='urn:ietf:params:xml:ns:xmpp-framing' to='example.com' version='1.0'/>",
                "<iq xmlns='jabber:client' type='get' id='ping1'/>",
                "<message xmlns=\"jabber:client\"><body>x</body></message>"
            };

            Assert.Multiple(() =>
            {
                foreach (var xml in unangetastet)
                    Assert.That(StanzaNamespace.Apply(xml, StanzaNamespace.Client), Is.EqualTo(xml),
                                $"Angefasst, obwohl nichts zu tun war: {xml}");
            });

        }

        #endregion

        #region Apply_IsNotFooledByAPrefixDeclaration()

        /// <summary>
        /// <c>xmlns:foo</c> deklariert keinen Standard-Namensraum.
        /// </summary>
        /// <remarks>
        /// Eine Stanza mit einer Präfix-Deklaration und ohne
        /// Standard-Namensraum steht weiterhin in keinem - wer die beiden
        /// verwechselt, lässt genau sie durch.
        /// </remarks>
        [Test]
        public void Apply_IsNotFooledByAPrefixDeclaration()
        {

            Assert.That(StanzaNamespace.Apply(
                            "<iq xmlns:db='jabber:server:dialback' type='get' id='x'/>",
                            StanzaNamespace.Server),
                        Is.EqualTo("<iq xmlns='jabber:server' xmlns:db='jabber:server:dialback' " +
                                   "type='get' id='x'/>"));

        }

        #endregion

        #region Apply_SurvivesAGreaterThanInsideAnAttribute()

        /// <summary>
        /// Ein <c>&gt;</c> im Attributwert beendet das Start-Tag nicht.
        /// </summary>
        /// <remarks>
        /// XML verlangt kein Escaping von <c>&gt;</c> in Attributwerten. Wer
        /// das Start-Tag am ersten <c>&gt;</c> enden lässt, sucht den
        /// Namensraum in der halben Stanza und setzt ihn an die falsche Stelle.
        /// </remarks>
        [Test]
        public void Apply_SurvivesAGreaterThanInsideAnAttribute()
        {

            Assert.That(StanzaNamespace.Apply("<message id='a>b' xmlns='jabber:client'/>",
                                              StanzaNamespace.Client),
                        Is.EqualTo("<message id='a>b' xmlns='jabber:client'/>"));

        }

        #endregion

    }

}
