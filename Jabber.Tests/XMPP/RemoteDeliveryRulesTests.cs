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
using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// RFC 6121, Abschnitt 8.5 gilt für <b>jede</b> eingehende Stanza — auch
    /// für die, die über die Servergrenze kommt.
    /// </summary>
    /// <remarks>
    /// Der Abschnitt spricht durchweg von einer „inbound stanza" und
    /// unterscheidet nicht, ob sie von einem Client oder von einem anderen
    /// Server kam. Für den Empfänger ist der Unterschied auch keiner: Es ist
    /// eine Nachricht an sein Konto.
    ///
    /// Dieser Server behandelte die beiden Herkünfte dennoch verschieden. Was
    /// über die Grenze kam, ging unbesehen ins Routing — ohne Offline-Ablage,
    /// ohne Rücksicht auf negative Prioritäten, ohne Unterscheidung nach Art.
    /// Damit lag die Lücke genau im häufigsten Fall: Der Bekannte auf einem
    /// anderen Server ist der Regelfall und nicht die Ausnahme, und wer eine
    /// Offline-Ablage baut, baut sie vor allem für ihn.
    ///
    /// Geprüft wird über zwei echte Server mit
    /// <see cref="DirectServerLinks"/> dazwischen und an jedem ein echter
    /// Client. Das ist wichtiger als es aussieht: Eine Fehlerantwort muss den
    /// Rückweg über die Grenze finden, und ein Mitschnitt am Server bewiese nur,
    /// dass sie abgeschickt wurde.
    /// </remarks>
    [TestFixture]
    public class RemoteDeliveryRulesTests
    {

        #region Data

        private XMPPServer _links   = null!;
        private XMPPServer _rechts  = null!;
        private readonly List<XMPPClient> _clients = [];

        private const String Alice = "alice@links.example";
        private const String Bob   = "bob@rechts.example";

        #endregion

        #region SetUp / TearDown

        [SetUp]
        public void ZweiServer()
        {

            _links   = new XMPPServer("links.example");
            _rechts  = new XMPPServer("rechts.example");

            _links.Start();
            _rechts.Start();

            DirectServerLinks.Connect(_links, _rechts);

        }

        [TearDown]
        public async Task Abraeumen()
        {

            foreach (var client in _clients)
            {
                try { await client.DisposeAsync(); }
                catch { /* im Teardown egal */ }
            }

            _clients.Clear();

            await _links.DisposeAsync();
            await _rechts.DisposeAsync();

        }

        #endregion

        #region Hilfsfunktionen

        /// <summary>
        /// Ein noch nicht verbundener Client an einem der beiden Server.
        /// </summary>
        /// <remarks>
        /// Getrennt vom Verbinden, weil ein Eingangskorb <b>vor</b> der ersten
        /// Presence hängen muss: Eine nachgereichte Nachricht kommt unmittelbar
        /// danach, und ein erst später angemeldeter Empfänger verpasst sie je
        /// nach Zeitverlauf.
        /// </remarks>
        private XMPPClient Erzeuge(XMPPServer  server,
                                   String      localPart,
                                   String?     resource  = null,
                                   Int32?      priority  = null)
        {

            if (server.GetAccount($"{localPart}@{server.Domain}") is null)
                server.AddAccount(localPart);

            var connection = new XMPPConnection($"{localPart}@{server.Domain}",
                                                "pw",
                                                server.Uri)
            {
                KeepaliveEnabled            = false,
                MaxReconnectAttempts        = 0,
                PresencePriority            = priority,
                ServerCertificateValidator  = server.IsOwnCertificate
            };

            if (resource is not null)
                connection.Resource = resource;

            var client = new XMPPClient(connection);
            _clients.Add(client);

            return client;

        }

        private async Task<XMPPClient> VerbindeAsync(XMPPServer  server,
                                                    String      localPart,
                                                    String?     resource  = null,
                                                    Int32?      priority  = null)
        {

            var client = Erzeuge(server, localPart, resource, priority);
            await client.ConnectAsync();

            return client;

        }

        private static async Task WarteAuf(Func<Boolean> bedingung, String was)
        {
            Assert.That(await XMPPServer.WaitUntilAsync(bedingung),
                        Is.True, $"Zeitüberschreitung beim Warten auf: {was}");
        }

        private static async Task WarteGegen(Func<Boolean> bedingung, String was)
        {
            Assert.That(await XMPPServer.WaitUntilAsync(bedingung, TimeSpan.FromSeconds(2)),
                        Is.False, $"Hätte nicht eintreten dürfen: {was}");
        }

        /// <summary>Die Offline-Ablage von Bobs Konto auf dem rechten Server.</summary>
        private IReadOnlyList<OfflineMessage> AblageVonBob
            => _rechts.GetAccount(Bob)!.OfflineMessages;

        #endregion


        #region AMessageToAnAbsentUser_IsStoredOnTheirServer()

        /// <summary>
        /// Der Kern: Bob ist auf seinem Server nicht angemeldet, als Alice von
        /// einem anderen Server schreibt — und liest es beim nächsten Anmelden.
        /// </summary>
        /// <remarks>
        /// Vorher verschwand diese Nachricht: Das Routing fand keine Sitzung und
        /// tat nichts. Alice hielt sie für zugestellt, Bob erfuhr nie, dass es
        /// sie gab. Genau der Fall, für den die Ablage gemacht ist — und der
        /// einzige, in dem sie einem Menschen etwas nützt, denn zwei Konten auf
        /// demselben Server sind die Ausnahme.
        /// </remarks>
        [Test]
        public async Task AMessageToAnAbsentUser_IsStoredOnTheirServer()
        {

            var alice = await VerbindeAsync(_links, "alice");
            _rechts.AddAccount("bob");

            await alice.SendMessageAsync(Bob, "Bis später");

            await WarteAuf(() => AblageVonBob.Count == 1,
                           "die abgelegte Nachricht auf Bobs Server");

            var bob      = Erzeuge(_rechts, "bob");
            var eingang  = new ConcurrentQueue<XMPPMessage>();

            bob.OnMessage += m => eingang.Enqueue(m);

            await bob.ConnectAsync();

            await WarteAuf(() => !eingang.IsEmpty, "die nachgereichte Nachricht");

            eingang.TryDequeue(out var nachricht);

            Assert.Multiple(() =>
            {

                Assert.That(nachricht!.Body,         Is.EqualTo("Bis später"));

                Assert.That(nachricht.FromBareJid,   Is.EqualTo(Alice),
                            "Nachgereicht wird mit dem Absender von der anderen Domain.");

                Assert.That(AblageVonBob,            Is.Empty);

            });

        }

        #endregion

        #region AStoredRemoteMessage_ArrivesInTheClientNamespace()

        /// <summary>
        /// Was über die Grenze kam, trägt <c>jabber:server</c>; was an einen
        /// Client geht, muss <c>jabber:client</c> tragen (RFC 6120,
        /// Abschnitt 4.8.1).
        /// </summary>
        /// <remarks>
        /// Die Ablage ist die Stelle, an der das leicht schiefgeht: Aufbewahrt
        /// wird die Stanza, wie sie hereinkam, und zugestellt wird sie viel
        /// später über einen anderen Stream. Läge der Namensraumwechsel nicht am
        /// Ausgang, sondern am Eingang, käme sie mit dem falschen heraus — und
        /// ein Client, der prüft, verwürfe sie.
        /// </remarks>
        [Test]
        public async Task AStoredRemoteMessage_ArrivesInTheClientNamespace()
        {

            var alice = await VerbindeAsync(_links, "alice");
            _rechts.AddAccount("bob");

            await alice.SendMessageAsync(Bob, "Aus der Ablage");

            await WarteAuf(() => AblageVonBob.Count == 1, "die abgelegte Nachricht");

            Assert.That(AblageVonBob[0].Stanza, Does.Contain("jabber:server"),
                        "Aufbewahrt wird, was über die Grenze kam.");

            var bob   = Erzeuge(_rechts, "bob");
            var rohe  = new ConcurrentQueue<String>();

            bob.Connection.OnRawXml += x =>
            {
                if (x.Contains("Aus der Ablage", StringComparison.Ordinal))
                    rohe.Enqueue(x);
            };

            await bob.ConnectAsync();

            await WarteAuf(() => !rohe.IsEmpty, "die nachgereichte Nachricht auf dem Draht");

            rohe.TryDequeue(out var stanza);

            Assert.Multiple(() =>
            {
                Assert.That(stanza, Does.Contain("jabber:client"));
                Assert.That(stanza, Does.Not.Contain("jabber:server"));
            });

        }

        #endregion

        #region AGroupchatAcrossTheBorder_IsRefusedToTheSender()

        /// <summary>
        /// Abschnitt 8.5.2.1.1 gilt auch über die Grenze: Ein
        /// <c>groupchat</c> an ein Konto wird abgelehnt — und die Ablehnung
        /// findet den Rückweg.
        /// </summary>
        /// <remarks>
        /// Der Rückweg ist der eigentliche Gegenstand dieses Tests. Innerhalb
        /// eines Servers geht eine Fehlerantwort in den Stream des Absenders;
        /// über die Grenze gibt es keinen Stream, sondern nur das <c>from</c> der
        /// eingegangenen Stanza und den Weg wieder hinaus. Beides zu verwechseln
        /// wäre nicht auffällig: Die Ablehnung fände einfach niemanden, und
        /// Alice wartete auf eine Antwort, die es nie gab.
        ///
        /// Dass sie beim <b>Client</b> ankommt und nicht bloss am Server
        /// abgeschickt wurde, ist der Grund für die zwei echten Server in diesem
        /// Aufbau.
        /// </remarks>
        [Test]
        public async Task AGroupchatAcrossTheBorder_IsRefusedToTheSender()
        {

            var alice = await VerbindeAsync(_links,  "alice");
            var bob   = await VerbindeAsync(_rechts, "bob");

            var beiBob   = new ConcurrentQueue<XMPPMessage>();
            var fehler   = new ConcurrentQueue<(String? From, StanzaError Error)>();

            bob.OnMessage                  += m => beiBob.Enqueue(m);
            alice.Connection.OnStanzaError += (from, e) => fehler.Enqueue((from, e));

            await alice.SendRawAsync(
                      $"<message to='{Bob}' type='groupchat' id='ueber-die-grenze'>" +
                      "<body>Gehört in einen Raum</body></message>");

            await WarteAuf(() => !fehler.IsEmpty, "die Ablehnung zurück über die Grenze");

            fehler.TryDequeue(out var abgelehnt);

            Assert.Multiple(() =>
            {

                Assert.That(abgelehnt.Error.Condition, Is.EqualTo("service-unavailable"));

                Assert.That(abgelehnt.From, Is.EqualTo(Bob),
                            "Die Antwort kommt von der Adresse, an die es nicht ging - " +
                            "Alices Frage ist, was aus ihrer Nachricht an Bob geworden ist, " +
                            "und nicht, was Bobs Server von sich aus meint.");

                Assert.That(beiBob, Is.Empty,
                            "Ein groupchat an ein Konto darf keine Resource erreichen.");

            });

        }

        #endregion

        #region WithoutTheStore_TheRemoteSenderIsTold()

        /// <summary>
        /// Der zweite von Abschnitt 8.5.2.2.1 erlaubte Weg, ebenfalls über die
        /// Grenze: keine Ablage, sondern <c>&lt;service-unavailable/&gt;</c>.
        /// </summary>
        /// <remarks>
        /// Die Ablehnung hat hier zwei verschiedene Ursprungsstellen im Server -
        /// die eine für <c>groupchat</c>, die andere für die voll gelaufene oder
        /// abgeschaltete Ablage. Beide brauchen den Rückweg, und nur eine davon
        /// zu prüfen liesse die andere im Dunkeln: Genau in dem Fall, für den
        /// dieser Server bis vor kurzem gar keine Antwort hatte, käme wieder
        /// keine.
        /// </remarks>
        [Test]
        public async Task WithoutTheStore_TheRemoteSenderIsTold()
        {

            _rechts.StoreOfflineMessages = false;

            var alice = await VerbindeAsync(_links, "alice");
            _rechts.AddAccount("bob");

            var fehler = new ConcurrentQueue<StanzaError>();
            alice.Connection.OnStanzaError += (from, e) => fehler.Enqueue(e);

            await alice.SendMessageAsync(Bob, "Kommt nicht an");

            await WarteAuf(() => !fehler.IsEmpty, "die Ablehnung zurück über die Grenze");

            fehler.TryDequeue(out var abgelehnt);

            Assert.Multiple(() =>
            {
                Assert.That(abgelehnt!.Condition, Is.EqualTo("service-unavailable"));
                Assert.That(AblageVonBob,         Is.Empty);
            });

        }

        #endregion

        #region ARefusalAcrossTheBorder_IsNotAnsweredWithARefusal()

        /// <summary>
        /// RFC 6120, Abschnitt 8.3.1: Auf eine Fehler-Stanza folgt nie ein
        /// Fehler.
        /// </summary>
        /// <remarks>
        /// Zwischen zwei Servern ist das keine Formalie, sondern der Unterschied
        /// zwischen einer verlorenen Nachricht und zwei Servern, die sich
        /// gegenseitig Meldungen zuschieben, bis einer aufgibt. Die Gefahr ist
        /// hier neu: Solange nur Clients diesen Weg nahmen, endete jede Antwort
        /// in einem Stream. Jetzt geht sie wieder hinaus, und der Empfänger ist
        /// eine Maschine, die ihrerseits antworten könnte.
        ///
        /// Geprüft wird beides in einem: der Fehler an das Konto (der
        /// stillschweigend übergangen werden muss) und der an eine Resource, die
        /// es nicht gibt.
        /// </remarks>
        [Test]
        public async Task ARefusalAcrossTheBorder_IsNotAnsweredWithARefusal()
        {

            var alice = await VerbindeAsync(_links, "alice");
            _rechts.AddAccount("bob");

            var beiAlice = new ConcurrentQueue<StanzaError>();
            alice.Connection.OnStanzaError += (from, e) => beiAlice.Enqueue(e);

            const String rumpf = "<error type='cancel'>" +
                                 "<service-unavailable xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/>" +
                                 "</error>";

            await alice.SendRawAsync($"<message to='{Bob}' type='error' id='an-das-konto'>{rumpf}</message>");
            await alice.SendRawAsync($"<message to='{Bob}/weg' type='error' id='an-die-resource'>{rumpf}</message>");

            await WarteGegen(() => !beiAlice.IsEmpty,
                             "einen Fehler als Antwort auf einen Fehler");

            Assert.That(AblageVonBob, Is.Empty,
                        "Eine Fehler-Stanza gehört auch nicht in die Ablage.");

        }

        #endregion

        #region ANegativePriorityAcrossTheBorder_GetsNothingFromTheAccount()

        /// <summary>
        /// Die negative Priorität wirkt auch gegen Nachrichten von einem anderen
        /// Server — und die Nachricht ist damit nicht verloren, sondern liegt.
        /// </summary>
        /// <remarks>
        /// Vorher war die Priorität für diesen Weg wirkungslos: Das Routing
        /// stellte an jede Sitzung zu, die es fand. Ein Zweitgerät, das sich
        /// ausdrücklich aus dem Verkehr an das Konto heraushält, bekam die
        /// Nachricht trotzdem — und weil sie damit als zugestellt galt, sah der
        /// Mensch sie an dem Gerät, an dem er gerade nicht sass.
        ///
        /// Die zweite Hälfte gehört dazu: Gerichtet ansprechbar bleibt das Gerät.
        /// Ohne sie wäre eine negative Priorität ein Abmelden, und das ist sie
        /// gerade nicht.
        /// </remarks>
        [Test]
        public async Task ANegativePriorityAcrossTheBorder_GetsNothingFromTheAccount()
        {

            var alice        = await VerbindeAsync(_links, "alice");
            var zweitgeraet  = Erzeuge(_rechts, "bob", "Zweitgerät", priority: -1);
            var eingang      = new ConcurrentQueue<XMPPMessage>();

            zweitgeraet.OnMessage += m => eingang.Enqueue(m);

            await zweitgeraet.ConnectAsync();

            await WarteAuf(() => _rechts.SessionOf(zweitgeraet.FullJid!)?.PresencePriority == -1,
                           "die negative Priorität auf Bobs Server");

            await alice.SendRawAsync(
                      $"<message to='{Bob}' type='chat' id='an-das-konto'>" +
                      "<body>An das Konto</body></message>");

            await WarteAuf(() => AblageVonBob.Count == 1,
                           "die Ablage statt der Zustellung");

            // Gerichtet an dieselbe Resource - das muss ankommen.
            await alice.SendRawAsync(
                      $"<message to='{zweitgeraet.FullJid}' type='chat' id='an-die-resource'>" +
                      "<body>An die Resource</body></message>");

            await WarteAuf(() => eingang.Any(m => m.MessageId == "an-die-resource"),
                           "die gerichtete Nachricht");

            Assert.That(eingang.Any(m => m.MessageId == "an-das-konto"), Is.False,
                        "Was an das Konto ging, darf eine negative Priorität nicht erreichen.");

        }

        #endregion

        #region AChatToAVanishedResource_IsHandledLikeTheAccount()

        /// <summary>
        /// Abschnitt 8.5.3.2.1 über die Grenze: Ein <c>chat</c> an eine Resource,
        /// die es nicht gibt, wird behandelt, als wäre er an das Konto gegangen.
        /// </summary>
        /// <remarks>
        /// Über die Grenze ist dieser Fall häufiger als daheim, und zwar aus
        /// einem einfachen Grund: Die Full-JID des Gegenübers hat der Client aus
        /// einer Nachricht, die durchs Netz kam, und zwischen ihr und seiner
        /// Antwort liegt mehr Zeit. Hat der andere in der Zwischenzeit das Gerät
        /// gewechselt, ist die Resource weg — und der Absender meinte nie sie,
        /// sondern seinen Gegenüber.
        /// </remarks>
        [Test]
        public async Task AChatToAVanishedResource_IsHandledLikeTheAccount()
        {

            var alice = await VerbindeAsync(_links, "alice");
            _rechts.AddAccount("bob");

            await alice.SendRawAsync(
                      $"<message to='{Bob}/gibtsnichtmehr' type='chat' id='abgelegt'>" +
                      "<body>Bist du weg?</body></message>");

            await WarteAuf(() => AblageVonBob.Count == 1,
                           "die abgelegte Nachricht an die verschwundene Resource");

            var bob      = Erzeuge(_rechts, "bob", "Neu");
            var eingang  = new ConcurrentQueue<XMPPMessage>();

            bob.OnMessage += m => eingang.Enqueue(m);

            await bob.ConnectAsync();

            await WarteAuf(() => eingang.Any(m => m.MessageId == "abgelegt"),
                           "die nachgereichte Nachricht");

            // Und nun ist Bob da, nur unter einem anderen Namen.
            await alice.SendRawAsync(
                      $"<message to='{Bob}/gibtsnichtmehr' type='chat' id='zugestellt'>" +
                      "<body>Doch nicht</body></message>");

            await WarteAuf(() => eingang.Any(m => m.MessageId == "zugestellt"),
                           "die Zustellung an die erreichbare Resource");

            Assert.That(AblageVonBob, Is.Empty,
                        "Solange eine Resource erreichbar ist, wird nichts abgelegt.");

        }

        #endregion

        #region AHeadlineToAnAbsentUser_IsNotStored()

        /// <summary>
        /// Die Gegenprobe: <c>headline</c> wird nicht abgelegt, auch nicht von
        /// einem anderen Server.
        /// </summary>
        /// <remarks>
        /// Ohne sie bestünde die Sammlung auch dann, wenn über die Grenze
        /// schlicht alles abgelegt würde, was nicht zustellbar war — und eine
        /// Meldung von gestern beim Anmelden nachgereicht zu bekommen ist
        /// schlechter, als sie zu verpassen: Sie sieht aus wie die von heute.
        /// </remarks>
        [Test]
        public async Task AHeadlineToAnAbsentUser_IsNotStored()
        {

            var alice = await VerbindeAsync(_links, "alice");
            _rechts.AddAccount("bob");

            await alice.SendRawAsync(
                      $"<message to='{Bob}' type='headline' id='meldung'>" +
                      "<body>Kurs gefallen</body></message>");

            // Danach eine, die abgelegt werden muss - sie belegt, dass die
            // Ablage lief, während die Meldung durchfiel.
            await alice.SendMessageAsync(Bob, "Und das bleibt liegen");

            await WarteAuf(() => AblageVonBob.Count == 1, "die abgelegte Nachricht");

            Assert.That(AblageVonBob[0].Stanza, Does.Contain("Und das bleibt liegen"));

        }

        #endregion

        #region AnIqAcrossTheBorder_ToAnAccount_IsAnsweredOnce()

        /// <summary>
        /// Abschnitt 8.5.2.1.3 gilt auch über die Grenze: Eine Anfrage an ein
        /// Konto wird vom Server des Empfängers beantwortet und an keine
        /// Resource verteilt.
        /// </summary>
        /// <remarks>
        /// Über die Grenze ist der Schaden grösser als daheim. Ein fremder
        /// Server, der eine Anfrage an alle Resourcen verteilt, schickt dem
        /// Fragenden mehrere Antworten auf eine <c>id</c> — und der hat keine
        /// Möglichkeit, das der Gegenstelle anzulasten: Für ihn sieht es aus, als
        /// hätte sein eigener Client die Zählung verloren.
        /// </remarks>
        [Test]
        public async Task AnIqAcrossTheBorder_ToAnAccount_IsAnsweredOnce()
        {

            var alice = await VerbindeAsync(_links, "alice");

            var handy    = Erzeuge(_rechts, "bob", "Handy");
            var rechner  = Erzeuge(_rechts, "bob", "Rechner");

            var amHandy    = new ConcurrentQueue<String>();
            var amRechner  = new ConcurrentQueue<String>();

            handy.Connection.OnRawXml += x =>
            {
                if (x.StartsWith("<<<", StringComparison.Ordinal) &&
                    x.Contains("urn:xmpp:ping", StringComparison.Ordinal))
                {
                    amHandy.Enqueue(x);
                }
            };

            rechner.Connection.OnRawXml += x =>
            {
                if (x.StartsWith("<<<", StringComparison.Ordinal) &&
                    x.Contains("urn:xmpp:ping", StringComparison.Ordinal))
                {
                    amRechner.Enqueue(x);
                }
            };

            await handy.ConnectAsync();
            await rechner.ConnectAsync();

            var antworten = new ConcurrentQueue<String>();
            var fehler    = new ConcurrentQueue<StanzaError>();

            alice.Connection.OnStanzaError += (from, e) => fehler.Enqueue(e);
            alice.Connection.OnRawXml      += x =>
            {
                if (x.StartsWith("<<<", StringComparison.Ordinal) &&
                    x.Contains("<iq",              StringComparison.Ordinal) &&
                    x.Contains("id='ueber-die-grenze'", StringComparison.Ordinal))
                {
                    antworten.Enqueue(x);
                }
            };

            await alice.SendRawAsync(
                      $"<iq type='get' id='ueber-die-grenze' to='{Bob}'>" +
                      "<ping xmlns='urn:xmpp:ping'/></iq>");

            await WarteAuf(() => !fehler.IsEmpty, "die Antwort von Bobs Server");

            // Den Resourcen Zeit geben, die Anfrage doch zu bekommen.
            await Task.Delay(TimeSpan.FromSeconds(1));

            fehler.TryDequeue(out var abgelehnt);

            Assert.Multiple(() =>
            {

                Assert.That(abgelehnt!.Condition, Is.EqualTo("service-unavailable"));

                Assert.That(antworten, Has.Count.EqualTo(1),
                            "Genau eine Antwort auf eine id.");

                Assert.That(amHandy,   Is.Empty, "Die Anfrage darf keine Resource erreichen.");
                Assert.That(amRechner, Is.Empty, "Auch nicht die zweite.");

            });

        }

        #endregion

        #region AnIqAcrossTheBorder_ToAResource_IsDelivered()

        /// <summary>
        /// Die Gegenprobe über die Grenze: An eine passende Resource gerichtet
        /// kommt die Anfrage an, und die Antwort findet zurück.
        /// </summary>
        /// <remarks>
        /// Das ist der ganze Sinn von IQ zwischen zwei Servern — eine
        /// Versionsabfrage, ein Ping, eine Dateiübertragung gehen an eine
        /// Full-JID. Ohne diese Gegenprobe bestünde die Sammlung auch dann, wenn
        /// über die Grenze jede Anfrage abgewiesen würde.
        /// </remarks>
        [Test]
        public async Task AnIqAcrossTheBorder_ToAResource_IsDelivered()
        {

            var alice = await VerbindeAsync(_links,  "alice");
            var bob   = await VerbindeAsync(_rechts, "bob");

            var antworten = new ConcurrentQueue<String>();

            alice.Connection.OnRawXml += x =>
            {
                if (x.StartsWith("<<<", StringComparison.Ordinal) &&
                    x.Contains("type='result'",         StringComparison.Ordinal) &&
                    x.Contains("id='an-die-resource'",  StringComparison.Ordinal))
                {
                    antworten.Enqueue(x);
                }
            };

            await alice.SendRawAsync(
                      $"<iq type='get' id='an-die-resource' to='{bob.FullJid}'>" +
                      "<ping xmlns='urn:xmpp:ping'/></iq>");

            await WarteAuf(() => !antworten.IsEmpty, "Bobs Antwort zurück über die Grenze");

            Assert.Pass();

        }

        #endregion

        #region AnIqToTheServersOwnAddress_IsNotClaimedByTheUserPath()

        /// <summary>
        /// Der Zustellweg für Nutzer fasst eine Anfrage an die Serveradresse
        /// nicht an.
        /// </summary>
        /// <remarks>
        /// Abschnitt 8.5.2 handelt von einer Adresse „of the form
        /// <c>&lt;localpart@domainpart&gt;</c>". Eine Anfrage an die Domain
        /// selbst richtet sich an den Server und nicht an einen Nutzer.
        ///
        /// Dass sie hier <b>unbeantwortet</b> bleibt, ist eine offene Stelle und
        /// keine Absicht: Der Server beantwortet an seiner eigenen Adresse
        /// disco#info und Pings, aber nur für hiesige Clients — von der
        /// Gegenstelle kommend erreichen sie diese Antworten noch nicht. Das
        /// steht unter „Später".
        ///
        /// Wogegen dieser Test schützt, ist die naheliegende Verwechslung: den
        /// Nutzer-Zustellweg auch für die Serveradresse zu nehmen. Er antwortete
        /// dann mit <c>&lt;service-unavailable/&gt;</c> — und das wäre schlechter
        /// als Schweigen. Schweigen ist eine Lücke; ein Fehler ist eine Aussage,
        /// und diese Aussage wäre falsch. Eine Gegenstelle, die sie glaubt, fragt
        /// nicht wieder.
        /// </remarks>
        [Test]
        public async Task AnIqToTheServersOwnAddress_IsNotClaimedByTheUserPath()
        {

            var alice = await VerbindeAsync(_links, "alice");

            var antworten = new ConcurrentQueue<String>();

            alice.Connection.OnRawXml += x =>
            {
                if (x.StartsWith("<<<", StringComparison.Ordinal) &&
                    x.Contains("an-den-server", StringComparison.Ordinal))
                {
                    antworten.Enqueue(x);
                }
            };

            await alice.SendRawAsync(
                      $"<iq type='get' id='an-den-server' to='{_rechts.Domain}'>" +
                      "<ping xmlns='urn:xmpp:ping'/></iq>");

            await WarteGegen(() => !antworten.IsEmpty,
                             "eine Ablehnung des Nutzer-Zustellwegs für die Serveradresse");

        }

        #endregion

        #region PresenceAcrossTheBorder_StillTakesTheDirectPath()

        /// <summary>
        /// Nur Nachrichten nehmen den neuen Weg. Presence geht weiterhin
        /// unmittelbar an die Resourcen.
        /// </summary>
        /// <remarks>
        /// Die Weiche fragt nach dem Element und nicht nach der Herkunft. Fragte
        /// sie falsch, liefe Presence durch die Zustellregeln für Nachrichten —
        /// und die kennen kein <c>&lt;presence/&gt;</c>: Es hat kein <c>type</c>,
        /// das sie deuten könnten, gälte damit als <c>normal</c> und landete in
        /// der Ablage. Beim nächsten Anmelden käme es als Anwesenheit von
        /// vorgestern heraus.
        ///
        /// Die erste Hälfte prüft mit <b>abwesendem</b> Bob, und das ist der
        /// Punkt: Solange er verbunden ist, kommt seine Presence auf beiden Wegen
        /// an — die Nachrichtenstrecke stellt sie einer erreichbaren Resource
        /// genauso zu. Sichtbar wird der falsche Weg erst dort, wo die
        /// Zustellregeln etwas anderes tun als das Routing, und das ist die
        /// Ablage.
        /// </remarks>
        [Test]
        public async Task PresenceAcrossTheBorder_StillTakesTheDirectPath()
        {

            var alice = Erzeuge(_links, "alice");
            _rechts.AddAccount("bob");

            _links.GetAccount(Alice)!.SetRosterEntry(new RosterEntry(Bob,   null, "both"));
            _rechts.GetAccount(Bob)!.SetRosterEntry(new RosterEntry(Alice, null, "both"));

            // Bob ist nicht da. Alices Presence geht trotzdem über die Grenze -
            // sein Server hat nur nichts, dem er sie geben könnte.
            await alice.ConnectAsync();
            await alice.SetPresenceAsync("away", "Mittagspause");

            await WarteGegen(() => AblageVonBob.Count > 0,
                             "Presence in der Offline-Ablage");

            // Und die Gegenprobe: Sie kommt an, wenn jemand da ist.
            var bob        = Erzeuge(_rechts, "bob");
            var presenzen  = new ConcurrentQueue<String>();

            bob.Connection.OnPresence += (from, show) => presenzen.Enqueue(from);

            await bob.ConnectAsync();
            await alice.SetPresenceAsync("dnd", "Bitte nicht stören");

            await WarteAuf(() => presenzen.Any(f => f.StartsWith(Alice, StringComparison.Ordinal)),
                           "die Presence über die Grenze");

        }

        #endregion

    }

}
