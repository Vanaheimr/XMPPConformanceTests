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

using System.Text.RegularExpressions;
using System.Xml.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;
using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// XEP-0115, Abschnitt 5.4: Der Cache nimmt eine disco#info-Antwort erst
    /// auf, wenn ihr Hash den angekündigten <c>ver</c>-Wert ergibt.
    /// </summary>
    /// <remarks>
    /// Ohne diese Prüfung war der Cache von jedem vergiftbar, dessen Presence
    /// hier ankommt. Der Angreifer kündigt das <c>node#ver</c>-Paar eines
    /// verbreiteten Clients an und antwortet auf die folgende Abfrage mit einer
    /// Liste seiner Wahl; unter diesem Paar liegt fortan seine Liste, und
    /// ausgeliefert wird sie an jeden weiteren Kontakt, der dasselbe Paar
    /// ankündigt — ohne dass der je gefragt würde.
    ///
    /// Geprüft wird ohne Server: <see cref="DiscoManager"/> bekommt eine
    /// Sende-Funktion, die die Abfrage nur mitschreibt, und die Antwort wird
    /// von Hand eingespeist. Nur so lässt sich eine Antwort bauen, die zum
    /// angekündigten Hash nicht passt — ein ehrlicher Client käme gar nicht
    /// dazu.
    /// </remarks>
    [TestFixture]
    public class CapsVerificationTests
    {

        #region Data

        private const String Knoten   = "https://example.org/client";
        private const String Mallory  = "mallory@example.org/r";
        private const String Alice    = "alice@example.org/r";

        /// <summary>Die Features, die der verbreitete Client wirklich hat.</summary>
        private static readonly String[] Echt = [
            "http://jabber.org/protocol/caps",
            "http://jabber.org/protocol/disco#info"
        ];

        /// <summary>Die Liste, die der Angreifer stattdessen unterschieben will.</summary>
        private static readonly String[] Untergeschoben = [
            "urn:xmpp:receipts"
        ];

        private static readonly DiscoIdentity Identitaet = new("client", "pc", "Exodus 0.9.1");

        private DiscoManager        disco       = null!;
        private EntityCapsManager   caps        = null!;
        private List<String>        gesendet    = null!;
        private List<String>        abgelehnt   = null!;
        private List<DiscoInfo>     gemeldet    = null!;

        #endregion

        #region SetUp

        [SetUp]
        public void Setup()
        {

            gesendet   = [];
            abgelehnt  = [];
            gemeldet   = [];

            disco = new DiscoManager(xml =>
            {
                lock (gesendet) gesendet.Add(xml);
                return Task.CompletedTask;
            });

            caps = new EntityCapsManager(disco) { Node = Knoten };

            caps.OnCapsRejected   += (from, grund) => { lock (abgelehnt) abgelehnt.Add(grund); };
            caps.OnCapsDiscovered += (from, info)  => { lock (gemeldet)  gemeldet.Add(info);   };

        }

        #endregion

        #region Hilfsfunktionen

        /// <summary>Der Verification String über diese Features.</summary>
        private static String VerOf(params String[] features)
            => EntityCapsManager.VerificationString([Identitaet], features);

        /// <summary>Eine disco#info-Antwort mit genau diesen Features.</summary>
        private static String Antwort(params String[] features)
            => "<query xmlns='http://jabber.org/protocol/disco#info'>" +
               $"<identity category='{Identitaet.Category}' type='{Identitaet.Type}' " +
               $"name='{Identitaet.Name}'/>" +
               String.Concat(features.Select(f => $"<feature var='{f}'/>")) +
               "</query>";

        /// <summary>
        /// Eine disco#info-Antwort mit einem softwareinfo-Formular, dessen
        /// <c>os</c>-Feld diesen Wert trägt.
        /// </summary>
        private static String AntwortMitFormular(String os)
            => "<query xmlns='http://jabber.org/protocol/disco#info'>" +
               $"<identity category='{Identitaet.Category}' type='{Identitaet.Type}' " +
               $"name='{Identitaet.Name}'/>" +
               String.Concat(Echt.Select(f => $"<feature var='{f}'/>")) +
               "<x xmlns='jabber:x:data' type='result'>" +
               "<field var='FORM_TYPE' type='hidden'>" +
               "<value>urn:xmpp:dataforms:softwareinfo</value></field>" +
               $"<field var='os'><value>{os}</value></field>" +
               "</x></query>";

        /// <summary>Der dazu passende Verification String.</summary>
        private static String VerMitFormular(String os)
            => EntityCapsManager.VerificationString(
                   [Identitaet],
                   Echt,
                   [new DiscoForm([
                        new DiscoField("FORM_TYPE", "hidden", ["urn:xmpp:dataforms:softwareinfo"]),
                        new DiscoField("os",        null,     [os])
                    ])]);

        private Int32 Abfragen
        {
            get { lock (gesendet) return gesendet.Count; }
        }

        /// <summary>Wartet, bis so viele disco#info-Abfragen abgeschickt sind.</summary>
        private async Task WaitForQueries(Int32 count)
        {

            var ok = await XMPPServer.WaitUntilAsync(() => Abfragen >= count);

            Assert.That(ok, Is.True,
                        $"Erwartet waren {count} disco#info-Abfragen, abgeschickt wurden {Abfragen}.");

        }

        /// <summary>Beantwortet die zuletzt abgeschickte Abfrage.</summary>
        private void Answer(String from, String query)
        {

            String letzte;
            lock (gesendet) letzte = gesendet[^1];

            var id = Regex.Match(letzte, @"id='([^']+)'").Groups[1].Value;

            disco.ProcessInfoResult(id,
                                    XElement.Parse($"<iq type='result' id='{id}'>{query}</iq>"),
                                    from);

        }

        #endregion


        #region AnAnswerThatDoesNotHashToTheAnnouncedVer_IsNotCached()

        /// <summary>
        /// Der Kern: Wer ein <c>ver</c> ankündigt und etwas anderes antwortet,
        /// kommt nicht in den Cache.
        /// </summary>
        [Test]
        public async Task AnAnswerThatDoesNotHashToTheAnnouncedVer_IsNotCached()
        {

            var ver     = VerOf(Echt);
            var laeuft  = caps.ProcessCapsAsync(Mallory, Knoten, ver, EntityCapsManager.Sha1Algorithm);

            await WaitForQueries(1);
            Answer(Mallory, Antwort(Untergeschoben));
            await laeuft;

            Assert.Multiple(() =>
            {

                Assert.That(caps.GetCachedInfo($"{Knoten}#{ver}"), Is.Null,
                            "Die untergeschobene Antwort darf nicht im Cache stehen.");

                Assert.That(abgelehnt, Is.Not.Empty, "Die Ablehnung muss gemeldet werden.");

                // Gemeldet wird sie trotzdem: Sie ist das, was diese Entity über
                // sich selbst sagt, und genau das ergäbe auch eine gewöhnliche
                // disco#info-Abfrage. Verweigert wird nur das Bündeln.
                Assert.That(gemeldet, Has.Count.EqualTo(1));

            });

        }

        #endregion

        #region AnAnswerThatHashesToTheAnnouncedVer_IsCached()

        /// <summary>
        /// Die Gegenprobe: Die ehrliche Antwort wird abgelegt.
        /// </summary>
        /// <remarks>
        /// Ohne sie bestünde die Sammlung auch dann, wenn schlicht nichts mehr
        /// in den Cache käme — und der ganze Zweck von XEP-0115, die zweite
        /// Abfrage zu sparen, wäre still verschwunden.
        /// </remarks>
        [Test]
        public async Task AnAnswerThatHashesToTheAnnouncedVer_IsCached()
        {

            var ver     = VerOf(Echt);
            var laeuft  = caps.ProcessCapsAsync(Alice, Knoten, ver, EntityCapsManager.Sha1Algorithm);

            await WaitForQueries(1);
            Answer(Alice, Antwort(Echt));
            await laeuft;

            var abgelegt = caps.GetCachedInfo($"{Knoten}#{ver}");

            Assert.Multiple(() =>
            {

                Assert.That(abgelegt, Is.Not.Null, "Die geprüfte Antwort gehört in den Cache.");
                Assert.That(abgelegt!.Features, Is.EquivalentTo(Echt));

                Assert.That(abgelehnt, Is.Empty,
                            $"Ohne Grund abgelehnt: {String.Join(" | ", abgelehnt)}");

            });

        }

        #endregion

        #region ThePoisonedEntryIsNotServedToTheNextContact()

        /// <summary>
        /// Der eigentliche Schaden, ausbuchstabiert: Was der Angreifer
        /// hinterlässt, darf dem nächsten Kontakt nicht als dessen Auskunft
        /// ausgeliefert werden.
        /// </summary>
        /// <remarks>
        /// Das ist der Test, der die Vergiftung als solche zeigt. Die anderen
        /// zeigen nur, dass ein Eintrag fehlt — hier fehlt er an der Stelle,
        /// an der er Wirkung entfaltet hätte: Alice kündigt dasselbe Paar an
        /// und wird deshalb ein zweites Mal gefragt, statt Mallorys Liste
        /// untergeschoben zu bekommen.
        /// </remarks>
        [Test]
        public async Task ThePoisonedEntryIsNotServedToTheNextContact()
        {

            var ver = VerOf(Echt);

            // Mallory kündigt das Paar eines verbreiteten Clients an und
            // antwortet mit einer Liste seiner Wahl.
            var angriff = caps.ProcessCapsAsync(Mallory, Knoten, ver, EntityCapsManager.Sha1Algorithm);
            await WaitForQueries(1);
            Answer(Mallory, Antwort(Untergeschoben));
            await angriff;

            // Alice kündigt dasselbe Paar an - diesmal zu Recht.
            var ehrlich = caps.ProcessCapsAsync(Alice, Knoten, ver, EntityCapsManager.Sha1Algorithm);
            await WaitForQueries(2);
            Answer(Alice, Antwort(Echt));
            await ehrlich;

            Assert.Multiple(() =>
            {

                Assert.That(Abfragen, Is.EqualTo(2),
                            "Alice muss selbst gefragt werden; aus dem Cache bedient zu werden " +
                            "hiesse, Mallorys Liste für ihre zu halten.");

                Assert.That(gemeldet[^1].Features, Is.EquivalentTo(Echt));
                Assert.That(gemeldet[^1].Features, Does.Not.Contain("urn:xmpp:receipts"));

            });

        }

        #endregion

        #region WithoutAHashAttribute_NothingIsCached()

        /// <summary>
        /// Die Altform aus XEP-0115 vor 1.4: <c>ver</c> ist dort eine
        /// Versionsnummer und kein Hash. Nachrechnen lässt sich nichts, also
        /// wird auch nichts abgelegt.
        /// </summary>
        /// <remarks>
        /// Ohne diese Regel bliebe der bequemste Weg offen: Wer den Cache
        /// vergiften will, lässt das <c>hash</c>-Attribut einfach weg.
        ///
        /// Geprüft wird auch die Begründung, und das nicht aus Ordnungsliebe:
        /// Ein fehlendes Attribut fiele sonst unter „unbekannter Algorithmus"
        /// (<c>null</c> ist nun einmal nicht <c>sha-1</c>), und der eigene
        /// Zweig dafür wäre nicht mehr als Zierde. Der Unterschied gehört ins
        /// Protokoll: Die Gegenstelle ist nicht kaputt, sie ist alt.
        /// </remarks>
        [Test]
        public async Task WithoutAHashAttribute_NothingIsCached()
        {

            var ver     = VerOf(Echt);
            var laeuft  = caps.ProcessCapsAsync(Mallory, Knoten, ver, hash: null);

            await WaitForQueries(1);
            Answer(Mallory, Antwort(Echt));
            await laeuft;

            Assert.Multiple(() =>
            {

                Assert.That(caps.GetCachedInfo($"{Knoten}#{ver}"), Is.Null);
                Assert.That(gemeldet, Has.Count.EqualTo(1));

                Assert.That(abgelehnt.Any(g => g.Contains("kein hash-Attribut", StringComparison.Ordinal)),
                            Is.True,
                            $"Die Altform muss als solche benannt werden. Gemeldet wurde: " +
                            $"{String.Join(" | ", abgelehnt)}");

            });

        }

        #endregion

        #region AnUnknownHashAlgorithm_IsNotCached()

        /// <summary>
        /// Und ein Algorithmus, den dieser Client nicht rechnen kann, ebenso —
        /// auch wenn er stärker ist als SHA-1.
        /// </summary>
        [Test]
        public async Task AnUnknownHashAlgorithm_IsNotCached()
        {

            var ver     = VerOf(Echt);
            var laeuft  = caps.ProcessCapsAsync(Mallory, Knoten, ver, "sha-256");

            await WaitForQueries(1);
            Answer(Mallory, Antwort(Echt));
            await laeuft;

            Assert.Multiple(() =>
            {
                Assert.That(caps.GetCachedInfo($"{Knoten}#{ver}"), Is.Null);
                Assert.That(abgelehnt, Is.Not.Empty);
            });

        }

        #endregion

        #region AnAnswerWithADataForm_IsVerifiedIncludingTheForm()

        /// <summary>
        /// Eine Antwort mit XEP-0128-Datenformular wird geprüft und abgelegt —
        /// das Formular geht in den Hash ein.
        /// </summary>
        /// <remarks>
        /// Der Rahmen wird durch das Gegenstück gleich darunter geschlossen:
        /// Hier steht, dass ein Formular nicht mehr im Weg ist; dort, dass es
        /// wirklich mitgerechnet wird. Ohne beides zusammen liesse sich der
        /// Test auch bestehen, indem man Formulare schlicht übergeht.
        /// </remarks>
        [Test]
        public async Task AnAnswerWithADataForm_IsVerifiedIncludingTheForm()
        {

            var ver     = VerMitFormular("Mac");
            var laeuft  = caps.ProcessCapsAsync(Alice, Knoten, ver, EntityCapsManager.Sha1Algorithm);

            await WaitForQueries(1);
            Answer(Alice, AntwortMitFormular("Mac"));
            await laeuft;

            var abgelegt = caps.GetCachedInfo($"{Knoten}#{ver}");

            Assert.Multiple(() =>
            {

                Assert.That(abgelehnt, Is.Empty,
                            $"Ohne Grund abgelehnt: {String.Join(" | ", abgelehnt)}");

                Assert.That(abgelegt, Is.Not.Null,
                            "Eine Antwort mit Formular gehört genauso in den Cache wie eine ohne.");

                Assert.That(abgelegt!.Forms, Has.Count.EqualTo(1),
                            "Das Formular muss erhalten bleiben.");

                Assert.That(abgelegt.Forms[0].FormType,
                            Is.EqualTo("urn:xmpp:dataforms:softwareinfo"));

            });

        }

        #endregion

        #region AnAnswerWhoseFormWasChanged_IsNotCached()

        /// <summary>
        /// Und die Gegenprobe: Wird am Formular etwas geändert, passt der Hash
        /// nicht mehr.
        /// </summary>
        /// <remarks>
        /// Ohne diesen Test wäre „Formulare einfach übergehen" eine bestehende
        /// Lösung — und damit stünde genau die Lücke wieder offen, um die es
        /// geht: Zwei Entities, die sich allein in ihren erweiterten Angaben
        /// unterscheiden, hätten denselben <c>ver</c>-Wert, und die Antwort
        /// der einen liesse sich der anderen zuschreiben.
        /// </remarks>
        [Test]
        public async Task AnAnswerWhoseFormWasChanged_IsNotCached()
        {

            var ver     = VerMitFormular("Mac");
            var laeuft  = caps.ProcessCapsAsync(Mallory, Knoten, ver, EntityCapsManager.Sha1Algorithm);

            await WaitForQueries(1);
            Answer(Mallory, AntwortMitFormular("Windows"));
            await laeuft;

            Assert.Multiple(() =>
            {
                Assert.That(caps.GetCachedInfo($"{Knoten}#{ver}"), Is.Null);
                Assert.That(abgelehnt, Is.Not.Empty);
            });

        }

        #endregion

        #region XmlLangOfAnIdentity_GoesIntoTheHash()

        /// <summary>
        /// Das <c>xml:lang</c> einer Identität geht in den Hash ein — und muss
        /// dafür erst einmal beim Zerlegen der Antwort erhalten bleiben.
        /// </summary>
        /// <remarks>
        /// Eine Entity darf denselben Namen in mehreren Sprachen führen; im
        /// Verification String steht die Sprache zwischen Typ und Name. Wer sie
        /// beim Zerlegen verliert, errechnet für jede solche Gegenstelle einen
        /// anderen Wert als sie selbst — und lehnt sie ab, obwohl sie ehrlich
        /// ist.
        /// </remarks>
        [Test]
        public async Task XmlLangOfAnIdentity_GoesIntoTheHash()
        {

            var ver = EntityCapsManager.VerificationString(
                          [new DiscoIdentity("client", "pc", "Psi 0.11", "en")],
                          Echt);

            var antwort =
                "<query xmlns='http://jabber.org/protocol/disco#info'>" +
                "<identity xml:lang='en' category='client' type='pc' name='Psi 0.11'/>" +
                String.Concat(Echt.Select(f => $"<feature var='{f}'/>")) +
                "</query>";

            var laeuft = caps.ProcessCapsAsync(Alice, Knoten, ver, EntityCapsManager.Sha1Algorithm);

            await WaitForQueries(1);
            Answer(Alice, antwort);
            await laeuft;

            var abgelegt = caps.GetCachedInfo($"{Knoten}#{ver}");

            Assert.Multiple(() =>
            {

                Assert.That(abgelehnt, Is.Empty,
                            $"Ohne Grund abgelehnt: {String.Join(" | ", abgelehnt)}");

                Assert.That(abgelegt, Is.Not.Null);
                Assert.That(abgelegt!.Identities[0].Language, Is.EqualTo("en"));

            });

        }

        #endregion

        #region AnAmbiguousAnswer_IsNotCached()

        /// <summary>
        /// XEP-0115, Abschnitt 5.4: Eine Antwort, die sich nicht eindeutig in
        /// eine Zeichenkette überführen lässt, wird als ganze verworfen.
        /// </summary>
        /// <remarks>
        /// Das ist keine Formstrenge. Wo Doppelungen stehen, gibt es mehr als
        /// eine mögliche Zeichenkette zu derselben Antwort — und damit lässt
        /// sich zu einem gegebenen Hash eine zweite Antwort bauen. Sich für
        /// eine Lesart zu entscheiden hiesse, dem Angreifer die Wahl zu lassen,
        /// welche er meint.
        /// </remarks>
        [Test]
        public async Task AnAmbiguousAnswer_IsNotCached()
        {

            const String Ich = "<identity category='client' type='pc' name='Exodus 0.9.1'/>";

            String Query(String inhalt)
                => $"<query xmlns='http://jabber.org/protocol/disco#info'>{inhalt}</query>";

            DiscoForm Formular(params String[] typen)
                => new([new DiscoField("FORM_TYPE", "hidden", typen)]);

            const String FormularXml =
                "<x xmlns='jabber:x:data' type='result'>" +
                "<field var='FORM_TYPE' type='hidden'><value>urn:test:form</value></field></x>";

            // Entscheidend ist, dass der angekündigte ver-Wert zu der
            // mehrdeutigen Antwort *passt*: Sonst schlüge schon der
            // Hash-Vergleich zu, und diese Regeln wären ungeprüft.
            var faelle = new (String Name, String Antwort, String Ver)[]
            {

                ("dasselbe Feature zweimal",
                 Query(Ich + "<feature var='urn:test:a'/><feature var='urn:test:a'/>"),
                 EntityCapsManager.VerificationString([Identitaet], ["urn:test:a", "urn:test:a"])),

                ("dieselbe Identität zweimal",
                 Query(Ich + Ich),
                 EntityCapsManager.VerificationString([Identitaet, Identitaet], [])),

                ("zwei Formulare mit demselben FORM_TYPE",
                 Query(Ich + FormularXml + FormularXml),
                 EntityCapsManager.VerificationString([Identitaet], [],
                                                      [Formular("urn:test:form"),
                                                       Formular("urn:test:form")])),

                // Der zweite Wert verschwindet spurlos aus der Rechnung - das
                // FORM_TYPE-Feld selbst wird ja nicht mit angehängt. Zwei
                // verschiedene Antworten ergäben damit denselben Hash.
                ("ein FORM_TYPE mit zwei Werten",
                 Query(Ich + "<x xmlns='jabber:x:data' type='result'>" +
                             "<field var='FORM_TYPE' type='hidden'>" +
                             "<value>urn:test:a</value><value>urn:test:b</value></field></x>"),
                 EntityCapsManager.VerificationString([Identitaet], [],
                                                      [Formular("urn:test:a", "urn:test:b")]))

            };

            foreach (var (name, antwort, ver) in faelle)
            {

                Setup();

                var laeuft = caps.ProcessCapsAsync(Mallory, Knoten, ver, EntityCapsManager.Sha1Algorithm);

                await WaitForQueries(1);
                Answer(Mallory, antwort);
                await laeuft;

                Assert.Multiple(() =>
                {

                    Assert.That(caps.GetCachedInfo($"{Knoten}#{ver}"), Is.Null,
                                $"Mehrdeutig, aber der Hash stimmte - und abgelegt: {name}.");

                    Assert.That(abgelehnt, Is.Not.Empty,
                                $"Ohne Meldung durchgelassen: {name}.");

                });

            }

        }

        #endregion

        #region ACachedEntryIsServedWithoutAsking()

        /// <summary>
        /// Und wozu das Ganze da ist: Ein geprüfter Eintrag erspart dem
        /// nächsten Kontakt die Abfrage.
        /// </summary>
        [Test]
        public async Task ACachedEntryIsServedWithoutAsking()
        {

            var ver = VerOf(Echt);

            var erste = caps.ProcessCapsAsync(Alice, Knoten, ver, EntityCapsManager.Sha1Algorithm);
            await WaitForQueries(1);
            Answer(Alice, Antwort(Echt));
            await erste;

            await caps.ProcessCapsAsync(Mallory, Knoten, ver, EntityCapsManager.Sha1Algorithm);

            Assert.Multiple(() =>
            {
                Assert.That(Abfragen, Is.EqualTo(1), "Der Cache muss die zweite Abfrage sparen.");
                Assert.That(gemeldet, Has.Count.EqualTo(2));
            });

        }

        #endregion

    }

}
