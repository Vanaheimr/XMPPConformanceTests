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

using System.Text.Json;
using System.Text.Json.Serialization;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP.Server
{

    /// <summary>
    /// Ein Kontenspeicher in einer JSON-Datei.
    /// </summary>
    /// <remarks>
    /// Eine Datei für alle Konten, bei jeder Änderung vollständig neu
    /// geschrieben. Das ist O(n) je Speichervorgang und für ein paar tausend
    /// Konten völlig ausreichend; wer mehr braucht, braucht ohnehin eine
    /// Datenbank und damit eine andere Implementierung dieser Schnittstelle.
    ///
    /// Geschrieben wird über eine Nebendatei, die anschliessend an ihren Platz
    /// verschoben wird. Bricht der Vorgang ab, steht die alte Fassung noch
    /// vollständig da - ein direkt beschriebenes Ziel wäre nach einem
    /// Stromausfall mitten im Schreiben abgeschnitten und damit unlesbar.
    ///
    /// Was hier <b>nicht</b> passiert: die Datei wird nicht verschlüsselt und
    /// ihre Zugriffsrechte werden nicht gesetzt. Die abgelegten Schlüssel sind
    /// keine Passwörter, aber sie erlauben einem Angreifer, Anmeldungen zu
    /// prüfen. Die Datei gehört an einen Ort, an den nur der Serverprozess
    /// kommt.
    /// </remarks>
    public sealed class FileAccountStore : IXMPPAccountStore
    {

        #region Data

        private readonly String _path;
        private readonly Lock _lock = new();

        private static readonly JsonSerializerOptions _options = new() {
            WriteIndented         = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        #endregion

        #region Properties

        /// <summary>Die Datei, in der die Konten liegen.</summary>
        public String Path => _path;

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Legt einen Speicher an der angegebenen Datei an. Die Datei muss
        /// noch nicht existieren.
        /// </summary>
        public FileAccountStore(String path)
        {
            _path = System.IO.Path.GetFullPath(path);
        }

        #endregion


        #region Load()

        public IEnumerable<XMPPAccount> Load()
        {

            lock (_lock)
                return LesenOhneSperre().Select(ZuKonto).ToList();

        }

        #endregion

        #region Save(account)

        public void Save(XMPPAccount account)
        {

            lock (_lock)
            {

                var konten = LesenOhneSperre()
                                 .Where(k => !String.Equals(k.BareJid, account.BareJid, StringComparison.OrdinalIgnoreCase))
                                 .ToList();

                konten.Add(ZuDatensatz(account));

                SchreibenOhneSperre(konten);

            }

        }

        #endregion

        #region Delete(bareJid)

        public void Delete(String bareJid)
        {

            lock (_lock)
            {

                var konten = LesenOhneSperre()
                                 .Where(k => !String.Equals(k.BareJid, bareJid, StringComparison.OrdinalIgnoreCase))
                                 .ToList();

                SchreibenOhneSperre(konten);

            }

        }

        #endregion


        #region (private) Datei lesen und schreiben

        private List<GespeichertesKonto> LesenOhneSperre()
        {

            if (!File.Exists(_path))
                return [];

            var json = File.ReadAllText(_path);

            if (json.Length == 0)
                return [];

            return JsonSerializer.Deserialize<GespeicherteKonten>(json, _options)?.Accounts ?? [];

        }

        private void SchreibenOhneSperre(List<GespeichertesKonto> konten)
        {

            var verzeichnis = System.IO.Path.GetDirectoryName(_path);

            if (!String.IsNullOrEmpty(verzeichnis))
                Directory.CreateDirectory(verzeichnis);

            var json      = JsonSerializer.Serialize(new GespeicherteKonten(1, konten), _options);
            var nebenbei  = _path + ".neu";

            File.WriteAllText(nebenbei, json);
            File.Move(nebenbei, _path, overwrite: true);

        }

        #endregion

        #region (private) Umwandlung

        private static GespeichertesKonto ZuDatensatz(XMPPAccount account)
        {

            var credentials = account.Credentials;

            return new GespeichertesKonto(

                       account.BareJid,

                       new GespeicherteZugangsdaten(
                           Convert.ToBase64String(credentials.Salt),
                           credentials.IterationCount,
                           credentials.Mechanisms.ToDictionary(
                               m => m.ToString(),
                               m => new GespeicherteSchluessel(
                                        Convert.ToBase64String(credentials.KeysOf(m).StoredKey),
                                        Convert.ToBase64String(credentials.KeysOf(m).ServerKey)))),

                       [.. account.Roster.Select(e => new GespeicherterKontakt(e.Jid, e.Name, e.Subscription,
                                                                              e.Ask, e.Approved))],

                       account.PendingSubscriptionRequests.Count > 0
                           ? new Dictionary<String, String>(account.PendingSubscriptionRequests)
                           : null

                   );

        }

        private static XMPPAccount ZuKonto(GespeichertesKonto datensatz)
        {

            var credentials = XMPPCredentials.FromStored(
                                  Convert.FromBase64String(datensatz.Credentials.Salt),
                                  datensatz.Credentials.IterationCount,
                                  datensatz.Credentials.Keys.ToDictionary(
                                      k => Enum.Parse<SCRAMMechanism>(k.Key),
                                      k => new SCRAMKeys(Convert.FromBase64String(k.Value.StoredKey),
                                                         Convert.FromBase64String(k.Value.ServerKey))));

            var account = new XMPPAccount(datensatz.BareJid, credentials);

            foreach (var kontakt in datensatz.Roster)
                account.SetRosterEntry(new RosterEntry(kontakt.Jid,
                                                       kontakt.Name,
                                                       kontakt.Subscription,
                                                       kontakt.Ask,
                                                       kontakt.Approved));

            // RFC 6121, Abschnitt 3.1.3: eine aufbewahrte Anfrage soll
            // zugestellt werden, sobald der Kontakt sich das nächste Mal
            // anmeldet - und einen Serverneustart zu überstehen gehört dazu.
            // Ohne das wäre "aufbewahrt" nur ein anderes Wort für "bis zum
            // nächsten Neustart".
            foreach (var anfrage in datensatz.PendingSubscriptions ?? [])
                account.RememberSubscriptionRequest(anfrage.Key, anfrage.Value);

            return account;

        }

        #endregion

        #region (private) Dateiformat

        /// <param name="Version">
        /// Damit ein späteres Format erkennen kann, was es vor sich hat.
        /// </param>
        private sealed record GespeicherteKonten(Int32                     Version,
                                                 List<GespeichertesKonto>  Accounts);

        /// <param name="PendingSubscriptions">
        /// Aufbewahrte Subscription-Anfragen, nach Absender (RFC 6121,
        /// Abschnitt 3.1.3). Fehlt in älteren Dateien und ist dann null.
        /// </param>
        private sealed record GespeichertesKonto(String                        BareJid,
                                                 GespeicherteZugangsdaten      Credentials,
                                                 List<GespeicherterKontakt>    Roster,
                                                 Dictionary<String, String>?   PendingSubscriptions);

        private sealed record GespeicherteZugangsdaten(String                                    Salt,
                                                       Int32                                     IterationCount,
                                                       Dictionary<String, GespeicherteSchluessel> Keys);

        private sealed record GespeicherteSchluessel(String StoredKey,
                                                     String ServerKey);

        private sealed record GespeicherterKontakt(String   Jid,
                                                   String?  Name,
                                                   String   Subscription,
                                                   String?  Ask,
                                                   Boolean  Approved);

        #endregion

    }

}
