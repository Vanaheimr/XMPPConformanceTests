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

using System.Xml.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Die Aushandlung des Streams in gültiger, aber ungewöhnlicher
    /// Schreibweise.
    ///
    /// Sie lief bis zuletzt über Textmuster: <c>&lt;mechanism&gt;…&lt;/mechanism&gt;</c>
    /// ohne Attribute, <c>xmlns</c> als erstes Attribut, <c>Contains</c> auf
    /// dem ganzen Rahmen. Diese Tests halten fest, was ein Server alles
    /// schicken darf, ohne dass der Verbindungsaufbau danebengreift.
    /// </summary>
    [TestFixture]
    public class StreamNegotiationTests
    {

        #region Hilfsfunktionen

        private static XElement Features(String inner)
            => XElement.Parse("<stream:features xmlns:stream='http://etherx.jabber.org/streams'>" +
                              inner +
                              "</stream:features>");

        #endregion


        #region SaslMechanisms_ReadsIndentedElements()

        /// <summary>
        /// Ein Server, der seine Features einrückt, schreibt den Namen des
        /// Mechanismus mit Zeilenumbruch und Leerzeichen drumherum. Das frühere
        /// Muster gab ihn ungeschnitten zurück, und der anschliessende
        /// Vergleich auf "PLAIN" ging ins Leere - der Client hielt den Server
        /// für einen ohne SASL.
        /// </summary>
        [Test]
        public void SaslMechanisms_ReadsIndentedElements()
        {

            var features = Features("<mechanisms xmlns='urn:ietf:params:xml:ns:xmpp-sasl'>\n" +
                                    "    <mechanism>\n      SCRAM-SHA-256\n    </mechanism>\n" +
                                    "    <mechanism>\n      PLAIN\n    </mechanism>\n" +
                                    "  </mechanisms>");

            Assert.That(StreamNegotiation.SaslMechanisms(features),
                        Is.EqualTo(new[] { "SCRAM-SHA-256", "PLAIN" }));

        }

        #endregion

        #region SaslMechanisms_ReadsRepeatedNamespaceDeclaration()

        /// <summary>
        /// Der Namespace darf am Kindelement wiederholt werden - überflüssig,
        /// aber gültig, und genau so serialisiert manche Bibliothek. Das
        /// frühere Muster verlangte <c>&lt;mechanism&gt;</c> ganz ohne
        /// Attribute und fand dann gar nichts mehr.
        /// </summary>
        [Test]
        public void SaslMechanisms_ReadsRepeatedNamespaceDeclaration()
        {

            var features = Features("<mechanisms xmlns='urn:ietf:params:xml:ns:xmpp-sasl'>" +
                                    "<mechanism xmlns='urn:ietf:params:xml:ns:xmpp-sasl'>PLAIN</mechanism>" +
                                    "</mechanisms>");

            Assert.That(StreamNegotiation.SaslMechanisms(features), Is.EqualTo(new[] { "PLAIN" }));

        }

        #endregion

        #region SaslMechanisms_IgnoresMechanismsOfAnotherFeature()

        /// <summary>
        /// Gesucht wird im <c>&lt;mechanisms/&gt;</c> von SASL, nicht irgendwo
        /// im Rahmen. Ein gleichnamiges Element einer anderen Erweiterung -
        /// etwa der Mechanismenliste einer Verschlüsselungsschicht - darf nicht
        /// in die Auswahl geraten, sonst versucht der Client einen Mechanismus,
        /// den der Server für SASL nie angeboten hat.
        /// </summary>
        [Test]
        public void SaslMechanisms_IgnoresMechanismsOfAnotherFeature()
        {

            var features = Features("<mechanisms xmlns='urn:ietf:params:xml:ns:xmpp-sasl'>" +
                                    "<mechanism>PLAIN</mechanism></mechanisms>" +
                                    "<mechanisms xmlns='urn:example:etwas-anderes'>" +
                                    "<mechanism>MAGIE</mechanism></mechanisms>");

            Assert.That(StreamNegotiation.SaslMechanisms(features), Is.EqualTo(new[] { "PLAIN" }));

        }

        #endregion

        #region FeatureNamespaces_FindsTrailingXmlns()

        /// <summary>
        /// Das frühere Muster verlangte <c>xmlns</c> als erstes Attribut. Die
        /// BCL serialisiert es aber als letztes, und XML schreibt keine
        /// Reihenfolge vor - solche Features fehlten in der Liste, und der
        /// Server wirkte weniger fähig, als er ist.
        /// </summary>
        [Test]
        public void FeatureNamespaces_FindsTrailingXmlns()
        {

            var features = Features("<c hash='sha-1' node='http://example.org/srv' ver='abc='" +
                                    " xmlns='http://jabber.org/protocol/caps'/>");

            Assert.That(StreamNegotiation.FeatureNamespaces(features),
                        Does.Contain("http://jabber.org/protocol/caps"));

        }

        #endregion

        #region FeatureNamespaces_IgnoresNestedElements()

        /// <summary>
        /// Angekündigt ist ein Feature durch ein <b>direktes</b> Kind von
        /// <c>&lt;features/&gt;</c>. Das frühere Muster suchte im ganzen Text
        /// und nahm auch Namespaces aus dem Inneren eines Features auf - der
        /// Client hielt den Server dann für fähig zu etwas, das dort nur als
        /// Detail vorkam.
        /// </summary>
        [Test]
        public void FeatureNamespaces_IgnoresNestedElements()
        {

            var features = Features("<mechanisms xmlns='urn:ietf:params:xml:ns:xmpp-sasl'>" +
                                    "<hinweis xmlns='urn:example:innen'/>" +
                                    "</mechanisms>");

            Assert.That(StreamNegotiation.FeatureNamespaces(features),
                        Is.EqualTo(new[] { "urn:ietf:params:xml:ns:xmpp-sasl" }));

        }

        #endregion

        #region RequiresSession_IgnoresTheOptionalOfAnotherFeature()

        /// <summary>
        /// Der Kern des Fehlers: <c>&lt;optional/&gt;</c> gehört jeweils zu
        /// genau einem Feature. XEP-0198 setzt es in sein eigenes, und die
        /// frühere Prüfung <c>!Contains("optional")</c> las das als Aussage
        /// über die Session - ein Server, der beides ankündigt, bekam die
        /// zwingende Session nie angefordert.
        /// </summary>
        [Test]
        public void RequiresSession_IgnoresTheOptionalOfAnotherFeature()
        {

            var features = Features("<session xmlns='urn:ietf:params:xml:ns:xmpp-session'/>" +
                                    "<sm xmlns='urn:xmpp:sm:3'><optional/></sm>");

            Assert.That(StreamNegotiation.RequiresSession(features), Is.True);

        }

        #endregion

        #region RequiresSession_False_WhenTheSessionItselfIsOptional()

        /// <summary>Die Gegenprobe: das eigene <c>&lt;optional/&gt;</c> zählt.</summary>
        [Test]
        public void RequiresSession_False_WhenTheSessionItselfIsOptional()
        {

            var features = Features("<session xmlns='urn:ietf:params:xml:ns:xmpp-session'><optional/></session>");

            Assert.That(StreamNegotiation.RequiresSession(features), Is.False);

        }

        #endregion

        #region OffersBind_AcceptsAPrefixedNamespace()

        /// <summary>
        /// Ob der Server den Bind-Namespace als Default setzt oder über ein
        /// Präfix bindet, ist seine Sache. Die frühere Prüfung
        /// <c>Contains("&lt;bind")</c> traf ein <c>&lt;b:bind/&gt;</c> nicht -
        /// der Client hätte das Binding übersprungen und wäre ohne Resource
        /// weitergelaufen.
        /// </summary>
        [Test]
        public void OffersBind_AcceptsAPrefixedNamespace()
        {

            var features = Features("<b:bind xmlns:b='urn:ietf:params:xml:ns:xmpp-bind'/>");

            Assert.That(StreamNegotiation.OffersBind(features), Is.True);

        }

        #endregion

        #region ReadBoundJid_ResolvesEntities()

        /// <summary>
        /// Der frühere Griff mit <c>&lt;jid&gt;([^&lt;]+)&lt;/jid&gt;</c> holte
        /// den Rohtext: aus <c>a&amp;amp;b</c> wurde nicht <c>a&amp;b</c>. Der
        /// Client hätte sich fortan mit einem JID gemeldet, den es so nicht
        /// gibt.
        /// </summary>
        [Test]
        public void ReadBoundJid_ResolvesEntities()
        {

            var iq = XElement.Parse("<iq type='result' id='bind1'>" +
                                    "<bind xmlns='urn:ietf:params:xml:ns:xmpp-bind'>" +
                                    "<jid>a&amp;b@example.org/console</jid>" +
                                    "</bind></iq>");

            Assert.That(StreamNegotiation.ReadBoundJid(iq), Is.EqualTo("a&b@example.org/console"));

        }

        #endregion

        #region ReadBoundJid_TrimsSurroundingWhitespace()

        /// <summary>
        /// Auch hier schlägt das Einrücken zu: der JID darf nicht mit
        /// Zeilenumbrüchen im Namen weiterverwendet werden.
        /// </summary>
        [Test]
        public void ReadBoundJid_TrimsSurroundingWhitespace()
        {

            var iq = XElement.Parse("<iq type='result' id='bind1'>\n" +
                                    "  <bind xmlns='urn:ietf:params:xml:ns:xmpp-bind'>\n" +
                                    "    <jid>\n      alice@example.org/console\n    </jid>\n" +
                                    "  </bind>\n</iq>");

            Assert.That(StreamNegotiation.ReadBoundJid(iq), Is.EqualTo("alice@example.org/console"));

        }

        #endregion

        #region ReadBoundJid_ReturnsNullForARejection()

        /// <summary>
        /// Ein abgelehntes Binding muss als Ablehnung erkennbar sein. Früher
        /// fiel der Client auf den selbst gewünschten JID zurück und meldete
        /// sich mit einer Resource online, die ihm nie zugeteilt wurde.
        /// </summary>
        [Test]
        public void ReadBoundJid_ReturnsNullForARejection()
        {

            var iq = XElement.Parse("<iq type='error' id='bind1'><error type='cancel'>" +
                                    "<not-allowed xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/>" +
                                    "</error></iq>");

            Assert.That(StreamNegotiation.ReadBoundJid(iq), Is.Null);

        }

        #endregion

        #region ReadBoundJid_IgnoresAJidOfAnotherPayload()

        /// <summary>
        /// Der JID muss aus dem <c>&lt;bind/&gt;</c> stammen. Die Textsuche
        /// fand jedes <c>&lt;jid/&gt;</c> im Rahmen - auch eines, das zu einer
        /// ganz anderen Nutzlast gehört.
        /// </summary>
        [Test]
        public void ReadBoundJid_IgnoresAJidOfAnotherPayload()
        {

            var iq = XElement.Parse("<iq type='result' id='bind1'>" +
                                    "<query xmlns='urn:example:etwas-anderes'>" +
                                    "<jid>fremd@example.org/x</jid></query></iq>");

            Assert.That(StreamNegotiation.ReadBoundJid(iq), Is.Null);

        }

        #endregion

        #region IsSasl_ChecksTheNamespace()

        /// <summary>
        /// <c>&lt;success/&gt;</c> ist ein häufiger Elementname. Nur eines aus
        /// dem SASL-Namespace beendet die Authentifizierung; die frühere Suche
        /// nach der Zeichenfolge <c>"&lt;success"</c> im Rohtext hätte auch
        /// jedes andere akzeptiert.
        /// </summary>
        [Test]
        public void IsSasl_ChecksTheNamespace()
        {

            var fremd = XElement.Parse("<success xmlns='urn:example:etwas-anderes'/>");
            var echt  = XElement.Parse("<success xmlns='urn:ietf:params:xml:ns:xmpp-sasl'/>");

            Assert.Multiple(() =>
            {
                Assert.That(StreamNegotiation.IsSasl(fremd, "success"), Is.False);
                Assert.That(StreamNegotiation.IsSasl(echt,  "success"), Is.True);
            });

        }

        #endregion

        #region SaslPayload_IsEmptyWithoutAServerFinalMessage()

        /// <summary>
        /// Die Grundlage der SCRAM-Prüfung: ein <c>&lt;success/&gt;</c> ohne
        /// Inhalt trägt keine server-final-message. Nach RFC 5802,
        /// Abschnitt 5 ist die Signatur damit nicht prüfbar - der
        /// Verbindungsaufbau bricht dort jetzt ab, statt die gegenseitige
        /// Authentifizierung stillschweigend fallen zu lassen.
        /// </summary>
        [Test]
        public void SaslPayload_IsEmptyWithoutAServerFinalMessage()
        {

            var leer   = XElement.Parse("<success xmlns='urn:ietf:params:xml:ns:xmpp-sasl'/>");
            var gefuellt = XElement.Parse("<success xmlns='urn:ietf:params:xml:ns:xmpp-sasl'>\n" +
                                          "  dj1yUnRDMXBUUw==\n</success>");

            Assert.Multiple(() =>
            {
                Assert.That(StreamNegotiation.SaslPayload(leer),     Is.Empty);
                Assert.That(StreamNegotiation.SaslPayload(gefuellt), Is.EqualTo("dj1yUnRDMXBUUw=="));
            });

        }

        #endregion

        #region SaslFailureCondition_SkipsTheTextElement()

        /// <summary>
        /// RFC 6120, Abschnitt 6.5 erlaubt ein erläuterndes
        /// <c>&lt;text/&gt;</c> neben der Bedingung, ohne die Reihenfolge
        /// festzulegen. Gemeldet gehört die Bedingung, nicht der Erläuterungstext.
        /// </summary>
        [Test]
        public void SaslFailureCondition_SkipsTheTextElement()
        {

            var failure = XElement.Parse("<failure xmlns='urn:ietf:params:xml:ns:xmpp-sasl'>" +
                                         "<text>Falsches Passwort</text>" +
                                         "<not-authorized/></failure>");

            Assert.That(StreamNegotiation.SaslFailureCondition(failure), Is.EqualTo("not-authorized"));

        }

        #endregion

    }

}
