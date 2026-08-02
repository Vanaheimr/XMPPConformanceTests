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

using System.Security.Cryptography;
using System.Text;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP.Server
{

    /// <summary>
    /// Ein Konto auf dem Testserver: Zugangsdaten und serverseitiger Roster.
    /// </summary>
    public sealed class XMPPAccount
    {

        #region Data

        private readonly List<RosterEntry> _roster = [];
        private readonly Dictionary<String, String> _pendingRequests = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<OfflineMessage> _offlineMessages = [];
        private readonly Lock _lock = new();

        /// <summary>
        /// Die PEP-Knoten dieses Kontos (XEP-0163): je Knoten die Einträge
        /// nach ihrer Kennung.
        /// </summary>
        /// <remarks>
        /// Am Konto und nicht an der Sitzung, und das ist der ganze Sinn von
        /// PEP: Ein Bundle muss abrufbar sein, <b>während sein Besitzer
        /// offline ist</b> - sonst könnte niemand ihm verschlüsselt schreiben,
        /// bevor er das nächste Mal erscheint. Der Server antwortet hier
        /// stellvertretend für einen Menschen, der gerade nicht da ist.
        /// </remarks>
        private readonly Dictionary<String, Dictionary<String, String>> _pepNodes =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Die Einstellungen je Knoten (XEP-0060, Abschnitt 8.2).
        /// </summary>
        /// <remarks>
        /// Getrennt von den Einträgen, weil ein Knoten und sein Inhalt zwei
        /// Dinge sind: Ein angelegter Knoten hat noch keine Einträge, und ein
        /// Knoten ohne Ablage bekommt nie welche - beide gibt es trotzdem.
        /// </remarks>
        private readonly Dictionary<String, PubSubNodeConfiguration> _pepNodeConfigs =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Die Rollen je Knoten (XEP-0060, Abschnitt 4.1).
        /// </summary>
        /// <remarks>
        /// <b>Der Eigentümer steht hier nicht drin.</b> Er ist das Konto, und
        /// eine Eintragung, die immer dasselbe sagt, kann nur fehlen oder
        /// falsch werden.
        /// </remarks>
        private readonly Dictionary<String, Dictionary<String, PubSubAffiliation>> _pepAffiliations =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Die Abonnements je Knoten, jedes mit seinem Abonnenten und seiner
        /// Kennung (XEP-0060, Abschnitt 6.1).
        /// </summary>
        /// <remarks>
        /// Ebenfalls am Konto und nicht an der Sitzung: Ein Abonnement gilt
        /// über die Anwesenheit beider Seiten hinaus. Wer abonniert hat und
        /// dann geht, hat es beim Wiederkommen noch - alles andere wäre kein
        /// Abonnement, sondern eine Anwesenheitsliste.
        ///
        /// <b>Eine Liste und keine Abbildung nach JID</b>: Derselbe JID darf
        /// mehrere Abonnements auf denselben Knoten halten, und eine Abbildung
        /// könnte das zweite nur verschlucken. Der Knoten wird nach
        /// <see cref="StringComparer.Ordinal"/> unterschieden wie in
        /// <see cref="_pepNodes"/>, der Abonnent beim Vergleichen ohne Rücksicht
        /// auf Gross- und Kleinschreibung wie jeder JID.
        /// </remarks>
        private readonly Dictionary<String, List<PepSubscription>> _pepSubscriptions =
            new(StringComparer.Ordinal);

        #endregion

        #region Properties

        /// <summary>Bare-JID des Kontos, z.B. alice@localhost.</summary>
        public String BareJid { get; }

        /// <summary>
        /// Die Zugangsdaten für die SASL-Authentifizierung - abgeleitet, nicht
        /// im Klartext.
        /// </summary>
        public XMPPCredentials Credentials { get; }

        /// <summary>Momentaufnahme des serverseitigen Rosters.</summary>
        public IReadOnlyList<RosterEntry> Roster
        {
            get { lock (_lock) return _roster.ToList(); }
        }

        /// <summary>
        /// RFC 6121, Abschnitt 2.6: Die Fassung des Rosters - eine
        /// undurchsichtige Zeichenkette, die sich mit jeder Änderung ändert.
        /// </summary>
        /// <remarks>
        /// Gerechnet statt gezählt. Ein Zähler wäre die naheliegende Wahl,
        /// müsste aber mit dem Konto gespeichert werden und überstünde einen
        /// Neustart nur, wenn jemand daran denkt. Ein Streuwert über den Inhalt
        /// braucht keinen Speicher, ist nach einem Neustart derselbe und bleibt
        /// auch dann richtig, wenn jemand den Roster an der Datei vorbei
        /// ändert.
        ///
        /// Er hat eine Eigenschaft, die ein Zähler nicht hat: Geht der Roster
        /// von A nach B und wieder zurück nach A, ist die Fassung wieder die
        /// alte. Das ist kein Mangel, sondern richtig - der Zwischenstand eines
        /// Clients, der A zwischengespeichert hat, stimmt ja wieder.
        ///
        /// Die Trennzeichen sind Steuerzeichen, die in keinem Feld vorkommen
        /// können. Ohne sie ergäben ein Kontakt „ab" ohne Namen und ein Kontakt
        /// „a" mit dem Namen „b" dieselbe Zeichenfolge.
        /// </remarks>
        public String RosterVersion
        {
            get
            {

                var sb = new StringBuilder();

                foreach (var e in Roster.OrderBy(e => e.Jid, StringComparer.Ordinal))
                    sb.Append(e.Jid).         Append('\u001F').
                       Append(e.Name).        Append('\u001F').
                       Append(e.Subscription).Append('\u001F').
                       Append(e.Ask).         Append('\u001F').
                       Append(e.Approved).    Append('\u001E');

                return Convert.ToBase64String(
                           SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))
                       )[..16];

            }
        }

        /// <summary>
        /// Unbeantwortete Subscription-Anfragen, nach dem Bare-JID des
        /// Antragstellers (RFC 6121, Abschnitt 3.1.3, Regel 4).
        /// </summary>
        /// <remarks>
        /// Aufbewahrt wird die vollständige Stanza und nicht bloss der
        /// Absender: der Abschnitt verlangt das ausdrücklich, weil eine
        /// Anfrage erweiterten Inhalt tragen darf - vor allem das
        /// <c>&lt;status/&gt;</c>, mit dem ein Mensch begründet, warum er
        /// fragt. Wer sich nur den Absender merkt, stellt beim nächsten
        /// Anmelden eine andere Anfrage zu als die, die gestellt wurde.
        ///
        /// Neben dem Roster und nicht darin: die Security Warning desselben
        /// Abschnitts untersagt einen Roster-Eintrag, solange nicht
        /// zugestimmt wurde.
        /// </remarks>
        public IReadOnlyDictionary<String, String> PendingSubscriptionRequests
        {
            get
            {
                lock (_lock)
                    return new Dictionary<String, String>(_pendingRequests, StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Nachrichten, die aufbewahrt wurden, weil das Konto keine erreichbare
        /// Resource hatte (RFC 6121, Abschnitt 8.5.2.2.1), älteste zuerst.
        /// </summary>
        /// <remarks>
        /// Die Reihenfolge ist nicht Beiwerk. Ein Gespräch, das in falscher
        /// Ordnung nachgereicht wird, ist schwerer zu lesen als eines, das ganz
        /// fehlt - der Leser hält die Antwort für die Frage.
        /// </remarks>
        public IReadOnlyList<OfflineMessage> OfflineMessages
        {
            get { lock (_lock) return _offlineMessages.ToList(); }
        }

        /// <summary>
        /// Wird nach jeder Roster-Änderung gerufen; der Server hängt daran
        /// seinen Kontenspeicher.
        /// </summary>
        /// <remarks>
        /// Hier und nicht an den Aufrufstellen im Server: der Roster lässt
        /// sich auch direkt am Konto ändern - Testhilfen tun genau das -, und
        /// eine Liste von Stellen, an denen man das Speichern nicht vergessen
        /// darf, wird über kurz oder lang unvollständig.
        /// </remarks>
        internal Action<XMPPAccount>? OnChanged { get; set; }

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Legt ein Konto mit einem Klartextpasswort an, das sofort abgeleitet
        /// und danach verworfen wird.
        /// </summary>
        public XMPPAccount(String bareJid, String password)
            : this(bareJid, XMPPCredentials.FromPassword(password))
        { }

        /// <summary>
        /// Legt ein Konto mit bereits abgeleiteten Zugangsdaten an - der Weg,
        /// auf dem ein Kontenspeicher sie wieder einliest.
        /// </summary>
        public XMPPAccount(String bareJid, XMPPCredentials credentials)
        {
            BareJid      = bareJid;
            Credentials  = credentials;
        }

        #endregion


        /// <summary>
        /// Legt einen Roster-Eintrag an oder aktualisiert ihn.
        /// </summary>
        public void SetRosterEntry(RosterEntry entry)
        {

            lock (_lock)
            {
                _roster.RemoveAll(e => String.Equals(e.Jid, entry.Jid, StringComparison.OrdinalIgnoreCase));
                _roster.Add(entry);
            }

            // Ausserhalb der Sperre: der Speicher schreibt womöglich eine
            // Datei, und darauf soll niemand warten, der nur den Roster lesen
            // will.
            OnChanged?.Invoke(this);

        }

        /// <summary>
        /// Entfernt einen Roster-Eintrag.
        /// </summary>
        public void RemoveRosterEntry(String jid)
        {

            lock (_lock)
                _roster.RemoveAll(e => String.Equals(e.Jid, jid, StringComparison.OrdinalIgnoreCase));

            OnChanged?.Invoke(this);

        }

        /// <summary>
        /// Bewahrt eine Subscription-Anfrage auf, bis sie beantwortet ist.
        /// </summary>
        /// <param name="maxStored">
        /// Obergrenze für die Zahl aufbewahrter Anfragen. Ist sie erreicht,
        /// wird die neue verworfen statt eine bereits aufbewahrte zu
        /// verdrängen - sonst könnte ein Angreifer die echte Anfrage eines
        /// Bekannten gezielt hinausdrängen.
        /// </param>
        /// <returns>
        /// false, wenn nichts aufbewahrt wurde: entweder liegt von diesem
        /// Absender bereits eine Anfrage vor, oder die Grenze ist erreicht.
        /// In beiden Fällen soll auch nichts zugestellt werden.
        /// </returns>
        /// <remarks>
        /// Abschnitt 3.1.3 stellt frei, ob die erste oder die letzte Anfrage
        /// eines Absenders aufbewahrt wird, verlangt aber, dass es genau eine
        /// bleibt ("this helps to prevent 'subscription request spam'"). Hier
        /// bleibt die erste stehen: sonst bestimmte derjenige, der zuletzt
        /// fragt, den Inhalt dessen, was der Kontakt beim nächsten Anmelden zu
        /// sehen bekommt, und könnte ihn beliebig oft austauschen.
        /// </remarks>
        public Boolean RememberSubscriptionRequest(String   fromBareJid,
                                                   String   stanza,
                                                   Int32    maxStored = Int32.MaxValue)
        {

            lock (_lock)
            {

                if (_pendingRequests.ContainsKey(fromBareJid) ||
                    _pendingRequests.Count >= maxStored)
                {
                    return false;
                }

                _pendingRequests[fromBareJid] = stanza;

            }

            OnChanged?.Invoke(this);

            return true;

        }

        /// <summary>
        /// Vergisst eine aufbewahrte Anfrage - sie ist beantwortet.
        /// </summary>
        /// <returns>true, wenn eine vorlag.</returns>
        public Boolean ForgetSubscriptionRequest(String fromBareJid)
        {

            Boolean entfernt;

            lock (_lock)
                entfernt = _pendingRequests.Remove(fromBareJid);

            if (entfernt)
                OnChanged?.Invoke(this);

            return entfernt;

        }

        /// <summary>
        /// Liegt von diesem Kontakt eine unbeantwortete Anfrage vor?
        /// </summary>
        /// <remarks>
        /// Die Frage, an der nach Abschnitt 3.4.2 hängt, ob ein
        /// <c>&lt;presence type='subscribed'/&gt;</c> eine Zustimmung ist oder
        /// eine Vormerkung.
        /// </remarks>
        public Boolean HasPendingRequestFrom(String bareJid)
        {
            lock (_lock)
                return _pendingRequests.ContainsKey(bareJid);
        }

        #region PEP (XEP-0163)

        /// <summary>
        /// Legt einen Eintrag in einem PEP-Knoten ab und ersetzt einen
        /// gleichnamigen.
        /// </summary>
        /// <remarks>
        /// <b>Ersetzen und nicht danebenlegen</b>: Die Kennung <i>ist</i> die
        /// Aussage. Die Geräteliste steht unter <c>current</c>, ein Bundle
        /// unter der Gerätekennung - zwei Einträge unter derselben Kennung
        /// wären keine zwei Angaben, sondern eine Unklarheit darüber, welche
        /// gilt.
        ///
        /// Hier weicht der älteste Eintrag, anders als bei der Offline-Ablage
        /// (siehe <see cref="StoreOfflineMessage"/>), wo die <i>neue</i>
        /// Nachricht abgewiesen wird. Der Unterschied ist der Inhalt: Eine
        /// Nachricht ist einmalig und ihr Verlust endgültig; ein PEP-Eintrag
        /// ist der aktuelle Stand einer Sache, und der neueste ist der, auf
        /// den es ankommt.
        ///
        /// <b>Wie viele es sein dürfen und ob überhaupt abgelegt wird, sagt
        /// der Knoten</b> (seit K7). Ein Knoten ohne Ablage meldet nur - wer
        /// nicht zuhörte, hat es verpasst. Er entsteht trotzdem: Eine
        /// Veröffentlichung legt ihn an, auch wenn nichts von ihr bleibt.
        /// </remarks>
        public void PublishPepItem(String  node,
                                   String  itemId,
                                   String  payload)
        {

            lock (_lock)
            {

                if (!_pepNodeConfigs.TryGetValue(node, out var einstellung))
                    _pepNodeConfigs[node] = einstellung = PubSubNodeConfiguration.Default;

                if (!einstellung.PersistItems)
                    return;

                var maxItems = einstellung.MaxItems;

                if (!_pepNodes.TryGetValue(node, out var eintraege))
                    _pepNodes[node] = eintraege = new Dictionary<String, String>(StringComparer.Ordinal);

                eintraege[itemId] = payload;

                while (eintraege.Count > maxItems)
                    eintraege.Remove(eintraege.Keys.First());

            }

        }

        /// <summary>
        /// Die Einträge eines PEP-Knotens.
        /// </summary>
        /// <param name="itemId">
        /// Ein bestimmter Eintrag, oder null für alle.
        /// </param>
        /// <returns>
        /// Die Einträge, oder eine leere Liste - <b>auch dann, wenn es den
        /// Knoten gar nicht gibt</b>. Der Unterschied zwischen „leerer Knoten"
        /// und „kein Knoten" beantwortet die Frage nicht, die der Abrufer
        /// stellt: Er will wissen, ob es etwas abzuholen gibt.
        /// </returns>
        public IReadOnlyList<(String ItemId, String Payload)> GetPepItems(String node, String? itemId = null)
        {

            lock (_lock)
            {

                if (!_pepNodes.TryGetValue(node, out var eintraege))
                    return [];

                if (itemId is not null)
                    return eintraege.TryGetValue(itemId, out var einer)
                               ? [(itemId, einer)]
                               : [];

                return [.. eintraege.Select(e => (e.Key, e.Value))];

            }

        }

        /// <summary>Die Knoten, in denen dieses Konto etwas veröffentlicht hat.</summary>
        public IReadOnlyCollection<String> PepNodes
        {
            get { lock (_lock) return [.. _pepNodes.Keys]; }
        }

        /// <summary>
        /// Gibt es diesen Knoten?
        /// </summary>
        /// <remarks>
        /// <b>Etwas anderes als „hat Einträge".</b> Ein angelegter Knoten
        /// existiert, bevor irgendetwas darin steht, und ein Knoten ohne
        /// Ablage bekommt nie welche - beide muss man abonnieren können, sonst
        /// wäre das Anlegen folgenlos.
        /// </remarks>
        public Boolean PepNodeExists(String node)
        {
            lock (_lock)
                return _pepNodeConfigs.ContainsKey(node) || _pepNodes.ContainsKey(node);
        }

        /// <summary>
        /// Legt einen Knoten an (XEP-0060, Abschnitt 8.1).
        /// </summary>
        /// <returns>
        /// false, wenn es ihn schon gibt - <c>&lt;conflict/&gt;</c>. Ein
        /// zweites Anlegen stillschweigend gelten zu lassen hiesse, eine
        /// bestehende Einstellung durch eine neue zu ersetzen, ohne dass
        /// jemand danach gefragt hat.
        /// </returns>
        public Boolean CreatePepNode(String node, PubSubNodeConfiguration? configuration = null)
        {

            lock (_lock)
            {

                if (PepNodeExists(node))
                    return false;

                _pepNodeConfigs[node] = configuration ?? PubSubNodeConfiguration.Default;

                return true;

            }

        }

        /// <summary>
        /// Stellt einen bestehenden Knoten ein (XEP-0060, Abschnitt 8.2).
        /// </summary>
        /// <returns>false, wenn es den Knoten nicht gibt.</returns>
        public Boolean ConfigurePepNode(String node, PubSubNodeConfiguration configuration)
        {

            lock (_lock)
            {

                if (!PepNodeExists(node))
                    return false;

                _pepNodeConfigs[node] = configuration;

                // Eine kleinere Grenze gilt sofort und nicht erst beim nächsten
                // Mal: Wer sie setzt, will nicht so viele aufbewahrt wissen -
                // und der Bestand ist genau das, was aufbewahrt wird.
                if (_pepNodes.TryGetValue(node, out var eintraege))
                    while (eintraege.Count > configuration.MaxItems)
                        eintraege.Remove(eintraege.Keys.First());

                return true;

            }

        }

        /// <summary>
        /// Die Einstellungen eines Knotens, oder null, wenn es ihn nicht gibt.
        /// </summary>
        public PubSubNodeConfiguration? PepNodeConfiguration(String node)
        {
            lock (_lock)
                return _pepNodeConfigs.TryGetValue(node, out var einstellung)
                           ? einstellung
                           : _pepNodes.ContainsKey(node)
                                 ? PubSubNodeConfiguration.Default
                                 : null;
        }

        /// <summary>
        /// Legt ein Abonnement an und gibt seine Kennung zurück (XEP-0060,
        /// Abschnitt 6.1).
        /// </summary>
        /// <remarks>
        /// <b>Jedes <c>subscribe</c> ist ein eigenes Abonnement</b>, auch das
        /// zweite desselben JIDs auf denselben Knoten. XEP-0060 sieht das
        /// ausdrücklich vor - dafür gibt es die <c>subid</c> überhaupt.
        ///
        /// Der Fall ist nicht ausgedacht: Er entsteht von selbst, wenn ein
        /// Client neu startet und wieder abonniert, ohne seine alte Kennung zu
        /// kennen. Von da an ist jedes Abbestellen ohne Kennung zweideutig,
        /// und genau das muss der Dienst dann auch sagen.
        ///
        /// <b>Was hier fehlt</b>, ist der Grund, aus dem sich zwei
        /// Abonnements sonst unterscheiden: die Konfiguration je Abonnement
        /// (Abschnitt 6.3). Ohne sie bringt ein zweites nichts ein als eine
        /// zweite Zustellung - der Server muss trotzdem richtig antworten,
        /// wenn es eines gibt.
        /// </remarks>
        public String AddPepSubscription(String node, String subscriberBareJid)
        {

            lock (_lock)
            {

                if (!_pepSubscriptions.TryGetValue(node, out var abonnements))
                    _pepSubscriptions[node] = abonnements = [];

                var subId = Guid.NewGuid().ToString("N")[..12];

                abonnements.Add(new PepSubscription(subscriberBareJid,
                                                    subId,
                                                    new PubSubSubscriptionOptions()));

                return subId;

            }

        }

        /// <summary>
        /// Sucht das gemeinte Abonnement heraus.
        /// </summary>
        /// <param name="subId">
        /// Die Kennung aus der Zusage, oder null. Sie darf fehlen, solange es
        /// nur eines gibt (XEP-0060, Abschnitt 6.2.3.1); gibt es mehrere, ist
        /// sie die einzige Auskunft darüber, welches gemeint ist.
        /// </param>
        /// <remarks>
        /// Eine Stelle für zwei Fragen: Abbestellen und Einstellen suchen
        /// dasselbe. Nur die Fehlermeldung darauf unterscheidet sich, und die
        /// baut der Aufrufer.
        /// </remarks>
        public PepSubscriptionResult FindPepSubscription(String              node,
                                                         String              subscriberBareJid,
                                                         String?             subId,
                                                         out PepSubscription?  subscription)
        {

            subscription = null;

            lock (_lock)
            {

                if (!_pepSubscriptions.TryGetValue(node, out var abonnements))
                    return PepSubscriptionResult.NotSubscribed;

                var seine = abonnements.FindAll(
                                a => String.Equals(a.Jid, subscriberBareJid, StringComparison.OrdinalIgnoreCase));

                if (seine.Count == 0)
                    return PepSubscriptionResult.NotSubscribed;

                if (subId is null && seine.Count > 1)
                    return PepSubscriptionResult.SubIdRequired;

                subscription = subId is null
                                   ? seine[0]
                                   : seine.Find(a => String.Equals(a.SubId, subId, StringComparison.Ordinal));

                return subscription is null
                           ? PepSubscriptionResult.WrongSubId
                           : PepSubscriptionResult.Ok;

            }

        }

        /// <summary>
        /// Beendet ein Abonnement (XEP-0060, Abschnitt 6.2).
        /// </summary>
        public PepSubscriptionResult RemovePepSubscription(String   node,
                                                           String   subscriberBareJid,
                                                           String?  subId = null)
        {

            lock (_lock)
            {

                var befund = FindPepSubscription(node, subscriberBareJid, subId, out var abonnement);

                if (befund != PepSubscriptionResult.Ok)
                    return befund;

                var abonnements = _pepSubscriptions[node];

                abonnements.Remove(abonnement!);

                if (abonnements.Count == 0)
                    _pepSubscriptions.Remove(node);

                return PepSubscriptionResult.Ok;

            }

        }

        /// <summary>
        /// Beendet Abonnements auf Anweisung des Eigentümers (XEP-0060,
        /// Abschnitt 8.8.2).
        /// </summary>
        /// <param name="subId">
        /// Ein bestimmtes Abonnement, oder null für alle dieses JIDs an diesem
        /// Knoten.
        /// </param>
        /// <returns>
        /// Die beendeten Abonnements - eine leere Liste, wenn es keines gab.
        /// Nicht die Zahl: Wer den Abonnenten benachrichtigen will, muss
        /// wissen, welche Kennung erloschen ist.
        /// </returns>
        /// <remarks>
        /// <b>Ohne Kennung gehen alle, und das ist kein Widerspruch zu
        /// Abschnitt 6.2.3.1.</b> Dort muss der Abonnent sagen, welches seiner
        /// Abonnements er meint, weil die anderen seine bleiben sollen. Hier
        /// meint der Eigentümer den Menschen und nicht die Buchführung: Eines
        /// stehen zu lassen hiesse, die Anweisung zur Hälfte auszuführen - und
        /// der Betroffene bekäme weiter alles.
        /// </remarks>
        public IReadOnlyList<PepSubscription> RemovePepSubscriptions(String   node,
                                                                    String   subscriberBareJid,
                                                                    String?  subId = null)
        {

            lock (_lock)
            {

                if (!_pepSubscriptions.TryGetValue(node, out var abonnements))
                    return [];

                var betroffen = abonnements.FindAll(
                                    a => String.Equals(a.Jid, subscriberBareJid, StringComparison.OrdinalIgnoreCase) &&
                                         (subId is null || String.Equals(a.SubId, subId, StringComparison.Ordinal)));

                foreach (var eines in betroffen)
                    abonnements.Remove(eines);

                if (abonnements.Count == 0)
                    _pepSubscriptions.Remove(node);

                return betroffen;

            }

        }

        /// <summary>
        /// Stellt ein Abonnement ein (XEP-0060, Abschnitt 6.3).
        /// </summary>
        public PepSubscriptionResult SetPepSubscriptionOptions(String                     node,
                                                               String                     subscriberBareJid,
                                                               String?                    subId,
                                                               PubSubSubscriptionOptions  options)
        {

            lock (_lock)
            {

                var befund = FindPepSubscription(node, subscriberBareJid, subId, out var abonnement);

                if (befund != PepSubscriptionResult.Ok)
                    return befund;

                var abonnements = _pepSubscriptions[node];

                abonnements[abonnements.IndexOf(abonnement!)] = abonnement! with { Options = options };

                return PepSubscriptionResult.Ok;

            }

        }

        /// <summary>
        /// Die Abonnements dieses Knotens.
        /// </summary>
        /// <remarks>
        /// Derselbe JID kann mehrfach vorkommen. Wer die Empfänger will und
        /// nicht die Abonnements, muss also selbst zusammenfassen - und wer
        /// zustellt, gerade nicht: Jedes Abonnement ist eine eigene Zusage,
        /// mit eigener Einstellung.
        /// </remarks>
        public IReadOnlyList<PepSubscription> PepSubscriptions(String node)
        {
            lock (_lock)
                return _pepSubscriptions.TryGetValue(node, out var abonnements)
                           ? [.. abonnements]
                           : [];
        }

        /// <summary>
        /// Was jemand an einem Knoten ist (XEP-0060, Abschnitt 4.1).
        /// </summary>
        /// <remarks>
        /// <b>Der Eigentümer wird nicht nachgeschlagen, sondern erkannt.</b>
        /// Ein PEP-Knoten gehört dem Konto, in dem er steht - das ist keine
        /// Eintragung, die fehlen könnte, sondern eine Tatsache über die
        /// Adresse.
        /// </remarks>
        public PubSubAffiliation PepAffiliationOf(String node, String bareJid)
        {

            if (String.Equals(BareJid, bareJid, StringComparison.OrdinalIgnoreCase))
                return PubSubAffiliation.Owner;

            lock (_lock)
                return _pepAffiliations.TryGetValue(node, out var rollen) &&
                       rollen.TryGetValue(bareJid, out var rolle)
                           ? rolle
                           : PubSubAffiliation.None;

        }

        /// <summary>
        /// Setzt eine Rolle (XEP-0060, Abschnitt 8.9.2).
        /// </summary>
        /// <returns>
        /// false, wenn es den Knoten nicht gibt oder wenn jemand die Rolle des
        /// Eigentümers ändern will - beides ist kein Formfehler, sondern eine
        /// Anweisung, die es nicht gibt.
        /// </returns>
        /// <remarks>
        /// <c>None</c> löscht den Eintrag, statt ihn auf einen Wert zu setzen:
        /// „keine Rolle" ist die Abwesenheit einer Rolle und nicht eine unter
        /// mehreren.
        /// </remarks>
        public Boolean SetPepAffiliation(String node, String bareJid, PubSubAffiliation affiliation)

            => SetPepAffiliation(node, bareJid, affiliation, out _);

        /// <summary>
        /// Dasselbe, und nennt die Abonnements, die dabei erloschen sind.
        /// </summary>
        /// <param name="endedSubscriptions">
        /// Was der Ausschluss beendet hat - leer bei jeder anderen Rolle.
        /// </param>
        /// <remarks>
        /// <b>Warum die Auskunft hierher gehört und nicht zum Aufrufer.</b> Wer
        /// den Betroffenen benachrichtigen will, muss wissen, welche Kennungen
        /// erloschen sind. Sie sich vorher selbst zusammenzusuchen hiesse,
        /// dieselbe Frage ein zweites Mal zu beantworten - und die zweite
        /// Antwort wäre die ungenauere, weil zwischen Nachsehen und Setzen
        /// etwas dazwischenkommen kann.
        /// </remarks>
        public Boolean SetPepAffiliation(String                            node,
                                         String                            bareJid,
                                         PubSubAffiliation                 affiliation,
                                         out IReadOnlyList<PepSubscription>  endedSubscriptions)
        {

            endedSubscriptions = [];

            if (String.Equals(BareJid, bareJid, StringComparison.OrdinalIgnoreCase) ||
                affiliation == PubSubAffiliation.Owner)
            {
                return false;
            }

            lock (_lock)
            {

                if (!PepNodeExists(node))
                    return false;

                if (affiliation == PubSubAffiliation.None)
                {

                    if (_pepAffiliations.TryGetValue(node, out var bestand))
                    {

                        bestand.Remove(bareJid);

                        if (bestand.Count == 0)
                            _pepAffiliations.Remove(node);

                    }

                    return true;

                }

                if (!_pepAffiliations.TryGetValue(node, out var rollen))
                    _pepAffiliations[node] = rollen = new Dictionary<String, PubSubAffiliation>(StringComparer.OrdinalIgnoreCase);

                rollen[bareJid] = affiliation;

                // XEP-0060, Abschnitt 8.9.4: Wer ausgeschlossen wird, verliert
                // seine Abonnements. Ihn nur an neuen zu hindern hiesse, den
                // Ausschluss von dem Zufall abhängig zu machen, ob er vorher
                // schon da war.
                //
                // Über denselben Weg wie die Anweisung des Eigentümers
                // (Abschnitt 8.8.2): Zwei Stellen, die Abonnements beenden,
                // beenden sie irgendwann verschieden.
                if (affiliation == PubSubAffiliation.Outcast)
                    endedSubscriptions = RemovePepSubscriptions(node, bareJid);

                return true;

            }

        }

        /// <summary>
        /// Alle Rollen an einem Knoten, den Eigentümer eingeschlossen
        /// (XEP-0060, Abschnitt 8.9.1).
        /// </summary>
        public IReadOnlyList<(String Jid, PubSubAffiliation Affiliation)> PepAffiliations(String node)
        {
            lock (_lock)
                return [(BareJid, PubSubAffiliation.Owner),
                        .. (_pepAffiliations.TryGetValue(node, out var rollen)
                                ? rollen.Select(r => (r.Key, r.Value))
                                : [])];
        }

        /// <summary>
        /// Die Rollen eines JIDs über alle Knoten dieses Kontos (XEP-0060,
        /// Abschnitt 5.7).
        /// </summary>
        public IReadOnlyList<(String Node, PubSubAffiliation Affiliation)> PepAffiliationsOf(String bareJid)
        {

            lock (_lock)
            {

                // Dem Eigentümer gehören alle seine Knoten - auch die, an
                // denen nie jemand eine Rolle eingetragen hat.
                if (String.Equals(BareJid, bareJid, StringComparison.OrdinalIgnoreCase))
                    return [.. _pepNodeConfigs.Keys
                               .Concat(_pepNodes.Keys)
                               .Distinct(StringComparer.Ordinal)
                               .Select(n => (n, PubSubAffiliation.Owner))];

                return [.. _pepAffiliations
                           .Where (n => n.Value.ContainsKey(bareJid))
                           .Select(n => (n.Key, n.Value[bareJid]))];

            }

        }

        /// <summary>
        /// Alle Abonnements eines JIDs über alle Knoten dieses Kontos
        /// (XEP-0060, Abschnitt 5.6).
        /// </summary>
        /// <remarks>
        /// <b>Die Frage, die sich ein Client nicht selbst beantworten kann.</b>
        /// Seine Buchführung steht im Arbeitsspeicher und ist nach einem
        /// Verbindungsabriss leer; die Abonnements bestehen weiter, denn sie
        /// stehen hier am Konto. Ohne diesen Weg kennt er danach keine einzige
        /// Kennung mehr - und kann bei mehreren Abonnements auf denselben
        /// Knoten keines davon beenden.
        /// </remarks>
        public IReadOnlyList<(String Node, PepSubscription Subscription)> PepSubscriptionsOf(String subscriberBareJid)
        {
            lock (_lock)
                return [.. _pepSubscriptions
                           .SelectMany(knoten => knoten.Value.Select(a => (knoten.Key, a)))
                           .Where(e => String.Equals(e.a.Jid, subscriberBareJid, StringComparison.OrdinalIgnoreCase))];
        }

        #endregion

        /// <summary>
        /// Bewahrt eine Nachricht auf, bis das Konto wieder erreichbar ist.
        /// </summary>
        /// <param name="stanza">Die vollständige Stanza mit gesetztem <c>from</c>.</param>
        /// <param name="storedAt">Der Zeitpunkt des Eingangs.</param>
        /// <param name="maxStored">
        /// Obergrenze für die Zahl aufbewahrter Nachrichten je Konto.
        /// </param>
        /// <returns>
        /// false, wenn die Grenze erreicht ist - dann ist die Nachricht nicht
        /// aufbewahrt, und der Absender soll es erfahren.
        /// </returns>
        /// <remarks>
        /// Ist die Grenze erreicht, wird die neue Nachricht abgewiesen und
        /// keine aufbewahrte verdrängt. Beides verliert eine Nachricht, aber
        /// nur eines davon sagt es jemandem: Wer abweist, kann dem Absender
        /// <c>&lt;service-unavailable/&gt;</c> antworten - RFC 6121,
        /// Abschnitt 8.5.2.2.1 stellt genau diese beiden Wege nebeneinander.
        /// Wer verdrängt, wirft eine Nachricht weg, von der der Absender
        /// annimmt, sie liege bereit, und der Empfänger nie erfährt, dass es
        /// sie gab.
        /// </remarks>
        public Boolean StoreOfflineMessage(String          stanza,
                                           DateTimeOffset  storedAt,
                                           Int32           maxStored = Int32.MaxValue)
        {

            lock (_lock)
            {

                if (_offlineMessages.Count >= maxStored)
                    return false;

                _offlineMessages.Add(new OfflineMessage(stanza, storedAt));

            }

            OnChanged?.Invoke(this);

            return true;

        }

        /// <summary>
        /// Gibt die aufbewahrten Nachrichten heraus und leert die Ablage.
        /// </summary>
        /// <remarks>
        /// Herausgeben und Leeren in einem Schritt, unter derselben Sperre:
        /// Zwei Resourcen, die sich gleichzeitig anmelden, bekämen die Ablage
        /// sonst beide zu sehen, und der Nutzer läse alles doppelt.
        ///
        /// Anders als eine aufbewahrte Subscription-Anfrage
        /// (<see cref="PendingSubscriptionRequests"/>) bleibt hier nichts
        /// stehen. Die Anfrage wird nachgereicht, bis sie beantwortet ist -
        /// eine Nachricht ist mit dem Zustellen erledigt, und wer sie bei jeder
        /// Anmeldung erneut vorgelegt bekäme, könnte sie nie loswerden.
        /// </remarks>
        public IReadOnlyList<OfflineMessage> TakeOfflineMessages()
        {

            List<OfflineMessage> entnommen;

            lock (_lock)
            {

                if (_offlineMessages.Count == 0)
                    return [];

                entnommen = _offlineMessages.ToList();
                _offlineMessages.Clear();

            }

            OnChanged?.Invoke(this);

            return entnommen;

        }

        /// <summary>
        /// Darf dieser Kontakt die Presence dieses Kontos sehen?
        /// </summary>
        /// <remarks>
        /// RFC 6121, Abschnitt 4.2.2: das ist genau bei <c>from</c> und
        /// <c>both</c> der Fall. Die Richtung ist leicht zu verwechseln - ein
        /// <c>to</c> heisst, dass <b>dieses Konto</b> die Presence des
        /// Kontakts sieht, und gäbe die eigene an genau die falsche Hälfte des
        /// Rosters.
        /// </remarks>
        public Boolean IsPresenceSubscriber(String bareJid)
            => SubscriptionOf(bareJid) is "from" or "both";

        /// <summary>
        /// Bekommt dieses Konto die Presence des Kontakts - also <c>to</c> oder
        /// <c>both</c>?
        /// </summary>
        public Boolean ReceivesPresenceFrom(String bareJid)
            => SubscriptionOf(bareJid) is "to" or "both";

        /// <summary>
        /// Der Subscription-Zustand zu diesem Kontakt, oder null, wenn er nicht
        /// im Roster steht.
        /// </summary>
        public String? SubscriptionOf(String bareJid)
        {
            lock (_lock)
                return _roster.FirstOrDefault(e => String.Equals(e.Jid, bareJid, StringComparison.OrdinalIgnoreCase))
                             ?.Subscription;
        }

    }

}
