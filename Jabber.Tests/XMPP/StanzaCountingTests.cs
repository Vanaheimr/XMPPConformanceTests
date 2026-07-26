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
    /// XEP-0198: die beiden Bausteine der Zählung ohne Netzwerk -
    /// was zählt als Stanza, und wann gilt eine Sequenznummer als bestätigt.
    /// </summary>
    [TestFixture]
    public class StanzaCountingTests
    {

        #region Stanzas_AreCounted()

        /// <summary>
        /// XEP-0198 Abschnitt 2 zählt genau message, presence und iq.
        /// </summary>
        [Test]
        [TestCase("<message to='a@b' type='chat'><body>x</body></message>")]
        [TestCase("<presence/>")]
        [TestCase("<iq type='get' id='1'><ping xmlns='urn:xmpp:ping'/></iq>")]
        [TestCase("<message/>")]
        [TestCase("  <presence type='unavailable'/>")]
        [TestCase("<client:message xmlns:client='jabber:client'/>")]
        public void Stanzas_AreCounted(String xml)
        {
            Assert.That(StreamManagementManager.IsCountableStanza(xml), Is.True);
        }

        #endregion

        #region Nonzas_AreNotCounted()

        /// <summary>
        /// Nonzas zählen nicht. Besonders heikel sind <c>&lt;a/&gt;</c> und
        /// <c>&lt;r/&gt;</c>: sie laufen bei jedem Keepalive über dieselbe
        /// Sendestrecke wie echte Stanzas.
        /// </summary>
        [Test]
        [TestCase("<r xmlns='urn:xmpp:sm:3'/>")]
        [TestCase("<a xmlns='urn:xmpp:sm:3' h='12'/>")]
        [TestCase("<enable xmlns='urn:xmpp:sm:3' resume='true'/>")]
        [TestCase("<enabled xmlns='urn:xmpp:sm:3' id='x'/>")]
        [TestCase("<open xmlns='urn:ietf:params:xml:ns:xmpp-framing' to='example.org'/>")]
        [TestCase("<close xmlns='urn:ietf:params:xml:ns:xmpp-framing'/>")]
        [TestCase("<auth xmlns='urn:ietf:params:xml:ns:xmpp-sasl' mechanism='PLAIN'>eA==</auth>")]
        [TestCase("<success xmlns='urn:ietf:params:xml:ns:xmpp-sasl'/>")]
        [TestCase("<stream:features xmlns:stream='http://etherx.jabber.org/streams'/>")]
        [TestCase("")]
        [TestCase("kein XML")]
        public void Nonzas_AreNotCounted(String xml)
        {
            Assert.That(StreamManagementManager.IsCountableStanza(xml), Is.False);
        }

        #endregion

        #region ElementsWithStanzaPrefix_AreNotCounted()

        /// <summary>
        /// Ein blosser Präfixvergleich wie <c>StartsWith("&lt;a")</c> würde
        /// auch <c>&lt;auth/&gt;</c> treffen. Der Elementname muss vollständig
        /// übereinstimmen.
        /// </summary>
        [Test]
        [TestCase("<iqbogus/>")]
        [TestCase("<messages/>")]
        [TestCase("<presence-probe/>")]
        public void ElementsWithStanzaPrefix_AreNotCounted(String xml)
        {
            Assert.That(StreamManagementManager.IsCountableStanza(xml), Is.False);
        }

        #endregion

        #region Acknowledgement_UsesModuloArithmetic()

        /// <summary>
        /// Der Zähler ist ein 32-Bit-Wert, der nach 2^32-1 auf 0 überläuft
        /// (XEP-0198, Abschnitt 4). Ein einfaches <c>Seq &lt;= h</c> würde die
        /// noch offenen Stanzas direkt nach dem Überlauf für immer in der
        /// Queue liegen lassen.
        /// </summary>
        [Test]
        [TestCase(1u,          1u,          true,  TestName = "Genau bestätigt")]
        [TestCase(1u,          5u,          true,  TestName = "Älter als h")]
        [TestCase(5u,          1u,          false, TestName = "Neuer als h")]
        [TestCase(5u,          4u,          false, TestName = "Eins zu neu")]
        [TestCase(UInt32.MaxValue, 1u,      true,  TestName = "Ueberlauf: h hat umgeschlagen")]
        [TestCase(UInt32.MaxValue, 0u,      true,  TestName = "Ueberlauf: h genau auf 0")]
        [TestCase(1u,          UInt32.MaxValue, false, TestName = "h liegt weit zurueck")]
        public void Acknowledgement_UsesModuloArithmetic(UInt32 seq, UInt32 h, Boolean expected)
        {
            Assert.That(StreamManagementManager.IsAcknowledged(seq, h), Is.EqualTo(expected));
        }

        #endregion

    }

}
