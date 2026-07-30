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

using System.Collections.Concurrent;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// RFC 6120, Abschnitt 8.2.3, Regel 2: Das <c>type</c>-Attribut einer
    /// IQ-Stanza ist zwingend und muss <c>get</c>, <c>set</c>, <c>result</c>
    /// oder <c>error</c> sein — andernfalls antwortet „the recipient or an
    /// intermediate router" mit <c>&lt;bad-request/&gt;</c>.
    /// </summary>
    /// <remarks>
    /// Der halbe Satz „or an intermediate router" ist der eigentliche Inhalt
    /// dieser Regel. Bei jeder anderen Stanza darf ein Server durchreichen und
    /// den Empfänger urteilen lassen; hier nicht. Der Grund liegt in der Natur
    /// von IQ: Ein Frage-Antwort-Paar hängt an <c>type</c> und <c>id</c>, und
    /// wer keinen der vier Werte trägt, ist weder Frage noch Antwort. Ein
    /// Server, der so etwas weiterreicht, verschiebt das Problem nur — und wenn
    /// die Gegenstelle es ebenso hält, wandert eine Stanza durch das Netz, die
    /// niemand beantworten kann und die der Absender nie zurückbekommt.
    ///
    /// Dieser Server reichte sie durch, und zwar auf dem denkbar ungünstigsten
    /// Weg: Der Zustellweg behandelte alles ausser <c>result</c> und
    /// <c>error</c> als <b>Anfrage</b>. Ein <c>&lt;iq type='vielleicht'&gt;</c>
    /// wurde also einem Empfänger zugestellt, als hätte er etwas zu beantworten.
    ///
    /// Geprüft wird beides: dass die vier bekannten Werte weiterhin ankommen,
    /// und dass der fünfte es nicht tut. Nur die erste Hälfte prüfen hiesse,
    /// eine Sperre gegen alles nicht zu bemerken.
    /// </remarks>
    [TestFixture]
    public class IqTypeRuleTests : AXMPPTests
    {

        #region Hilfsfunktionen

        private String Bob => $"bob@{Server.Domain}";

        /// <summary>Sammelt die Stanza-Fehler eines Clients.</summary>
        private static ConcurrentQueue<StanzaError> Fehlerkorb(XMPPClient client)
        {

            var korb = new ConcurrentQueue<StanzaError>();
            client.Connection.OnStanzaError += (from, e) => korb.Enqueue(e);

            return korb;

        }

        /// <summary>Sammelt die rohen eingehenden Stanzas mit dieser Id.</summary>
        private static ConcurrentQueue<String> Eingangskorb(XMPPClient client, String id)
        {

            var korb = new ConcurrentQueue<String>();

            client.Connection.OnRawXml += x =>
            {
                if (x.StartsWith("<<<",              StringComparison.Ordinal) &&
                    x.Contains($"id='{id}'",         StringComparison.Ordinal))
                {
                    korb.Enqueue(x);
                }
            };

            return korb;

        }

        /// <summary>
        /// Eine IQ-Stanza mit frei wählbarem Typ — auch mit gar keinem.
        /// </summary>
        private static String Stanza(String? type, String? to, String id)
            => "<iq" +
               (type is not null ? $" type='{type}'" : "") +
               $" id='{id}'" +
               (to is not null ? $" to='{to}'" : "") +
               "><ping xmlns='urn:xmpp:ping'/></iq>";

        #endregion


        #region AnIqWithoutAType_IsRefused()

        /// <summary>
        /// Das fehlende Attribut: Der Server antwortet selbst, statt zu
        /// schweigen oder zuzustellen.
        /// </summary>
        [Test]
        public async Task AnIqWithoutAType_IsRefused()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            var fehler = Fehlerkorb(alice);

            await alice.SendRawAsync(Stanza(null, Bob, "ohne-typ"));

            await WaitFor(() => !fehler.IsEmpty, "die Ablehnung des Servers");

            fehler.TryDequeue(out var abgelehnt);

            Assert.That(abgelehnt!.Condition, Is.EqualTo("bad-request"));

        }

        #endregion

        #region AnIqWithAnUnknownType_IsRefused()

        /// <summary>
        /// Und derselbe Fehler mit einem Attribut, das dasteht und nichts
        /// bedeutet.
        /// </summary>
        [Test]
        public async Task AnIqWithAnUnknownType_IsRefused()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            var fehler = Fehlerkorb(alice);

            await alice.SendRawAsync(Stanza("vielleicht", Bob, "falscher-typ"));

            await WaitFor(() => !fehler.IsEmpty, "die Ablehnung des Servers");

            fehler.TryDequeue(out var abgelehnt);

            Assert.That(abgelehnt!.Condition, Is.EqualTo("bad-request"));

        }

        #endregion

        #region AnIqToTheServerItselfWithoutAType_IsRefused()

        /// <summary>
        /// Auch ohne <c>to</c>, also an den eigenen Server gerichtet.
        /// </summary>
        /// <remarks>
        /// Der Test, der die Stelle der Prüfung festhält. Eine Prüfung im
        /// Zustellweg — dort, wo eine Anfrage an eine andere Adresse
        /// weitergereicht wird — bestünde die beiden Tests darüber und liesse
        /// genau diesen Fall durch: Was an den Server selbst geht, kommt dort
        /// nie vorbei und fiele stillschweigend hinten heraus. Vorher tat es
        /// das auch.
        /// </remarks>
        [Test]
        public async Task AnIqToTheServerItselfWithoutAType_IsRefused()
        {

            var alice = await ConnectClientAsync("alice");

            var fehler = Fehlerkorb(alice);

            await alice.SendRawAsync(Stanza(null, null, "an-den-server"));

            await WaitFor(() => !fehler.IsEmpty, "die Ablehnung des Servers");

            fehler.TryDequeue(out var abgelehnt);

            Assert.That(abgelehnt!.Condition, Is.EqualTo("bad-request"));

        }

        #endregion

        #region TheRefusalKeepsTheIdAndAsksToModify()

        /// <summary>
        /// Die Form der Ablehnung: dieselbe <c>id</c>, Fehlerart
        /// <c>modify</c>, Absender der Server.
        /// </summary>
        /// <remarks>
        /// Die <c>id</c> hält das Paar zusammen (Regel 3) — ohne sie liegt beim
        /// Absender eine Ablehnung, die zu keiner seiner offenen Fragen gehört.
        ///
        /// <c>modify</c> und nicht <c>cancel</c>, weil Abschnitt 8.3.3.1 es für
        /// <c>&lt;bad-request/&gt;</c> so vorsieht, und das ist keine
        /// Förmlichkeit: Die Art sagt dem Absender, ob es sich lohnt, es noch
        /// einmal zu versuchen. Hier lohnt es sich — er muss nur das Attribut
        /// richtig setzen.
        ///
        /// Und der Absender ist dieser Server und nicht der gemeinte Empfänger.
        /// Der Unterschied zu <c>&lt;service-unavailable/&gt;</c>, das im Namen
        /// des Empfängers antwortet, ist inhaltlich: Dort hat der Server für
        /// jemanden geantwortet, hier hat er die Stanza gar nicht erst
        /// angenommen. Ein Empfänger als Absender behauptete, jemand habe
        /// hineingesehen.
        /// </remarks>
        [Test]
        public async Task TheRefusalKeepsTheIdAndAsksToModify()
        {

            var alice = await ConnectClientAsync("alice");
            Server.AddAccount("bob");

            var eingang = Eingangskorb(alice, "mit-form");

            await alice.SendRawAsync(Stanza("vielleicht", Bob, "mit-form"));

            await WaitFor(() => !eingang.IsEmpty, "die Ablehnung auf dem Draht");

            eingang.TryDequeue(out var stanza);

            Assert.Multiple(() =>
            {

                Assert.That(stanza, Does.Contain("type='error'"));
                Assert.That(stanza, Does.Contain("id='mit-form'"));
                Assert.That(stanza, Does.Contain("<error type='modify'"));
                Assert.That(stanza, Does.Contain("<bad-request "));
                Assert.That(stanza, Does.Contain($"from='{Server.Domain}'"));

            });

        }

        #endregion

        #region TheRefusalComesEvenWithoutAnId()

        /// <summary>
        /// Ohne <c>id</c> wird die Ablehnung trotzdem geschickt — und trägt
        /// dann keine.
        /// </summary>
        /// <remarks>
        /// Regel 2 stellt die Ablehnung unter keinen Vorbehalt, und der Grund
        /// trägt: Wo eine unbeantwortete Anfrage den Absender nur warten lässt,
        /// sagt diese Antwort etwas über die Stanza selbst — dass ihre Form
        /// nicht stimmt. Das kann er auch dann brauchen, wenn er sie keiner
        /// offenen Frage zuordnen kann; zumal die fehlende <c>id</c> nach
        /// Regel 1 selbst dazugehört.
        ///
        /// Ein leeres <c>id=''</c> wäre der schlechteste Ausgang: Es gehört zu
        /// keiner Frage und sieht aus, als gehörte es zu einer.
        /// </remarks>
        [Test]
        public async Task TheRefusalComesEvenWithoutAnId()
        {

            var alice = await ConnectClientAsync("alice");

            var eingang = new ConcurrentQueue<String>();

            alice.Connection.OnRawXml += x =>
            {
                if (x.StartsWith("<<<",         StringComparison.Ordinal) &&
                    x.Contains("bad-request",   StringComparison.Ordinal))
                {
                    eingang.Enqueue(x);
                }
            };

            await alice.SendRawAsync("<iq><ping xmlns='urn:xmpp:ping'/></iq>");

            await WaitFor(() => !eingang.IsEmpty, "die Ablehnung ohne id");

            eingang.TryDequeue(out var stanza);

            Assert.That(stanza, Does.Not.Contain("id="),
                        "Was keine id hatte, bekommt auch keine leere zurück.");

        }

        #endregion

        #region TheFourKnownTypes_ReachTheResource()

        /// <summary>
        /// Die Gegenprobe: Alle vier vorgesehenen Werte kommen weiterhin an.
        /// </summary>
        /// <remarks>
        /// An die Full-JID und mit beidseitiger Berechtigung, weil dann alle
        /// vier denselben Weg nehmen: <c>get</c> und <c>set</c> über die
        /// Presence-Prüfung aus Abschnitt 8.5.3.1, <c>result</c> und
        /// <c>error</c> über die Zuordnung zur fragenden Resource. Ein
        /// Unterschied im Ergebnis käme damit nur vom Typ selbst.
        /// </remarks>
        [Test]
        [TestCase("get")]
        [TestCase("set")]
        [TestCase("result")]
        [TestCase("error")]
        public async Task TheFourKnownTypes_ReachTheResource(String type)
        {

            MakeContacts("alice", "bob");

            var alice  = await ConnectClientAsync("alice");
            var bob    = await ConnectClientAsync("bob");

            var beiBob = Eingangskorb(bob, $"typ-{type}");

            await alice.SendRawAsync(Stanza(type, bob.FullJid, $"typ-{type}"));

            await WaitFor(() => !beiBob.IsEmpty, $"die Zustellung eines iq '{type}'");

        }

        #endregion

        #region AnUnknownType_ReachesNoResource()

        /// <summary>
        /// Und derselbe Aufbau mit einem fünften Wert: Er erreicht die Resource
        /// nicht.
        /// </summary>
        /// <remarks>
        /// Der Kern des Ganzen. Vorher wurde diese Stanza zugestellt, und zwar
        /// als Anfrage — der Zustellweg fragte nur, ob der Typ <c>result</c>
        /// oder <c>error</c> ist, und behandelte alles übrige als
        /// beantwortungspflichtig. Bob bekam damit etwas vorgelegt, worauf er
        /// nach Regel 3 antworten müsste und worauf keine Antwort passt.
        ///
        /// Beide Hälften gehören in einen Test: „kommt nicht an" allein wäre
        /// auch erfüllt, wenn die Stanza spurlos verschwände, und das wäre
        /// wieder Schweigen statt einer Antwort.
        /// </remarks>
        [Test]
        public async Task AnUnknownType_ReachesNoResource()
        {

            MakeContacts("alice", "bob");

            var alice  = await ConnectClientAsync("alice");
            var bob    = await ConnectClientAsync("bob");

            var beiBob = Eingangskorb(bob, "typ-vielleicht");
            var fehler = Fehlerkorb(alice);

            await alice.SendRawAsync(Stanza("vielleicht", bob.FullJid, "typ-vielleicht"));

            await WaitFor(() => !fehler.IsEmpty, "die Ablehnung des Servers");

            // Und Bob Zeit geben, sie doch noch zu bekommen.
            await WaitAgainst(() => !beiBob.IsEmpty, "die Zustellung an Bob");

            fehler.TryDequeue(out var abgelehnt);

            Assert.That(abgelehnt!.Condition, Is.EqualTo("bad-request"));

        }

        #endregion

    }

}
