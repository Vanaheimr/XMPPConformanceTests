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
    /// RFC 6120, Abschnitt 8.3 (Stanza-Fehler) und Abschnitt 4.9
    /// (Stream-Fehler), ohne Netzwerk.
    /// </summary>
    [TestFixture]
    public class StanzaErrorTests
    {

        #region Parse_ReadsTypeConditionAndText()

        /// <summary>
        /// Das vollständige Beispiel aus Abschnitt 8.3.2.
        /// </summary>
        [Test]
        public void Parse_ReadsTypeConditionAndText()
        {

            var ok = StanzaError.TryParse(
                         "<iq type='error' id='1' from='example.org'>" +
                         "<error type='modify' by='example.org'>" +
                         "<bad-request xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/>" +
                         "<text xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'>Das war nichts.</text>" +
                         "</error></iq>",
                         out var error);

            Assert.Multiple(() =>
            {
                Assert.That(ok,                Is.True);
                Assert.That(error!.Type,       Is.EqualTo(StanzaErrorType.Modify));
                Assert.That(error!.Condition,  Is.EqualTo("bad-request"));
                Assert.That(error!.Text,       Is.EqualTo("Das war nichts."));
                Assert.That(error!.By,         Is.EqualTo("example.org"));
            });

        }

        #endregion

        #region Parse_MapsAllErrorTypes()

        /// <summary>
        /// Alle fünf Fehlerarten aus Abschnitt 8.3.2.
        /// </summary>
        [Test]
        [TestCase("auth",     StanzaErrorType.Auth)]
        [TestCase("cancel",   StanzaErrorType.Cancel)]
        [TestCase("continue", StanzaErrorType.Continue)]
        [TestCase("modify",   StanzaErrorType.Modify)]
        [TestCase("wait",     StanzaErrorType.Wait)]
        public void Parse_MapsAllErrorTypes(String attribute, StanzaErrorType expected)
        {

            StanzaError.TryParse(
                $"<message type='error'><error type='{attribute}'>" +
                "<forbidden xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/>" +
                "</error></message>",
                out var error);

            Assert.That(error!.Type, Is.EqualTo(expected));

        }

        #endregion

        #region Parse_FallsBackToCancelOnUnknownType()

        /// <summary>
        /// Bei fehlender oder unbekannter Fehlerart ist <c>cancel</c> die
        /// vorsichtigste Annahme: sie führt zu keinem Wiederholungsversuch.
        /// </summary>
        [Test]
        [TestCase("<error><forbidden xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/></error>")]
        [TestCase("<error type='vollkommen-neu'><forbidden xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/></error>")]
        public void Parse_FallsBackToCancelOnUnknownType(String errorElement)
        {

            StanzaError.TryParse($"<iq type='error'>{errorElement}</iq>", out var error);

            Assert.That(error!.Type, Is.EqualTo(StanzaErrorType.Cancel));

        }

        #endregion

        #region Parse_KeepsUnknownConditions()

        /// <summary>
        /// Die Bedingung bleibt eine Zeichenkette, damit auch künftige und
        /// anwendungsspezifische Bedingungen unverfälscht durchkommen.
        /// </summary>
        [Test]
        public void Parse_KeepsUnknownConditions()
        {

            StanzaError.TryParse(
                "<iq type='error'><error type='cancel'>" +
                "<irgendwas-neues xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/>" +
                "</error></iq>",
                out var error);

            Assert.That(error!.Condition, Is.EqualTo("irgendwas-neues"));

        }

        #endregion

        #region Parse_DoesNotMistakeTextForTheCondition()

        /// <summary>
        /// <c>&lt;text/&gt;</c> liegt im selben Namespace wie die Bedingung und
        /// darf nicht als solche gelesen werden - auch dann nicht, wenn es
        /// zuerst steht.
        /// </summary>
        [Test]
        public void Parse_DoesNotMistakeTextForTheCondition()
        {

            StanzaError.TryParse(
                "<iq type='error'><error type='cancel'>" +
                "<text xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'>Zuerst der Text.</text>" +
                "<gone xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/>" +
                "</error></iq>",
                out var error);

            Assert.Multiple(() =>
            {
                Assert.That(error!.Condition, Is.EqualTo("gone"));
                Assert.That(error!.Text,      Is.EqualTo("Zuerst der Text."));
            });

        }

        #endregion

        #region Parse_ReturnsFalseWithoutErrorElement()

        /// <summary>
        /// Eine Stanza ohne error-Element ist kein Fehler.
        /// </summary>
        [Test]
        [TestCase("<message type='chat'><body>Hallo</body></message>")]
        [TestCase("<iq type='result' id='1'/>")]
        [TestCase("")]
        public void Parse_ReturnsFalseWithoutErrorElement(String stanza)
        {
            Assert.That(StanzaError.TryParse(stanza, out _), Is.False);
        }

        #endregion

        #region StreamError_ReadsConditionAndText()

        /// <summary>
        /// Stream-Fehler nach Abschnitt 4.9, inklusive des üblichen
        /// stream-Präfixes.
        /// </summary>
        [Test]
        public void StreamError_ReadsConditionAndText()
        {

            var ok = StreamError.TryParse(
                         "<stream:error xmlns:stream='http://etherx.jabber.org/streams'>" +
                         "<conflict xmlns='urn:ietf:params:xml:ns:xmpp-streams'/>" +
                         "<text xmlns='urn:ietf:params:xml:ns:xmpp-streams'>Resource doppelt.</text>" +
                         "</stream:error>",
                         out var error);

            Assert.Multiple(() =>
            {
                Assert.That(ok,                Is.True);
                Assert.That(error!.Condition,  Is.EqualTo("conflict"));
                Assert.That(error!.Text,       Is.EqualTo("Resource doppelt."));
            });

        }

        #endregion

        #region StreamError_SeparatesRecoverableFromFatal()

        /// <summary>
        /// Die Unterscheidung entscheidet, ob ein Reconnect versucht wird. Bei
        /// den endgültigen Bedingungen liefe er in dieselbe Ablehnung und
        /// erzeugte eine Schleife.
        /// </summary>
        [Test]
        [TestCase("system-shutdown",          true)]
        [TestCase("connection-timeout",       true)]
        [TestCase("resource-constraint",      true)]
        [TestCase("internal-server-error",    true)]
        [TestCase("reset",                    true)]
        [TestCase("conflict",                 false)]
        [TestCase("host-unknown",             false)]
        [TestCase("not-authorized",           false)]
        [TestCase("policy-violation",         false)]
        [TestCase("unsupported-version",      false)]
        [TestCase("see-other-host",           false)]
        public void StreamError_SeparatesRecoverableFromFatal(String condition, Boolean recoverable)
        {

            StreamError.TryParse(
                "<stream:error xmlns:stream='http://etherx.jabber.org/streams'>" +
                $"<{condition} xmlns='urn:ietf:params:xml:ns:xmpp-streams'/>" +
                "</stream:error>",
                out var error);

            Assert.That(error!.IsRecoverable, Is.EqualTo(recoverable),
                        $"'{condition}' ist falsch eingestuft.");

        }

        #endregion

        #region StreamError_ReturnsFalseForOtherStanzas()

        /// <summary>
        /// Ein Stanza-Fehler ist kein Stream-Fehler.
        /// </summary>
        [Test]
        [TestCase("<message type='error'><error type='cancel'/></message>")]
        [TestCase("<iq type='error' id='1'/>")]
        [TestCase("<presence/>")]
        public void StreamError_ReturnsFalseForOtherStanzas(String stanza)
        {
            Assert.That(StreamError.TryParse(stanza, out _), Is.False);
        }

        #endregion

    }

}
