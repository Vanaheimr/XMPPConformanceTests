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
    /// OMEMO von einem Ende zum anderen: zwei echte Clients, ein echter
    /// Server, eine verschlüsselte Nachricht.
    /// </summary>
    /// <remarks>
    /// <b>Der Test, auf den die sieben Etappen hinauslaufen.</b> Er benutzt
    /// nichts von innen: Alice schaltet OMEMO ein, schreibt, Bob liest.
    /// Dazwischen liegen Schlüsselerzeugung, PEP-Veröffentlichung,
    /// Bundle-Abruf, X3DH, Ratchet, Protobuf, SCE und der Speicher - und
    /// keines davon wird hier einzeln angefasst.
    ///
    /// Geprüft wird zusätzlich, was <b>nicht</b> auf der Leitung steht: Der
    /// Klartext darf in keiner Stanza vorkommen, die der Server gesehen hat.
    /// Ohne diese Prüfung bestünde der Test auch dann, wenn die Nachricht
    /// nebenher unverschlüsselt mitginge.
    /// </remarks>
    [TestFixture]
    public class OmemoEndToEndTests : AXMPPTests
    {

        #region AMessage_TravelsEncrypted()

        /// <summary>
        /// Alice schreibt verschlüsselt, Bob liest - und der Server sieht den
        /// Klartext nie.
        /// </summary>
        [Test]
        public async Task AMessage_TravelsEncrypted()
        {

            MakeContacts("alice", "bob");

            var alice = await ConnectClientAsync("alice", createAccount: false);
            var bob   = await ConnectClientAsync("bob",   createAccount: false);

            Assert.Multiple(() =>
            {
                Assert.That(alice.EnableOmemoAsync().GetAwaiter().GetResult(), Is.True,
                            "Alice konnte OMEMO nicht einschalten.");
                Assert.That(bob.EnableOmemoAsync().GetAwaiter().GetResult(), Is.True,
                            "Bob konnte OMEMO nicht einschalten.");
            });

            XMPPMessage?    empfangen  = null;
            OmemoDecrypted? auskunft   = null;

            bob.OnEncryptedMessage += (nachricht, omemo) =>
            {
                empfangen = nachricht;
                auskunft  = omemo;
            };

            const String geheim = "Treffen wir uns um acht?";

            var uebersprungen = await alice.SendEncryptedMessageAsync($"bob@{Server.Domain}", geheim);

            Assert.That(uebersprungen, Is.Empty,
                        "Nicht alle Geräte konnten mitlesen: " +
                        String.Join(", ", uebersprungen.Select(u => $"{u.Jid}/{u.DeviceId}: {u.Reason}")));

            await WaitFor(() => empfangen is not null, "die entschlüsselte Nachricht bei Bob");

            Assert.Multiple(() =>
            {

                Assert.That(empfangen!.Body, Is.EqualTo(geheim));

                Assert.That(auskunft!.SenderDeviceId, Is.EqualTo(alice.Omemo!.Identity.DeviceId),
                            "Die Nachricht wird dem falschen Gerät zugeschrieben.");

                Assert.That(auskunft.IdentityCheck, Is.EqualTo(OmemoIdentityCheck.New),
                            "Alices Gerät war Bob angeblich schon bekannt.");

                // Und jetzt der Punkt, ohne den der Test nichts wert wäre:
                // Der Klartext darf auf der Leitung nirgends stehen.
                var alleStanzas = Server.Sessions.SelectMany(s => s.Received.Concat(s.Sent)).ToList();

                Assert.That(alleStanzas.Any(f => f.Contains(geheim, StringComparison.Ordinal)),
                            Is.False,
                            "Der Klartext steht in einer Stanza, die der Server gesehen hat.");

                // Zur Gegenprobe: Der Geheimtext ist dort sehr wohl.
                Assert.That(alleStanzas.Any(f => f.Contains("urn:xmpp:omemo:2", StringComparison.Ordinal)),
                            Is.True,
                            "Es ging überhaupt keine OMEMO-Stanza über die Leitung - " +
                            "dann prüft der Test etwas anderes.");

            });

        }

        #endregion

        #region TwoMessagesInARow_BothArrive()

        /// <summary>
        /// Zwei Nachrichten hintereinander, ohne Antwort dazwischen.
        /// </summary>
        /// <remarks>
        /// <b>Der Test, der ein fehlendes Ablegen der Sitzung findet.</b> In
        /// einem Wechselgespräch fällt es nicht auf: Das Entschlüsseln der
        /// Antwort legt die Sitzung ohnehin ab, und die nächste Nachricht
        /// findet einen aktuellen Stand vor. Erst zwei Nachrichten in Folge
        /// zeigen, ob das <i>Senden</i> selbst den Fortschritt behält - sonst
        /// stünde die zweite auf derselben Stelle der Kette wie die erste, und
        /// der Empfänger hielte sie für eine Wiederholung.
        ///
        /// Genau daran ist eine Mutation vorbeigekommen, bis es diesen Test
        /// gab.
        /// </remarks>
        [Test]
        public async Task TwoMessagesInARow_BothArrive()
        {

            MakeContacts("alice", "bob");

            var alice = await ConnectClientAsync("alice", createAccount: false);
            var bob   = await ConnectClientAsync("bob",   createAccount: false);

            await alice.EnableOmemoAsync();
            await bob.EnableOmemoAsync();

            var beiBob = new List<String>();
            bob.OnEncryptedMessage += (n, _) => { lock (beiBob) beiBob.Add(n.Body); };

            await alice.SendEncryptedMessageAsync($"bob@{Server.Domain}", "die erste");
            await WaitFor(() => { lock (beiBob) return beiBob.Count == 1; }, "die erste Nachricht");

            // Ohne Antwort dazwischen.
            await alice.SendEncryptedMessageAsync($"bob@{Server.Domain}", "die zweite");
            await WaitFor(() => { lock (beiBob) return beiBob.Count == 2; },
                          "die zweite Nachricht ohne Antwort dazwischen");

            // Und eine dritte, damit auch der Schritt danach sitzt.
            await alice.SendEncryptedMessageAsync($"bob@{Server.Domain}", "die dritte");
            await WaitFor(() => { lock (beiBob) return beiBob.Count == 3; }, "die dritte Nachricht");

            Assert.That(beiBob, Is.EqualTo(new[] { "die erste", "die zweite", "die dritte" }));

        }

        #endregion

        #region TheOwnOtherDevice_ReadsAlong()

        /// <summary>
        /// Das eigene zweite Gerät liest mit - das eigene <b>erste</b> nicht.
        /// </summary>
        /// <remarks>
        /// Ohne den Mitschnitt an die eigenen Geräte sähe der eigene Rechner
        /// nicht, was das eigene Telefon geschrieben hat; das Gespräch stünde
        /// auf jedem Gerät anders da.
        ///
        /// Das <i>sendende</i> Gerät dagegen bekommt keinen Eintrag - es
        /// müsste eine Sitzung mit sich selbst führen. Beides steht hier
        /// zusammen, weil beides an derselben Zeile hängt und die Mutationen
        /// in beide Richtungen überlebt haben.
        /// </remarks>
        [Test]
        public async Task TheOwnOtherDevice_ReadsAlong()
        {

            MakeContacts("alice", "bob");

            var alice = await ConnectClientAsync("alice", createAccount: false);
            var bob   = await ConnectClientAsync("bob",   createAccount: false);

            var alicesZweites = CreateClient("alice");
            alicesZweites.Connection.Resource = "zweites-geraet";
            await alicesZweites.ConnectAsync();

            await alice.EnableOmemoAsync();
            await alicesZweites.EnableOmemoAsync();
            await bob.EnableOmemoAsync();

            String? beimZweiten = null;
            alicesZweites.OnEncryptedMessage += (n, _) => beimZweiten = n.Body;

            var angekommen = false;
            bob.OnEncryptedMessage += (_, _) => angekommen = true;

            await alice.SendEncryptedMessageAsync($"bob@{Server.Domain}", "an Bob geschrieben");

            await WaitFor(() => angekommen,       "die Nachricht bei Bob");
            await WaitFor(() => beimZweiten is not null,
                          "die Nachricht beim eigenen zweiten Gerät");

            Assert.That(beimZweiten, Is.EqualTo("an Bob geschrieben"));

            // Und das sendende Gerät bekommt keinen Eintrag für sich selbst.
            //
            // Gesucht wird die Nachricht und nicht irgendeine Stanza mit dem
            // OMEMO-Namensraum: Die erste davon ist eine PEP-Veröffentlichung,
            // und an der lässt sich über Schlüsseleinträge nichts ablesen.
            var stanza = Server.Sessions
                               .SelectMany(s => s.Sent)
                               .First(f => f.StartsWith("<message", StringComparison.Ordinal) &&
                                           f.Contains("<encrypted",  StringComparison.Ordinal));

            var element = System.Xml.Linq.XElement.Parse(stanza);

            Assert.That(OmemoEncryptedElement.TryRead(element, out var gelesen), Is.True);

            Assert.That(gelesen!.KeyFor($"alice@{Server.Domain}", alice.Omemo!.Identity.DeviceId),
                        Is.Null,
                        "Das sendende Gerät hat einen Schlüsseleintrag für sich selbst bekommen.");

        }

        #endregion

        #region TheEnvelope_CarriesTheSenderInside()

        /// <summary>
        /// Der Absender steht <b>in</b> der verschlüsselten Hülle - und wird
        /// gegen den der Stanza abgeglichen.
        /// </summary>
        /// <remarks>
        /// Der äussere Absender lässt sich von jedem ändern, der die Stanza in
        /// die Finger bekommt; der innere nicht. Ohne ihn liesse sich ein
        /// Geheimtext abfangen und unter fremdem Namen weiterreichen - die
        /// Verschlüsselung bliebe gültig, und der Empfänger sähe eine
        /// Nachricht, die nie an ihn gerichtet war.
        ///
        /// Zwei Mutationen sind hier durchgekommen, solange die Auskunft
        /// nicht bis zum Aufrufer reichte: die Hülle ohne Absender zu bauen,
        /// und beim Lesen nicht abzugleichen. Beides fällt nur auf, wenn
        /// jemand den inneren Absender <i>sehen</i> kann.
        /// </remarks>
        [Test]
        public async Task TheEnvelope_CarriesTheSenderInside()
        {

            MakeContacts("alice", "bob");

            var alice = await ConnectClientAsync("alice", createAccount: false);
            var bob   = await ConnectClientAsync("bob",   createAccount: false);

            await alice.EnableOmemoAsync();
            await bob.EnableOmemoAsync();

            OmemoDecrypted? auskunft = null;
            bob.OnEncryptedMessage += (_, o) => auskunft = o;

            await alice.SendEncryptedMessageAsync($"bob@{Server.Domain}", "von innen unterschrieben");

            await WaitFor(() => auskunft is not null, "die Nachricht bei Bob");

            Assert.That(auskunft!.EnvelopeFrom, Is.EqualTo($"alice@{Server.Domain}"),
                        "Die Hülle nennt keinen oder einen anderen Absender.");

        }

        #endregion

        #region AForwardedMessage_IsRefused()

        /// <summary>
        /// Eine gültige Nachricht, unter fremdem Namen weitergereicht, wird
        /// abgewiesen.
        /// </summary>
        /// <remarks>
        /// <b>Der Angriff, gegen den die Beigabe aus XEP-0420 steht</b> - und
        /// der einzige Weg, ihre Prüfung wirklich zu belegen.
        ///
        /// Alice schreibt an Bob und Mallory zugleich; beide bekommen ihren
        /// eigenen Schlüsseleintrag. Mallory schickt dieselbe
        /// <c>&lt;encrypted/&gt;</c>-Stanza unverändert an Bob weiter, unter
        /// ihrem eigenen Namen. Bobs Eintrag ist darin unangetastet, sein
        /// Ratchet-Schritt geht auf, die Prüfsumme stimmt - <b>alles
        /// kryptographisch einwandfrei</b>. Nur steht innen „von Alice" und
        /// aussen „von Mallory".
        ///
        /// Ohne den Abgleich sähe Bob eine Nachricht, die Alice nie an ihn
        /// gerichtet hat, geliefert von jemand anderem. Solange die Auskunft
        /// nur mitgeführt und nicht verglichen wurde, ist genau diese Mutation
        /// durch alle anderen Tests gekommen.
        /// </remarks>
        [Test]
        public async Task AForwardedMessage_IsRefused()
        {

            MakeContacts("alice", "bob");
            MakeContacts("alice", "mallory");

            var alice    = await ConnectClientAsync("alice",   createAccount: false);
            var bob      = await ConnectClientAsync("bob",     createAccount: false);
            var mallory  = await ConnectClientAsync("mallory", createAccount: true);

            await alice.EnableOmemoAsync();
            await bob.EnableOmemoAsync();
            await mallory.EnableOmemoAsync();

            var beiBob = 0;
            bob.OnEncryptedMessage += (_, _) => Interlocked.Increment(ref beiBob);

            // Eine Nachricht an beide - beide bekommen ihren Eintrag.
            System.Xml.Linq.XNamespace client = "jabber:client";

            var ergebnis = await alice.Omemo!.EncryptAsync(
                                     [$"bob@{Server.Domain}", $"mallory@{Server.Domain}"],
                                     [new System.Xml.Linq.XElement(client + "body", "nur für euch beide")]);

            Assert.That(ergebnis.Skipped, Is.Empty);

            // Mallory reicht sie unverändert an Bob weiter - unter ihrem Namen.
            await mallory.SendRawAsync(
                      $"<message xmlns='jabber:client' to='bob@{Server.Domain}' type='chat'>" +
                      $"{ergebnis.Element.ToXml()}</message>");

            await WaitAgainst(() => beiBob > 0,
                              "eine weitergereichte Nachricht unter fremdem Namen");

        }

        #endregion

        #region AConversation_RunsInBothDirections()

        /// <summary>
        /// Hin und zurück, mehrfach - und damit dreht sich auch die
        /// Diffie-Hellman-Ratsche.
        /// </summary>
        /// <remarks>
        /// Die erste Nachricht baut die Sitzung auf, die Antwort dreht die
        /// Ratsche, alles Weitere läuft darüber. <b>Erst dieser Ablauf prüft
        /// den Weg, den ein Gespräch tatsächlich nimmt</b> - eine einzelne
        /// Nachricht sagt darüber nichts.
        /// </remarks>
        [Test]
        public async Task AConversation_RunsInBothDirections()
        {

            MakeContacts("alice", "bob");

            var alice = await ConnectClientAsync("alice", createAccount: false);
            var bob   = await ConnectClientAsync("bob",   createAccount: false);

            await alice.EnableOmemoAsync();
            await bob.EnableOmemoAsync();

            var beiBob    = new List<String>();
            var beiAlice  = new List<String>();

            bob.OnEncryptedMessage    += (n, _) => { lock (beiBob)   beiBob.Add(n.Body); };
            alice.OnEncryptedMessage  += (n, _) => { lock (beiAlice) beiAlice.Add(n.Body); };

            for (var i = 0; i < 3; i++)
            {

                await alice.SendEncryptedMessageAsync($"bob@{Server.Domain}", $"von Alice {i}");
                await WaitFor(() => { lock (beiBob) return beiBob.Count == i + 1; },
                              $"Nachricht {i} bei Bob");

                await bob.SendEncryptedMessageAsync($"alice@{Server.Domain}", $"von Bob {i}");
                await WaitFor(() => { lock (beiAlice) return beiAlice.Count == i + 1; },
                              $"Antwort {i} bei Alice");

            }

            Assert.Multiple(() =>
            {

                Assert.That(beiBob,   Is.EqualTo(new[] { "von Alice 0", "von Alice 1", "von Alice 2" }));
                Assert.That(beiAlice, Is.EqualTo(new[] { "von Bob 0",   "von Bob 1",   "von Bob 2" }));

            });

        }

        #endregion

        #region TheFingerprints_MatchOnBothSides()

        /// <summary>
        /// Was Bob als Alices Fingerabdruck sieht, ist der, den Alice hat.
        /// </summary>
        /// <remarks>
        /// <b>Daran hängt die ganze Vertrauensfrage.</b> Zwei Menschen
        /// vergleichen diese Zeichenfolge über einen anderen Weg - am Telefon,
        /// im selben Raum. Stimmten die beiden Darstellungen nicht überein,
        /// wäre der Vergleich unmöglich, und niemand könnte je etwas
        /// bestätigen.
        /// </remarks>
        [Test]
        public async Task TheFingerprints_MatchOnBothSides()
        {

            MakeContacts("alice", "bob");

            var alice = await ConnectClientAsync("alice", createAccount: false);
            var bob   = await ConnectClientAsync("bob",   createAccount: false);

            await alice.EnableOmemoAsync();
            await bob.EnableOmemoAsync();

            var angekommen = false;
            bob.OnEncryptedMessage += (_, _) => angekommen = true;

            await alice.SendEncryptedMessageAsync($"bob@{Server.Domain}", "Hallo");
            await WaitFor(() => angekommen, "die Nachricht bei Bob");

            var beiBobVermerkt = bob.Omemo!.KnownDevices()
                                    .Single(d => d.DeviceId == alice.Omemo!.Identity.DeviceId);

            Assert.Multiple(() =>
            {

                Assert.That(beiBobVermerkt.Fingerprint, Is.EqualTo(alice.Omemo!.Fingerprint),
                            "Bob sieht einen anderen Fingerabdruck, als Alice hat.");

                Assert.That(beiBobVermerkt.Trust, Is.EqualTo(OmemoTrust.Undecided),
                            "Ein frisch gesehenes Gerät gilt als bestätigt.");

                Assert.That(beiBobVermerkt.BareJid, Is.EqualTo($"alice@{Server.Domain}"));

                // Und die Entscheidung lässt sich treffen.
                Assert.That(bob.Omemo.SetTrust($"alice@{Server.Domain}",
                                               alice.Omemo.Identity.DeviceId,
                                               OmemoTrust.Trusted),
                            Is.True);

            });

        }

        #endregion

        #region ADistrustedDevice_IsLeftOut()

        /// <summary>
        /// Ein abgelehntes Gerät bekommt nichts - und der Absender erfährt es.
        /// </summary>
        /// <remarks>
        /// <b>Das Erfahren ist der Punkt.</b> Ein Absender, der nicht merkt,
        /// dass sein Gegenüber ausgelassen wurde, hält das Gespräch für
        /// geführt und wundert sich über die ausbleibende Antwort. Deshalb
        /// nennt das Ergebnis jedes übersprungene Gerät samt Grund, statt
        /// still eine kürzere Empfängerliste zu bauen.
        /// </remarks>
        [Test]
        public async Task ADistrustedDevice_IsLeftOut()
        {

            MakeContacts("alice", "bob");

            var alice = await ConnectClientAsync("alice", createAccount: false);
            var bob   = await ConnectClientAsync("bob",   createAccount: false);

            await alice.EnableOmemoAsync();
            await bob.EnableOmemoAsync();

            var angekommen = false;
            bob.OnEncryptedMessage += (_, _) => angekommen = true;

            // Die erste Nachricht macht Bobs Gerät bei Alice bekannt.
            await alice.SendEncryptedMessageAsync($"bob@{Server.Domain}", "die erste");
            await WaitFor(() => angekommen, "die erste Nachricht");

            Assert.That(alice.Omemo!.SetTrust($"bob@{Server.Domain}",
                                              bob.Omemo!.Identity.DeviceId,
                                              OmemoTrust.Distrusted),
                        Is.True);

            angekommen = false;

            var uebersprungen = await alice.SendEncryptedMessageAsync($"bob@{Server.Domain}", "die zweite");

            Assert.Multiple(() =>
            {

                Assert.That(uebersprungen, Has.Count.EqualTo(1),
                            "Das abgelehnte Gerät wurde nicht gemeldet.");

                Assert.That(uebersprungen[0].DeviceId, Is.EqualTo(bob.Omemo.Identity.DeviceId));
                Assert.That(uebersprungen[0].Reason,   Does.Contain("abgelehnt"));

            });

            await WaitAgainst(() => angekommen, "eine Nachricht an das abgelehnte Gerät");

        }

        #endregion

        #region WithoutOmemo_NothingIsSentInTheClear()

        /// <summary>
        /// Ohne eingeschaltetes OMEMO wird geworfen - und nichts gesendet.
        /// </summary>
        /// <remarks>
        /// <b>Der schlimmste aller Fehler wäre, hier unverschlüsselt zu
        /// senden.</b> Der Aufrufer wollte verschlüsseln; bekäme er
        /// stattdessen lautlos eine gewöhnliche Nachricht, hielte er sie für
        /// geschützt. Eine Ausnahme ist laut, eine unverschlüsselte Nachricht
        /// ist es nicht.
        /// </remarks>
        [Test]
        public async Task WithoutOmemo_NothingIsSentInTheClear()
        {

            var alice   = await ConnectClientAsync("alice");
            var session = Server.SessionOf(alice.FullJid)!;

            Server.AddAccount("bob");

            var vorher = session.Received.Count;

            Assert.That(async () => await alice.SendEncryptedMessageAsync($"bob@{Server.Domain}", "geheim"),
                        Throws.TypeOf<InvalidOperationException>());

            await WaitAgainst(() => session.Received.Skip(vorher)
                                           .Any(f => f.Contains("geheim", StringComparison.Ordinal)),
                              "eine unverschlüsselt gesendete Nachricht");

        }

        #endregion

        #region AConsumedPreKey_IsPersistedImmediately()

        /// <summary>
        /// Der beim Schlüsselaustausch verbrauchte PreKey ist sofort im
        /// Speicher fort - nicht erst beim nächsten Ablegen.
        /// </summary>
        /// <remarks>
        /// <b>Sonst wäre er nach einem Neustart wieder da</b>, und dieselbe
        /// erste Nachricht liesse sich ein zweites Mal einspielen. Der
        /// Neustart ist dabei kein Sonderfall, sondern die Gelegenheit: Er
        /// passiert von selbst.
        /// </remarks>
        [Test]
        public async Task AConsumedPreKey_IsPersistedImmediately()
        {

            MakeContacts("alice", "bob");

            var alice = await ConnectClientAsync("alice", createAccount: false);
            var bob   = await ConnectClientAsync("bob",   createAccount: false);

            var bobsSpeicher = new OmemoMemoryStore();

            await alice.EnableOmemoAsync();
            await bob.EnableOmemoAsync(bobsSpeicher);

            var vorher = bobsSpeicher.LoadIdentity()!.PreKeys.Count;

            var angekommen = false;
            bob.OnEncryptedMessage += (_, _) => angekommen = true;

            await alice.SendEncryptedMessageAsync($"bob@{Server.Domain}", "die erste");
            await WaitFor(() => angekommen, "die Nachricht bei Bob");

            Assert.That(bobsSpeicher.LoadIdentity()!.PreKeys.Count, Is.EqualTo(vorher - 1),
                        "Der verbrauchte PreKey steht noch im Speicher - nach einem Neustart " +
                        "wäre die Nachricht ein zweites Mal annehmbar.");

        }

        #endregion

        #region AChangedIdentityKey_StopsTheMessage()

        /// <summary>
        /// Hat sich der IdentityKey eines Geräts geändert, geht nichts mehr an
        /// es - und der Absender erfährt den Grund.
        /// </summary>
        /// <remarks>
        /// Hergestellt wird der Fall, indem in Alices Speicher ein
        /// <i>falscher</i> Schlüssel für Bobs Gerät vermerkt wird. Aus Alices
        /// Sicht sieht das genauso aus wie ein Angreifer, der Bobs Bundle
        /// ausgetauscht hat - und genau dann darf nichts hinausgehen.
        /// </remarks>
        [Test]
        public async Task AChangedIdentityKey_StopsTheMessage()
        {

            MakeContacts("alice", "bob");

            var alice = await ConnectClientAsync("alice", createAccount: false);
            var bob   = await ConnectClientAsync("bob",   createAccount: false);

            var alicesSpeicher = new OmemoMemoryStore();

            await alice.EnableOmemoAsync(alicesSpeicher);
            await bob.EnableOmemoAsync();

            // Alice hat sich Bobs Gerät mit einem anderen Schlüssel gemerkt.
            alicesSpeicher.RecordIdentity($"bob@{Server.Domain}",
                                          bob.Omemo!.Identity.DeviceId,
                                          OmemoIdentity.Create().PublicIdentityKey);

            var angekommen = false;
            bob.OnEncryptedMessage += (_, _) => angekommen = true;

            var uebersprungen = await alice.SendEncryptedMessageAsync($"bob@{Server.Domain}", "geheim");

            Assert.Multiple(() =>
            {

                Assert.That(uebersprungen, Has.Count.EqualTo(1),
                            "Das Gerät mit geändertem Schlüssel wurde nicht übersprungen.");

                Assert.That(uebersprungen[0].Reason, Does.Contain("IdentityKey"));

            });

            await WaitAgainst(() => angekommen, "eine Nachricht trotz geändertem IdentityKey");

        }

        #endregion

        #region EnablingOmemo_KeepsAForeignEntryInTheOwnList()

        /// <summary>
        /// Das Einschalten ergänzt die eigene Geräteliste und schreibt sie
        /// nicht neu.
        /// </summary>
        /// <remarks>
        /// <b>Dieser Test musste einen Umweg nehmen, und der Grund ist
        /// lehrreich.</b> Ein Test mit zwei echten Clients findet den Fehler
        /// nicht: Verdrängt das zweite Gerät das erste, bemerkt das erste die
        /// PEP-Benachrichtigung und trägt sich sofort wieder ein (D66) - der
        /// Endzustand stimmt wieder, und der Test sieht nichts.
        ///
        /// Deshalb steht hier ein Eintrag für ein Gerät, das es gar nicht
        /// gibt. Es kann sich nicht wehren, und damit bleibt sichtbar, was das
        /// Einschalten mit der Liste tut.
        /// </remarks>
        [Test]
        public async Task EnablingOmemo_KeepsAForeignEntryInTheOwnList()
        {

            var alice = await ConnectClientAsync("alice");

            // Ein Gerät, zu dem es keinen Client gibt.
            await alice.Connection.PublishOmemoDeviceListAsync(
                      new OmemoDeviceList([new OmemoDevice(4711, "altes Telefon")]));

            await alice.EnableOmemoAsync();

            var liste = await alice.Connection.FetchOmemoDeviceListAsync($"alice@{Server.Domain}");

            Assert.Multiple(() =>
            {

                Assert.That(liste!.Contains(4711u), Is.True,
                            "Das Einschalten hat die Liste neu geschrieben und das andere Gerät " +
                            "verdrängt.");

                Assert.That(liste.Contains(alice.Omemo!.Identity.DeviceId), Is.True);

            });

        }

        #endregion

        #region TheDeviceList_KeepsTheOtherDevices()

        /// <summary>
        /// Schaltet ein zweites Gerät OMEMO ein, bleibt das erste in der
        /// Liste.
        /// </summary>
        /// <remarks>
        /// Wer die Liste beim Einschalten neu schriebe, verdrängte jedes
        /// andere Gerät desselben Menschen - und die bekämen von da an nichts
        /// mehr, ohne dass jemand etwas bemerkt. Der Wiedereintrag aus D66
        /// heilte das erst beim nächsten Mal, und auch nur, wenn das andere
        /// Gerät gerade online ist.
        /// </remarks>
        [Test]
        public async Task TheDeviceList_KeepsTheOtherDevices()
        {

            var erstes = await ConnectClientAsync("alice");

            var zweites = CreateClient("alice");
            zweites.Connection.Resource = "zweites-geraet";
            await zweites.ConnectAsync();

            await erstes.EnableOmemoAsync();
            await zweites.EnableOmemoAsync();

            var liste = await zweites.Connection.FetchOmemoDeviceListAsync($"alice@{Server.Domain}");

            Assert.Multiple(() =>
            {

                Assert.That(liste!.Contains(erstes.Omemo!.Identity.DeviceId), Is.True,
                            "Das erste Gerät wurde aus der Liste verdrängt.");

                Assert.That(liste.Contains(zweites.Omemo!.Identity.DeviceId), Is.True);

                Assert.That(liste.Devices, Has.Count.EqualTo(2));

            });

        }

        #endregion

    }

}
