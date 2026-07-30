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
    /// Der Leser, an dem die Weiche für eingehende Rahmen hängt.
    /// </summary>
    /// <remarks>
    /// Ohne Server geprüft, und das ist kein Sparen: Die Fragen hier sind
    /// Fragen an eine Zeichenkette. Ein Fixture mit Server könnte sie zwar auch
    /// beantworten, aber nur über den Umweg einer Wirkung — und wo es keinen
    /// Umweg braucht, verdeckt er nur, was gemessen wird.
    /// </remarks>
    [TestFixture]
    public class StanzaElementTests
    {

        #region NameOf_ReadsTheNameToItsEnd()

        /// <summary>
        /// Der Name endet beim ersten Zeichen, das nicht mehr zu ihm gehört.
        /// </summary>
        /// <remarks>
        /// Der Fall, der das Ganze ausgelöst hat, steht in der Mitte:
        /// <c>&lt;presence-probe/&gt;</c> heisst <c>presence-probe</c> und nicht
        /// <c>presence</c>. Der Bindestrich gehört zum Namen (XML 1.0,
        /// Abschnitt 2.3), und wer ihn nicht mitliest, macht aus dem Element ein
        /// anderes.
        /// </remarks>
        [Test]
        [TestCase("<iq/>",                     "iq")]
        [TestCase("<iq type='get' id='x'/>",   "iq")]
        [TestCase("<iq>text</iq>",             "iq")]
        [TestCase("<iqbogus/>",                "iqbogus")]
        [TestCase("<presence-probe/>",         "presence-probe")]
        [TestCase("<messages/>",               "messages")]
        [TestCase("<opencast/>",               "opencast")]
        [TestCase("<a_b/>",                    "a_b")]
        [TestCase("<a1/>",                     "a1")]
        public void NameOf_ReadsTheNameToItsEnd(String xml, String erwartet)
        {
            Assert.That(StanzaElement.NameOf(xml), Is.EqualTo(erwartet));
        }

        #endregion

        #region NameOf_DropsTheNamespacePrefix()

        /// <summary>
        /// Das Präfix gehört nicht zum Typ: <c>&lt;client:iq/&gt;</c> ist ein
        /// <c>iq</c>.
        /// </summary>
        /// <remarks>
        /// RFC 6120, Abschnitt 4.8.1 legt den Namensraum fest, nicht die
        /// Abkürzung, unter der er angesprochen wird. Ein Server, der am Präfix
        /// scheitert, scheitert an einer Freiheit, die der RFC ausdrücklich
        /// lässt — und zwei Gegenstellen machen davon verschieden Gebrauch:
        /// <c>&lt;stream:features/&gt;</c> und <c>&lt;features/&gt;</c> sind
        /// dasselbe Element.
        /// </remarks>
        [Test]
        [TestCase("<client:iq/>",        "iq")]
        [TestCase("<stream:features/>",  "features")]
        [TestCase("<db:result/>",        "result")]
        public void NameOf_DropsTheNamespacePrefix(String xml, String erwartet)
        {
            Assert.That(StanzaElement.NameOf(xml), Is.EqualTo(erwartet));
        }

        #endregion

        #region NameOf_SkipsLeadingWhitespace()

        /// <summary>
        /// Führender Leerraum vor dem Element wird übergangen.
        /// </summary>
        /// <remarks>
        /// Über WebSocket kommt ein Rahmen zwar meist ohne, aber über TCP steht
        /// der Zerleger vor einem Strom, in dem Leerraum als Keepalive erlaubt
        /// ist (RFC 6120, Abschnitt 4.6.1). Ein Leser, der daran scheitert,
        /// scheiterte an einem Leerzeichen.
        /// </remarks>
        [Test]
        [TestCase(" <iq/>")]
        [TestCase("\r\n\t<iq/>")]
        public void NameOf_SkipsLeadingWhitespace(String xml)
        {
            Assert.That(StanzaElement.NameOf(xml), Is.EqualTo("iq"));
        }

        #endregion

        #region NameOf_HasNoNameWithoutAnElement()

        /// <summary>
        /// Was mit keinem Element beginnt, hat keinen Namen — und darf keinen
        /// erfinden.
        /// </summary>
        /// <remarks>
        /// <c>&lt;/iq&gt;</c> steht ausdrücklich dabei: Ein schliessendes
        /// Element ist kein Element, das ankommt. Ohne diese Unterscheidung
        /// würde eine Weiche ein <c>&lt;/stream:stream&gt;</c> für einen Stream
        /// halten, der beginnt.
        /// </remarks>
        [Test]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("kein XML")]
        [TestCase("<>")]
        [TestCase("</iq>")]
        public void NameOf_HasNoNameWithoutAnElement(String xml)
        {
            Assert.That(StanzaElement.NameOf(xml), Is.Null);
        }

        #endregion

        #region IsStanza_KnowsTheThreeFromSection81()

        /// <summary>
        /// RFC 6120, Abschnitt 8.1 kennt drei Stanzas und keine vierte.
        /// </summary>
        [Test]
        [TestCase("<message/>",        true)]
        [TestCase("<presence/>",       true)]
        [TestCase("<iq/>",             true)]
        [TestCase("<client:message/>", true)]
        [TestCase("<iqbogus/>",        false)]
        [TestCase("<messages/>",       false)]
        [TestCase("<presence-probe/>", false)]
        [TestCase("<open/>",           false)]
        [TestCase("<r/>",              false)]
        [TestCase("kein XML",          false)]
        public void IsStanza_KnowsTheThreeFromSection81(String xml, Boolean erwartet)
        {
            Assert.That(StanzaElement.IsStanza(xml), Is.EqualTo(erwartet));
        }

        #endregion

        #region Is_ComparesTheWholeName()

        /// <summary>
        /// <c>Is</c> vergleicht den ganzen Namen und nicht seinen Anfang.
        /// </summary>
        [Test]
        [TestCase("<open xmlns='urn:ietf:params:xml:ns:xmpp-framing'/>", "open",  true)]
        [TestCase("<opencast/>",                                         "open",  false)]
        [TestCase("<close/>",                                            "close", true)]
        [TestCase("<closet/>",                                           "close", false)]
        [TestCase("<r xmlns='urn:xmpp:sm:3'/>",                          "r",     true)]
        [TestCase("<resume xmlns='urn:xmpp:sm:3'/>",                     "r",     false)]
        [TestCase("<a xmlns='urn:xmpp:sm:3' h='1'/>",                    "a",     true)]
        [TestCase("<auth xmlns='urn:ietf:params:xml:ns:xmpp-sasl'/>",    "a",     false)]
        public void Is_ComparesTheWholeName(String xml, String name, Boolean erwartet)
        {
            Assert.That(StanzaElement.Is(xml, name), Is.EqualTo(erwartet));
        }

        #endregion

    }

}
