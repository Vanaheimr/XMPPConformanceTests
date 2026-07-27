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
    /// Der Zerleger, der aus dem TCP-Zeichenstrom eines XMPP-Streams einzelne
    /// Rahmen macht.
    /// </summary>
    /// <remarks>
    /// Diese Tests sind der Grund, warum der Zerleger ein eigener Baustein ist
    /// und kein Handgriff in der Empfangsschleife: über localhost fallen die
    /// Pakete fast immer zufällig auf Elementgrenzen, ein fehlerhafter
    /// Zerleger fiele dort also nie auf. Deshalb wird hier absichtlich an den
    /// unbequemsten Stellen getrennt - mitten im Tag, mitten im Attributwert,
    /// zeichenweise.
    /// </remarks>
    [TestFixture]
    public class XmlStreamSplitterTests
    {

        #region Data

        private const String Kopf =
            "<stream:stream xmlns='jabber:server' " +
            "xmlns:stream='http://etherx.jabber.org/streams' " +
            "from='links.example' to='rechts.example' id='abc' version='1.0'>";

        #endregion

        #region Hilfsfunktionen

        /// <summary>Schiebt den Text in einem Stück hinein.</summary>
        private static List<String> Alles(String text)
            => [.. new XmlStreamSplitter().Push(text)];

        /// <summary>Schiebt den Text Zeichen für Zeichen hinein.</summary>
        private static List<String> Zeichenweise(String text)
        {

            var zerleger  = new XmlStreamSplitter();
            var rahmen    = new List<String>();

            foreach (var c in text)
                rahmen.AddRange(zerleger.Push(c.ToString()));

            return rahmen;

        }

        #endregion


        #region TheStreamHeaderComesOutOnItsOwn()

        /// <summary>
        /// Der Stream-Kopf ist ein offenes Tag - er darf nicht auf sein
        /// schliessendes warten, sonst käme nie ein Rahmen heraus.
        /// </summary>
        [Test]
        public void TheStreamHeaderComesOutOnItsOwn()
        {

            var rahmen = Alles(Kopf);

            Assert.Multiple(() =>
            {
                Assert.That(rahmen, Has.Count.EqualTo(1));
                Assert.That(rahmen[0], Is.EqualTo(Kopf));
            });

        }

        #endregion

        #region StanzasAfterTheHeaderComeOutOneByOne()

        [Test]
        public void StanzasAfterTheHeaderComeOutOneByOne()
        {

            var rahmen = Alles(Kopf +
                               "<message from='a@links.example' to='b@rechts.example'><body>eins</body></message>" +
                               "<presence from='a@links.example'/>");

            Assert.Multiple(() =>
            {
                Assert.That(rahmen, Has.Count.EqualTo(3));
                Assert.That(rahmen[1], Does.Contain("eins").And.StartWith("<message"));
                Assert.That(rahmen[2], Is.EqualTo("<presence from='a@links.example'/>"));
            });

        }

        #endregion

        #region AStanzaSplitAcrossReads_IsStillOneFrame()

        /// <summary>
        /// Der eigentliche Punkt: TCP kennt keine Nachrichtengrenzen.
        /// </summary>
        [Test]
        public void AStanzaSplitAcrossReads_IsStillOneFrame()
        {

            var zerleger = new XmlStreamSplitter();

            Assert.That(zerleger.Push(Kopf), Has.Count.EqualTo(1));

            // Mitten im Tag getrennt.
            Assert.That(zerleger.Push("<message from='a@links.exa"), Is.Empty);
            Assert.That(zerleger.Push("mple' to='b@rechts.example'><bo"), Is.Empty);
            Assert.That(zerleger.Push("dy>zwei</body>"), Is.Empty);

            var fertig = zerleger.Push("</message>");

            Assert.Multiple(() =>
            {
                Assert.That(fertig, Has.Count.EqualTo(1));
                Assert.That(fertig[0], Does.Contain("zwei"));
                Assert.That(fertig[0], Does.StartWith("<message"));
                Assert.That(fertig[0], Does.EndWith("</message>"));
            });

        }

        #endregion

        #region CharacterByCharacter_YieldsTheSameFrames()

        /// <summary>
        /// Die schärfste Gegenprobe: jedes einzelne Zeichen ein Lesevorgang.
        /// </summary>
        /// <remarks>
        /// Wer den Zerleger versehentlich auf ganze Lesevorgänge stützt statt
        /// auf mitgeführten Zustand, scheitert hier und sonst fast nirgends.
        /// </remarks>
        [Test]
        public void CharacterByCharacter_YieldsTheSameFrames()
        {

            var strom = Kopf +
                        "<message to='b@rechts.example'><body>drei</body></message>" +
                        "<iq type='get' id='1'><ping xmlns='urn:xmpp:ping'/></iq>" +
                        "</stream:stream>";

            Assert.That(Zeichenweise(strom), Is.EqualTo(Alles(strom)));

        }

        #endregion

        #region SeveralStanzasInOneRead_AreSeparated()

        /// <summary>
        /// Und die Gegenrichtung: mehrere Stanzas in einem einzigen
        /// Lesevorgang.
        /// </summary>
        [Test]
        public void SeveralStanzasInOneRead_AreSeparated()
        {

            var zerleger = new XmlStreamSplitter();
            zerleger.Push(Kopf);

            var rahmen = zerleger.Push("<presence/><presence type='unavailable'/><message/>");

            Assert.That(rahmen, Has.Count.EqualTo(3));

        }

        #endregion

        #region AGreaterThanInsideAnAttribute_DoesNotEndTheTag()

        /// <summary>
        /// Ein <c>&gt;</c> in einem Attributwert ist gültiges XML und beendet
        /// das Tag nicht.
        /// </summary>
        /// <remarks>
        /// Das <b>selbstschliessende</b> Tag ist hier der tragende Fall, und
        /// das ist kein Detail: bei einem gewöhnlichen Element fällt fehlende
        /// Anführungszeichen-Behandlung nicht auf, weil die Elementgrenze am
        /// Ende dieselbe bleibt - das schliessende Tag bringt die Tiefe so
        /// oder so auf null. Erst wenn das <c>/&gt;</c> übersehen wird, zählt
        /// der Zerleger eine Ebene zu viel und liefert den Rahmen nie aus.
        /// Eine erste Fassung dieses Tests prüfte nur den gewöhnlichen Fall
        /// und überlebte die Mutation.
        /// </remarks>
        [Test]
        public void AGreaterThanInsideAnAttribute_DoesNotEndTheTag()
        {

            var zerleger = new XmlStreamSplitter();
            zerleger.Push(Kopf);

            var selbstschliessend = "<presence status='a>b'/>";
            var gewoehnlich       = "<message subject='a &gt; b' id='x>y'><body>vier</body></message>";

            var rahmen = zerleger.Push(selbstschliessend + gewoehnlich);

            Assert.Multiple(() =>
            {
                Assert.That(rahmen,     Has.Count.EqualTo(2));
                Assert.That(rahmen[0],  Is.EqualTo(selbstschliessend));
                Assert.That(rahmen[1],  Is.EqualTo(gewoehnlich));
            });

        }

        #endregion

        #region ATagInsideCData_IsNotAnElement()

        /// <summary>
        /// In CDATA darf alles stehen, auch etwas, das wie ein Tag aussieht.
        /// </summary>
        [Test]
        public void ATagInsideCData_IsNotAnElement()
        {

            var zerleger = new XmlStreamSplitter();
            zerleger.Push(Kopf);

            var stanza  = "<message><body><![CDATA[</message><evil/>]]></body></message>";
            var rahmen  = zerleger.Push(stanza);

            Assert.Multiple(() =>
            {
                Assert.That(rahmen, Has.Count.EqualTo(1));
                Assert.That(rahmen[0], Is.EqualTo(stanza));
            });

        }

        #endregion

        #region NestedElementsOfTheSameName_AreCountedCorrectly()

        /// <summary>
        /// Verschachtelte gleichnamige Elemente - das schliessende Tag des
        /// inneren beendet nicht das äussere.
        /// </summary>
        /// <remarks>
        /// XEP-0280 Carbons und XEP-0297 Forwarding schachteln
        /// <c>&lt;message/&gt;</c> genau so ineinander; das ist kein
        /// konstruierter Fall.
        /// </remarks>
        [Test]
        public void NestedElementsOfTheSameName_AreCountedCorrectly()
        {

            var zerleger = new XmlStreamSplitter();
            zerleger.Push(Kopf);

            var stanza = "<message><sent xmlns='urn:xmpp:carbons:2'><forwarded>" +
                         "<message><body>innen</body></message>" +
                         "</forwarded></sent></message>";

            var rahmen = zerleger.Push(stanza);

            Assert.Multiple(() =>
            {
                Assert.That(rahmen, Has.Count.EqualTo(1));
                Assert.That(rahmen[0], Is.EqualTo(stanza));
            });

        }

        #endregion

        #region TheXmlDeclaration_IsNotMistakenForTheHeader()

        /// <summary>
        /// Manche Server schicken eine XML-Deklaration voraus. Sie ist kein
        /// Element und darf nicht als Stream-Kopf durchgehen.
        /// </summary>
        [Test]
        public void TheXmlDeclaration_IsNotMistakenForTheHeader()
        {

            var rahmen = Alles("<?xml version='1.0'?>" + Kopf + "<presence/>");

            Assert.Multiple(() =>
            {
                Assert.That(rahmen, Has.Count.EqualTo(2));
                Assert.That(rahmen[0], Is.EqualTo(Kopf));
                Assert.That(rahmen[1], Is.EqualTo("<presence/>"));
            });

        }

        #endregion

        #region WhitespaceBetweenStanzas_IsNotAFrame()

        /// <summary>
        /// Zwischen Stanzas steht oft Leerraum - unter anderem als
        /// Keepalive. Er ergibt keinen Rahmen.
        /// </summary>
        [Test]
        public void WhitespaceBetweenStanzas_IsNotAFrame()
        {

            var zerleger = new XmlStreamSplitter();
            zerleger.Push(Kopf);

            Assert.Multiple(() =>
            {
                Assert.That(zerleger.Push("\n  \t "), Is.Empty);
                Assert.That(zerleger.Push("  <presence/>\n"), Has.Count.EqualTo(1));
            });

        }

        #endregion

        #region TheClosingStreamTag_IsItsOwnFrame()

        /// <summary>
        /// Das Stream-Ende muss oben ankommen, sonst merkt niemand, dass die
        /// Gegenstelle ordentlich Schluss gemacht hat.
        /// </summary>
        [Test]
        public void TheClosingStreamTag_IsItsOwnFrame()
        {

            var rahmen = Alles(Kopf + "<presence/></stream:stream>");

            Assert.Multiple(() =>
            {
                Assert.That(rahmen, Has.Count.EqualTo(3));
                Assert.That(rahmen[2], Is.EqualTo("</stream:stream>"));
            });

        }

        #endregion

        #region AnIncompleteStanza_YieldsNothingYet()

        /// <summary>
        /// Halb Empfangenes wird zurückgehalten, nicht halb geliefert.
        /// </summary>
        [Test]
        public void AnIncompleteStanza_YieldsNothingYet()
        {

            var zerleger = new XmlStreamSplitter();
            zerleger.Push(Kopf);

            Assert.That(zerleger.Push("<message><body>unfert"), Is.Empty);

        }

        #endregion

    }

}
