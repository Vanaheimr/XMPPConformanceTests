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
using System.Xml;
using System.Xml.Linq;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP.Server
{

    /// <summary>
    /// Ein Server-zu-Server-Stream - die Protokollschicht zwischen zwei
    /// Servern, ohne Transport darunter.
    /// </summary>
    /// <remarks>
    /// Diese Klasse kennt weder Sockets noch WebSocket-Rahmen: sie bekommt
    /// eingehende Rahmen als Zeichenketten gereicht und schickt ausgehende über
    /// eine Funktion hinaus. Genau deshalb steht sie zuerst - TCP und WebSocket
    /// sind darunter nur zwei Rahmungen derselben Schicht, und was sie
    /// gemeinsam haben (Handshake, Absenderprüfung, Stream-Fehler,
    /// Lebenszyklus) soll nicht zweimal entstehen.
    ///
    /// <b>Der Stream ist gerichtet</b>, wie RFC 6120, Abschnitt 4.1 es
    /// beschreibt: über ihn fliessen Stanzas nur vom Initiator zum Empfänger.
    /// Wer antworten will, baut seinen eigenen Stream in die Gegenrichtung
    /// auf. Das ist der Grund, warum ein Stream ohne
    /// <c>deliverStanza</c>-Funktion eingehende Stanzas nicht etwa zustellt,
    /// sondern über <see cref="OnStanzaRefused"/> meldet und verwirft. Beides
    /// über eine Verbindung zu führen wäre XEP-0288 (Bidirectional Server-to-
    /// Server Connections) und müsste ausgehandelt werden.
    ///
    /// <b>Was diese Schicht nicht leistet:</b> sie <i>glaubt</i> der
    /// Gegenstelle ihre Domain. Das <c>from</c> im <c>&lt;open/&gt;</c> ist
    /// eine Behauptung; belegt wird sie erst durch Dialback (XEP-0220) oder
    /// SASL-EXTERNAL. Bis dahin ist ein echter Transport hier genau so viel
    /// wert wie <see cref="DirectServerLinks"/> - nur eben über ein Netz.
    /// </remarks>
    public sealed class S2SStream
    {

        #region Data

        private readonly Func<String, CancellationToken, Task>            sendFrame;
        private readonly Func<String, String, Task<RemoteStanzaResult>>?  deliverStanza;
        private readonly Lock                                             dataLock  = new();

        /// <summary>
        /// Das Dialback-Geheimnis dieses Servers, oder null. Gebraucht in zwei
        /// Rollen: der aufbauende Server erzeugt damit seinen Schlüssel, der
        /// autoritative rechnet ihn damit nach.
        /// </summary>
        private readonly String? secret;

        /// <summary>
        /// Lässt einen vorgelegten Dialback-Schlüssel beim autoritativen Server
        /// der Absenderdomain prüfen - Parameter sind Absenderdomain,
        /// Stream-ID und Schlüssel.
        /// </summary>
        /// <remarks>
        /// Steht hier als Funktion und nicht als Implementierung, weil die
        /// Prüfung eine <b>zweite Verbindung</b> braucht und diese Schicht
        /// keine aufbauen kann. Genau an dieser Stelle entscheidet sich, ob
        /// Dialback etwas wert ist: die Adresse, an die gefragt wird, darf
        /// nicht von dem stammen, der gerade geprüft werden soll.
        /// </remarks>
        private readonly Func<String, String, String, Task<Boolean>>? verifyKey;

        /// <summary>
        /// Wird erfüllt, sobald Dialback durch ist - und abgebrochen, wenn der
        /// Stream vorher endet.
        /// </summary>
        private readonly TaskCompletionSource dialbackDone =
            new (TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Wird erfüllt, sobald der Handshake steht - und abgebrochen, wenn der
        /// Stream vorher endet, damit niemand auf ein <c>&lt;open/&gt;</c>
        /// wartet, das nicht mehr kommen kann.
        /// </summary>
        private readonly TaskCompletionSource openHandshake =
            new (TaskCreationOptions.RunContinuationsAsynchronously);

        #endregion

        #region Properties

        /// <summary>Der Namensraum der Rahmung (RFC 7395, Abschnitt 3.1).</summary>
        public const String FramingNamespace = "urn:ietf:params:xml:ns:xmpp-framing";

        /// <summary>Der Namensraum der Stream-Ebene (RFC 6120, Abschnitt 4.8.2).</summary>
        public const String StreamNamespace = "http://etherx.jabber.org/streams";

        /// <summary>Der Namensraum der Stream-Fehlerbedingungen (RFC 6120, Abschnitt 4.9.2).</summary>
        public const String StreamErrorNamespace = "urn:ietf:params:xml:ns:xmpp-streams";

        /// <summary>Die eigene Domain.</summary>
        public String LocalDomain { get; }

        /// <summary>
        /// Die Domain der Gegenstelle. Beim Initiator von Anfang an bekannt,
        /// beim Empfänger erst nach ihrem <c>&lt;open/&gt;</c> - und dann als
        /// Behauptung, nicht als Beleg.
        /// </summary>
        public String? RemoteDomain { get; private set; }

        /// <summary>Hat dieser Server den Stream aufgebaut?</summary>
        public Boolean IsInitiator { get; }

        /// <summary>
        /// Die Kennung des Streams. Der Empfänger vergibt sie, der Initiator
        /// liest sie aus dem <c>&lt;open/&gt;</c> der Gegenseite (RFC 7395,
        /// Abschnitt 3.4). Dialback hängt daran.
        /// </summary>
        public String? StreamId { get; private set; }

        /// <summary>Steht der Handshake?</summary>
        public Boolean IsOpen { get; private set; }

        /// <summary>Ist der Stream beendet?</summary>
        public Boolean IsClosed { get; private set; }

        /// <summary>
        /// Verlangt dieser Stream Dialback, bevor Stanzas fliessen dürfen?
        /// </summary>
        /// <remarks>
        /// XEP-0220, Abschnitt 1: der annehmende Server "does not process XMPP
        /// stanzas over the connection until it has verified the initiating
        /// server's identity". Ohne Dialback bleibt es beim Stand von S4b-2:
        /// die Domain der Gegenstelle ist behauptet, nicht belegt. Ein
        /// Transport, der das ohne Ersatz abschaltet, macht genau das Loch
        /// auf, gegen das die Absenderprüfung existiert - zulässig ist es nur
        /// dort, wo die Identität anders feststeht (SASL-EXTERNAL) oder wo
        /// gar kein Netz dazwischen liegt.
        /// </remarks>
        public Boolean RequiresDialback { get; }

        /// <summary>
        /// Ist die Domain der Gegenstelle belegt? Bei einem Stream ohne
        /// Dialback bleibt das dauerhaft false - dann ist
        /// <see cref="RequiresDialback"/> ebenfalls false und niemand fragt.
        /// </summary>
        public Boolean IsAuthenticated { get; private set; }

        #endregion

        #region Events

        /// <summary>
        /// Eine eingehende Stanza wurde nicht zugestellt - mit dem Grund.
        /// </summary>
        public event Action<String>? OnStanzaRefused;

        /// <summary>
        /// Der Stream ist beendet, mit dem Grund oder null bei einem
        /// ordentlichen <c>&lt;close/&gt;</c>.
        /// </summary>
        public event Action<String?>? OnClosed;

        #endregion

        #region Constructor(s)

        private S2SStream(String                                           localDomain,
                          String?                                          remoteDomain,
                          Boolean                                          isInitiator,
                          Func<String, CancellationToken, Task>            sendFrame,
                          Func<String, String, Task<RemoteStanzaResult>>?  deliverStanza,
                          String?                                          secret,
                          Func<String, String, String, Task<Boolean>>?     verifyKey,
                          Boolean                                          requiresDialback)
        {

            LocalDomain         = localDomain;
            RemoteDomain        = remoteDomain;
            IsInitiator         = isInitiator;
            RequiresDialback    = requiresDialback;

            this.sendFrame      = sendFrame;
            this.deliverStanza  = deliverStanza;
            this.secret         = secret;
            this.verifyKey      = verifyKey;

        }

        #endregion

        #region (static) Initiate(localDomain, remoteDomain, sendFrame, secret)

        /// <summary>
        /// Der ausgehende Stream: er trägt Stanzas hinaus und nimmt keine
        /// entgegen.
        /// </summary>
        /// <param name="localDomain">Die eigene Domain.</param>
        /// <param name="remoteDomain">Die Domain, zu der aufgebaut wird.</param>
        /// <param name="sendFrame">Schickt einen Rahmen über den Transport.</param>
        /// <param name="secret">
        /// Das eigene Dialback-Geheimnis. Ist es gesetzt, weist sich dieser
        /// Stream nach dem Handshake von sich aus mit
        /// <c>&lt;db:result/&gt;</c> aus und trägt erst danach Stanzas.
        /// </param>
        public static S2SStream Initiate(String                                localDomain,
                                         String                                remoteDomain,
                                         Func<String, CancellationToken, Task> sendFrame,
                                         String?                               secret   = null)

            => new (localDomain,
                    remoteDomain,
                    isInitiator:       true,
                    sendFrame:         sendFrame,
                    deliverStanza:     null,
                    secret:            secret,
                    verifyKey:         null,
                    requiresDialback:  secret is not null);

        #endregion

        #region (static) Accept(localDomain, sendFrame, deliverStanza, secret, verifyKey)

        /// <summary>
        /// Der eingehende Stream: er nimmt Stanzas entgegen und schickt selbst
        /// nur Stream-Ebene.
        /// </summary>
        /// <param name="localDomain">Die eigene Domain.</param>
        /// <param name="sendFrame">Schickt einen Rahmen über den Transport.</param>
        /// <param name="deliverStanza">
        /// Übergibt eine eingehende Stanza samt der Domain, für die die
        /// Gegenstelle sprechen darf, an das Routing.
        /// </param>
        /// <param name="secret">
        /// Das eigene Dialback-Geheimnis - gebraucht in der Rolle des
        /// autoritativen Servers, um einen fremden
        /// <c>&lt;db:verify/&gt;</c> nachzurechnen.
        /// </param>
        /// <param name="verifyKey">
        /// Lässt einen vorgelegten Schlüssel beim autoritativen Server der
        /// Absenderdomain prüfen. Ist sie gesetzt, verlangt dieser Stream
        /// Dialback, bevor er Stanzas annimmt.
        /// </param>
        public static S2SStream Accept(String                                          localDomain,
                                       Func<String, CancellationToken, Task>           sendFrame,
                                       Func<String, String, Task<RemoteStanzaResult>>  deliverStanza,
                                       String?                                         secret      = null,
                                       Func<String, String, String, Task<Boolean>>?    verifyKey   = null)

            => new (localDomain,
                    remoteDomain:      null,
                    isInitiator:       false,
                    sendFrame:         sendFrame,
                    deliverStanza:     deliverStanza,
                    secret:            secret,
                    verifyKey:         verifyKey,
                    requiresDialback:  verifyKey is not null);

        #endregion

        #region (static) InitiateVerification(localDomain, remoteDomain, sendFrame)

        /// <summary>
        /// Der kurzlebige Stream, über den ein annehmender Server einen
        /// Dialback-Schlüssel beim autoritativen Server nachfragt
        /// (XEP-0220, Schritt 2 und 3).
        /// </summary>
        /// <remarks>
        /// Eigene Rolle und nicht bloss ein <see cref="Initiate"/> ohne
        /// Geheimnis: über ihn geht nie eine Stanza, er weist sich nicht aus
        /// und er gehört in keinen Verbindungs-Cache. Er wird aufgebaut,
        /// stellt eine Frage, bekommt eine Antwort und ist wieder weg.
        /// </remarks>
        public static S2SStream InitiateVerification(String                                localDomain,
                                                     String                                remoteDomain,
                                                     Func<String, CancellationToken, Task> sendFrame)

            => new (localDomain,
                    remoteDomain,
                    isInitiator:       true,
                    sendFrame:         sendFrame,
                    deliverStanza:     null,
                    secret:            null,
                    verifyKey:         null,
                    requiresDialback:  false);

        #endregion


        #region OpenAsync(CancellationToken)

        /// <summary>
        /// Schickt den Stream-Kopf. Nur der Initiator fängt an.
        /// </summary>
        public Task OpenAsync(CancellationToken cancellationToken = default)
        {

            if (!IsInitiator)
                throw new InvalidOperationException(
                          "Nur der Initiator öffnet den Stream; der Empfänger antwortet auf das <open/>.");

            return sendFrame(
                       $"<open xmlns='{FramingNamespace}' " +
                       $"from='{XmlEscaping.Escape(LocalDomain)}' " +
                       $"to='{XmlEscaping.Escape(RemoteDomain!)}' " +
                       "version='1.0'/>",
                       cancellationToken);

        }

        #endregion

        #region WaitUntilOpenAsync(Timeout, CancellationToken)

        /// <summary>
        /// Wartet auf das <c>&lt;open/&gt;</c> der Gegenstelle.
        /// </summary>
        /// <returns>false bei Zeitüberschreitung oder wenn der Stream vorher endete.</returns>
        public async Task<Boolean> WaitUntilOpenAsync(TimeSpan           Timeout,
                                                      CancellationToken  cancellationToken = default)
        {

            try
            {
                await openHandshake.Task.WaitAsync(Timeout, cancellationToken);
                return true;
            }
            catch (Exception)
            {
                return false;
            }

        }

        #endregion

        #region ProcessFrameAsync(frame, CancellationToken)

        /// <summary>
        /// Verarbeitet einen eingehenden Rahmen.
        /// </summary>
        /// <returns>false, wenn der Rahmen nicht verstanden wurde.</returns>
        public async Task<Boolean> ProcessFrameAsync(String             frame,
                                                     CancellationToken  cancellationToken = default)
        {

            if (frame.StartsWith("<open", StringComparison.Ordinal))
                return await ProcessOpenAsync(frame, cancellationToken);

            if (frame.StartsWith("<close", StringComparison.Ordinal))
            {
                MarkClosed(null);
                return true;
            }

            // RFC 6120, Abschnitt 4.9: nach einem Stream-Fehler ist der Stream
            // tot; eine Antwort darauf gibt es nicht.
            if (frame.StartsWith("<stream:error", StringComparison.Ordinal) ||
                frame.Contains(StreamErrorNamespace, StringComparison.Ordinal))
            {
                MarkClosed($"Stream-Fehler der Gegenstelle: {frame}");
                return true;
            }

            // Die Features des Empfängers. Dass Dialback angeboten wird, steht
            // dort drin; verlangt wird es hier aber unabhängig davon, weil ein
            // Angreifer die Ankündigung schlicht weglassen könnte.
            if (frame.StartsWith("<stream:features", StringComparison.Ordinal) ||
                frame.StartsWith("<features",        StringComparison.Ordinal))
            {
                return true;
            }

            if (IsDialback(frame, "result"))
                return await ProcessDialbackResultAsync(frame, cancellationToken);

            if (IsDialback(frame, "verify"))
                return await ProcessDialbackVerifyAsync(frame, cancellationToken);

            if (frame.StartsWith("<message",  StringComparison.Ordinal) ||
                frame.StartsWith("<presence", StringComparison.Ordinal) ||
                frame.StartsWith("<iq",       StringComparison.Ordinal))
            {
                return await ProcessStanzaAsync(frame, cancellationToken);
            }

            return false;

        }

        #endregion

        #region SendStanzaAsync(stanza, CancellationToken)

        /// <summary>
        /// Schickt eine Stanza über den Stream.
        /// </summary>
        /// <returns>false, wenn der Stream nicht (mehr) offen ist.</returns>
        public async Task<Boolean> SendStanzaAsync(String             stanza,
                                                   CancellationToken  cancellationToken = default)
        {

            lock (dataLock)
            {

                if (!IsOpen || IsClosed)
                    return false;

                // Ein Stream, der sich noch nicht ausgewiesen hat, trägt
                // nichts. Die Gegenstelle würde es ohnehin verwerfen.
                if (RequiresDialback && !IsAuthenticated)
                    return false;

            }

            await sendFrame(stanza, cancellationToken);

            return true;

        }

        #endregion

        #region WaitUntilAuthenticatedAsync(Timeout, CancellationToken)

        /// <summary>
        /// Wartet, bis Dialback durch ist.
        /// </summary>
        /// <returns>
        /// true auch dann sofort, wenn dieser Stream gar kein Dialback
        /// verlangt - dann gibt es nichts zu warten.
        /// </returns>
        public async Task<Boolean> WaitUntilAuthenticatedAsync(TimeSpan           Timeout,
                                                               CancellationToken  cancellationToken = default)
        {

            if (!RequiresDialback)
                return true;

            try
            {
                await dialbackDone.Task.WaitAsync(Timeout, cancellationToken);
                return true;
            }
            catch (Exception)
            {
                return false;
            }

        }

        #endregion

        #region CloseAsync(CancellationToken)

        /// <summary>
        /// Beendet den Stream ordentlich (RFC 7395, Abschnitt 3.6).
        /// </summary>
        public async Task CloseAsync(CancellationToken cancellationToken = default)
        {

            lock (dataLock)
            {
                if (IsClosed)
                    return;
            }

            try
            {
                await sendFrame($"<close xmlns='{FramingNamespace}'/>", cancellationToken);
            }
            catch (Exception)
            {
                // Die Verbindung ist schon weg - das Ergebnis ist dasselbe.
            }

            MarkClosed(null);

        }

        #endregion

        #region SendStreamErrorAsync(condition, text, CancellationToken)

        /// <summary>
        /// Beendet den Stream mit einem Fehler (RFC 6120, Abschnitt 4.9).
        /// </summary>
        /// <param name="condition">Bedingung aus Abschnitt 4.9.3, etwa <c>invalid-from</c>.</param>
        /// <param name="text">Optionaler erläuternder Text.</param>
        public async Task SendStreamErrorAsync(String             condition,
                                               String?            text                = null,
                                               CancellationToken  cancellationToken   = default)
        {

            try
            {
                await sendFrame(
                          $"<stream:error xmlns:stream='{StreamNamespace}'>" +
                          $"<{condition} xmlns='{StreamErrorNamespace}'/>" +
                          (text is not null
                               ? $"<text xmlns='{StreamErrorNamespace}'>{XmlEscaping.Escape(text)}</text>"
                               : "") +
                          "</stream:error>",
                          cancellationToken);
            }
            catch (Exception)
            {
                // Auch ein ungehörter Fehler beendet den Stream.
            }

            MarkClosed(condition);

        }

        #endregion


        #region (private) ProcessOpenAsync(frame, CancellationToken)

        /// <summary>
        /// Der Stream-Kopf der Gegenstelle (RFC 7395, Abschnitt 3.4).
        /// </summary>
        private async Task<Boolean> ProcessOpenAsync(String             frame,
                                                     CancellationToken  cancellationToken)
        {

            XElement element;

            try
            {
                element = XElement.Parse(frame, LoadOptions.PreserveWhitespace);
            }
            catch (XmlException)
            {
                await SendStreamErrorAsync("bad-format",
                                           "Der Stream-Kopf ist kein wohlgeformtes XML.",
                                           cancellationToken);
                return false;
            }

            var from  = element.Attribute("from")?.Value;
            var to    = element.Attribute("to")?.Value;
            var id    = element.Attribute("id")?.Value;

            if (IsInitiator)
            {

                // Die Gegenstelle muss sich als die Domain ausgeben, zu der wir
                // aufgebaut haben. Nennt sie eine andere, ist entweder die
                // Adresse falsch oder jemand sitzt dazwischen - in beiden
                // Fällen ist der Stream nichts wert.
                if (from is not null &&
                    !String.Equals(from, RemoteDomain, StringComparison.OrdinalIgnoreCase))
                {
                    await SendStreamErrorAsync("invalid-from",
                                               $"Erwartet wurde '{RemoteDomain}', geantwortet hat '{from}'.",
                                               cancellationToken);
                    return false;
                }

                MarkOpen(id);

                // XEP-0220, Schritt 1: sich unaufgefordert ausweisen. Der
                // Schlüssel bindet an die Stream-ID, die die Gegenstelle
                // gerade vergeben hat.
                if (RequiresDialback && secret is not null && StreamId is not null)
                    await sendFrame(
                              $"<db:result xmlns:db='{DialbackKey.Namespace}' " +
                              $"from='{XmlEscaping.Escape(LocalDomain)}' " +
                              $"to='{XmlEscaping.Escape(RemoteDomain!)}'>" +
                              DialbackKey.Generate(secret, RemoteDomain!, LocalDomain, StreamId) +
                              "</db:result>",
                              cancellationToken);

                return true;

            }

            // Empfänger: ohne 'from' wissen wir nicht, für wen die Gegenstelle
            // sprechen will, und die Absenderprüfung hätte nichts, woran sie
            // sich halten könnte.
            if (String.IsNullOrEmpty(from))
            {
                await SendStreamErrorAsync("improper-addressing",
                                           "Dem <open/> fehlt das 'from'.",
                                           cancellationToken);
                return false;
            }

            // RFC 6120, Abschnitt 4.9.3.6: ein 'to', das dieser Server nicht
            // bedient, ist <host-unknown/>.
            if (to is not null &&
                !String.Equals(to, LocalDomain, StringComparison.OrdinalIgnoreCase))
            {
                await SendStreamErrorAsync("host-unknown",
                                           $"Dieser Server bedient '{LocalDomain}', nicht '{to}'.",
                                           cancellationToken);
                return false;
            }

            RemoteDomain  = from;

            var streamId  = Guid.NewGuid().ToString("N");

            await sendFrame(
                      $"<open xmlns='{FramingNamespace}' " +
                      $"from='{XmlEscaping.Escape(LocalDomain)}' " +
                      $"to='{XmlEscaping.Escape(from)}' " +
                      $"id='{streamId}' " +
                      "version='1.0'/>",
                      cancellationToken);

            // RFC 6120, Abschnitt 4.3.2 verlangt die Features. Verlangt wird
            // Dialback aber unabhängig davon, ob es hier angekündigt steht -
            // eine Ankündigung, auf die man sich verlässt, könnte ein
            // Angreifer einfach weglassen.
            await sendFrame(
                      $"<stream:features xmlns:stream='{StreamNamespace}'>" +
                      (RequiresDialback
                           ? $"<dialback xmlns='urn:xmpp:features:dialback'><required/></dialback>"
                           : "") +
                      "</stream:features>",
                      cancellationToken);

            MarkOpen(streamId);

            return true;

        }

        #endregion

        #region (private) ProcessDialbackResultAsync(frame, CancellationToken)

        /// <summary>
        /// <c>&lt;db:result/&gt;</c> - XEP-0220, Schritt 1 beim Empfänger und
        /// Schritt 4 beim Aufbauenden.
        /// </summary>
        private async Task<Boolean> ProcessDialbackResultAsync(String             frame,
                                                               CancellationToken  cancellationToken)
        {

            var type = Attr(frame, "type");

            // Schritt 4: die Antwort auf den eigenen Schlüssel.
            if (IsInitiator)
            {

                if (type == "valid")
                {
                    MarkAuthenticated();
                    return true;
                }

                // XEP-0220, Abschnitt 2.1.3: ohne gültiges Dialback darf über
                // diesen Stream nichts laufen.
                await SendStreamErrorAsync(
                          "not-authorized",
                          $"Die Gegenstelle hat den Dialback-Schlüssel mit '{type ?? "(ohne Typ)"}' abgelehnt.",
                          cancellationToken);

                return true;

            }

            // Schritt 1: die Gegenstelle legt ihren Schlüssel vor.
            if (verifyKey is null)
            {
                // Dieser Stream verlangt kein Dialback - dann kann er es auch
                // nicht prüfen und tut nicht so, als hätte er es getan.
                return true;
            }

            var senderDomain  = Attr(frame, "from");
            var targetDomain  = Attr(frame, "to");
            var key           = Body(frame);

            if (senderDomain is null || key is null)
            {
                await SendStreamErrorAsync("improper-addressing",
                                           "Dem <db:result/> fehlt das 'from' oder der Schlüssel.",
                                           cancellationToken);
                return false;
            }

            // Die Gegenstelle darf sich nicht für eine andere Domain
            // ausweisen, als sie im <open/> genannt hat - sonst liesse sich
            // über einen einmal aufgebauten Stream nachträglich eine zweite
            // Identität nachschieben.
            if (!String.Equals(senderDomain, RemoteDomain, StringComparison.OrdinalIgnoreCase))
            {
                await SendStreamErrorAsync("invalid-from",
                                           $"Der Stream gehört zu '{RemoteDomain}', nicht zu '{senderDomain}'.",
                                           cancellationToken);
                return false;
            }

            if (targetDomain is not null &&
                !String.Equals(targetDomain, LocalDomain, StringComparison.OrdinalIgnoreCase))
            {
                await SendStreamErrorAsync("host-unknown",
                                           $"Dieser Server bedient '{LocalDomain}', nicht '{targetDomain}'.",
                                           cancellationToken);
                return false;
            }

            var gueltig = false;

            try
            {
                gueltig = await verifyKey(senderDomain, StreamId ?? "", key);
            }
            catch (Exception)
            {
                // Der autoritative Server war nicht zu erreichen. XEP-0220,
                // Abschnitt 2.4 nennt dafür <remote-server-timeout/>; hier
                // reicht "nicht gültig", die Antwort unten sagt es.
            }

            await sendFrame(
                      $"<db:result xmlns:db='{DialbackKey.Namespace}' " +
                      $"from='{XmlEscaping.Escape(LocalDomain)}' " +
                      $"to='{XmlEscaping.Escape(senderDomain)}' " +
                      $"type='{(gueltig ? "valid" : "invalid")}'/>",
                      cancellationToken);

            if (gueltig)
                MarkAuthenticated();

            else
                OnStanzaRefused?.Invoke($"Dialback für '{senderDomain}' fehlgeschlagen");

            return true;

        }

        #endregion

        #region (private) ProcessDialbackVerifyAsync(frame, CancellationToken)

        /// <summary>
        /// <c>&lt;db:verify/&gt;</c> - XEP-0220, Schritt 2 und 3 in der Rolle
        /// des autoritativen Servers.
        /// </summary>
        /// <remarks>
        /// Hier rechnet der Server nach, ob <b>er selbst</b> diesen Schlüssel
        /// hätte ausstellen können. Er merkt sich dafür nichts: aus
        /// Zieldomain, eigener Domain und Stream-ID ergibt sich der Schlüssel
        /// jedesmal neu. Ein Angreifer, der sich für diese Domain ausgibt,
        /// scheitert daran, dass die Frage bei ihm nie ankommt - sie geht an
        /// die Adresse, die der prüfende Server für diese Domain hinterlegt
        /// hat.
        /// </remarks>
        private async Task<Boolean> ProcessDialbackVerifyAsync(String             frame,
                                                               CancellationToken  cancellationToken)
        {

            var type = Attr(frame, "type");

            // Schritt 3: die Antwort auf die eigene Nachfrage.
            if (type is not null)
            {

                verificationAnswer?.TrySetResult(type == "valid");

                return true;

            }

            // Schritt 2: jemand fragt nach einem Schlüssel, den wir
            // ausgestellt haben sollen.
            var targetDomain  = Attr(frame, "from");
            var ownDomain     = Attr(frame, "to");
            var streamId      = Attr(frame, "id");
            var key           = Body(frame);

            if (targetDomain is null || streamId is null || key is null)
            {
                await SendStreamErrorAsync("improper-addressing",
                                           "Dem <db:verify/> fehlt 'from', 'id' oder der Schlüssel.",
                                           cancellationToken);
                return false;
            }

            if (ownDomain is not null &&
                !String.Equals(ownDomain, LocalDomain, StringComparison.OrdinalIgnoreCase))
            {
                await SendStreamErrorAsync("host-unknown",
                                           $"Dieser Server bedient '{LocalDomain}', nicht '{ownDomain}'.",
                                           cancellationToken);
                return false;
            }

            var gueltig = secret is not null &&
                          DialbackKey.Verify(secret, targetDomain, LocalDomain, streamId, key);

            await sendFrame(
                      $"<db:verify xmlns:db='{DialbackKey.Namespace}' " +
                      $"from='{XmlEscaping.Escape(LocalDomain)}' " +
                      $"to='{XmlEscaping.Escape(targetDomain)}' " +
                      $"id='{XmlEscaping.Escape(streamId)}' " +
                      $"type='{(gueltig ? "valid" : "invalid")}'/>",
                      cancellationToken);

            return true;

        }

        #endregion

        #region RequestVerificationAsync(targetDomain, streamId, key, Timeout, CancellationToken)

        /// <summary>
        /// Fragt den autoritativen Server, ob er diesen Schlüssel ausgestellt
        /// hat (XEP-0220, Schritt 2).
        /// </summary>
        /// <param name="targetDomain">
        /// Die Domain des annehmenden Servers - also die eigene. Sie geht als
        /// <c>from</c> hinaus, so verlangt es der normative Text zu Schritt 2.
        /// </param>
        /// <param name="streamId">Die Stream-ID, an die der Schlüssel gebunden ist.</param>
        /// <param name="key">Der vorgelegte Schlüssel.</param>
        public async Task<Boolean> RequestVerificationAsync(String             targetDomain,
                                                            String             streamId,
                                                            String             key,
                                                            TimeSpan           Timeout,
                                                            CancellationToken  cancellationToken = default)
        {

            verificationAnswer = new TaskCompletionSource<Boolean>(
                                     TaskCreationOptions.RunContinuationsAsynchronously);

            await sendFrame(
                      $"<db:verify xmlns:db='{DialbackKey.Namespace}' " +
                      $"from='{XmlEscaping.Escape(targetDomain)}' " +
                      $"to='{XmlEscaping.Escape(RemoteDomain!)}' " +
                      $"id='{XmlEscaping.Escape(streamId)}'>" +
                      key +
                      "</db:verify>",
                      cancellationToken);

            try
            {
                return await verificationAnswer.Task.WaitAsync(Timeout, cancellationToken);
            }
            catch (Exception)
            {
                return false;
            }

        }

        private TaskCompletionSource<Boolean>? verificationAnswer;

        #endregion

        #region (private static) IsDialback(frame, name) / Attr(xml, name) / Body(xml)

        /// <summary>
        /// Ist der Rahmen ein Dialback-Element des angegebenen Namens?
        /// </summary>
        /// <remarks>
        /// XEP-0220 schreibt durchweg das Präfix <c>db:</c>; die Variante mit
        /// Vorgabe-Namensraum wird trotzdem erkannt, weil sie ebenso gültig
        /// ist.
        /// </remarks>
        private static Boolean IsDialback(String frame, String name)

            => frame.StartsWith($"<db:{name}", StringComparison.Ordinal) ||
               (frame.StartsWith($"<{name}", StringComparison.Ordinal) &&
                frame.Contains(DialbackKey.Namespace, StringComparison.Ordinal));

        /// <summary>
        /// Liest ein Attribut aus einem Rahmen.
        /// </summary>
        /// <remarks>
        /// Über einen regulären Ausdruck und nicht über
        /// <see cref="XElement.Parse(String)"/>: die Dialback-Elemente tragen
        /// ein Präfix, und ob die Gegenstelle es auf dem Element selbst
        /// deklariert, steht ihr frei. Über TCP hängt die Deklaration am
        /// Stream-Root und der Rahmen wäre allein gar nicht wohlgeformt - das
        /// muss diese Schicht aushalten, sie soll ja beide Rahmungen tragen.
        /// </remarks>
        private static String? Attr(String xml, String name)
        {

            var m = Regex.Match(xml, $@"\b{name}\s*=\s*['""]([^'""]*)['""]");

            return m.Success && m.Groups[1].Value.Length > 0
                       ? m.Groups[1].Value
                       : null;

        }

        /// <summary>Der Textinhalt eines Rahmens, ohne umgebenden Leerraum.</summary>
        private static String? Body(String xml)
        {

            var m = Regex.Match(xml, @">([^<>]*)<\s*/");

            return m.Success && m.Groups[1].Value.Trim().Length > 0
                       ? m.Groups[1].Value.Trim()
                       : null;

        }

        #endregion

        #region (private) ProcessStanzaAsync(stanza, CancellationToken)

        private async Task<Boolean> ProcessStanzaAsync(String             stanza,
                                                       CancellationToken  cancellationToken)
        {

            if (!IsOpen)
            {
                // RFC 6120, Abschnitt 4.9.3.12: Stanzas vor dem Stream-Kopf
                // gibt es nicht.
                await SendStreamErrorAsync("not-well-formed",
                                           "Eine Stanza vor dem <open/>.",
                                           cancellationToken);
                return false;
            }

            if (deliverStanza is null)
            {
                // Ein ausgehender Stream trägt nur in eine Richtung
                // (RFC 6120, Abschnitt 4.1).
                OnStanzaRefused?.Invoke("Stanza auf einem ausgehenden Stream");
                return false;
            }

            // XEP-0220, Abschnitt 1: bis die Identität belegt ist, wird über
            // die Verbindung keine Stanza verarbeitet. Das ist die Zeile, die
            // Dialback überhaupt erst zu einer Sicherung macht - ohne sie
            // liefe der Austausch mit, ohne etwas zu entscheiden.
            if (RequiresDialback && !IsAuthenticated)
            {
                OnStanzaRefused?.Invoke("Stanza vor abgeschlossenem Dialback");
                return false;
            }

            var result = await deliverStanza(RemoteDomain!, stanza);

            if (result == RemoteStanzaResult.Accepted)
                return true;

            OnStanzaRefused?.Invoke(result.ToString());

            // RFC 6120, Abschnitt 8.1.1.1: bei einem 'from', für das die
            // Gegenstelle nicht sprechen darf, endet der Stream. Der Grund ist
            // nicht Strenge um ihrer selbst willen - wer einmal im Namen einer
            // fremden Domain schreibt, tut es beim nächsten Versuch wieder, und
            // eine einzelne verworfene Stanza hielte ihn nicht auf. Die
            // übrigen Ablehnungen betreffen nur die eine Stanza.
            if (result == RemoteStanzaResult.ForeignSender)
                await SendStreamErrorAsync("invalid-from",
                                           $"'{RemoteDomain}' darf nicht für eine fremde Domain sprechen.",
                                           cancellationToken);

            return false;

        }

        #endregion

        #region Abort(reason)

        /// <summary>
        /// Beendet den Stream, ohne einen Rahmen zu schicken - für den Fall,
        /// dass der Transport selbst schon weg ist und ein
        /// <c>&lt;close/&gt;</c> ohnehin niemanden mehr erreichte.
        /// </summary>
        internal void Abort(String? reason)
            => MarkClosed(reason);

        #endregion

        #region (private) MarkOpen(streamId) / MarkClosed(reason)

        private void MarkOpen(String? streamId)
        {

            lock (dataLock)
            {

                if (IsOpen || IsClosed)
                    return;

                StreamId  = streamId;
                IsOpen    = true;

            }

            openHandshake.TrySetResult();

        }

        private void MarkAuthenticated()
        {

            lock (dataLock)
            {

                if (IsAuthenticated || IsClosed)
                    return;

                IsAuthenticated = true;

            }

            dialbackDone.TrySetResult();

        }

        private void MarkClosed(String? reason)
        {

            lock (dataLock)
            {

                if (IsClosed)
                    return;

                IsClosed  = true;
                IsOpen    = false;

            }

            // Wer auf den Handshake wartet, soll nicht ins Zeitlimit laufen,
            // wenn schon feststeht, dass er nicht mehr kommt. Dasselbe gilt
            // für Dialback und für eine offene Verifikationsanfrage.
            openHandshake.TrySetCanceled();
            dialbackDone.TrySetCanceled();
            verificationAnswer?.TrySetResult(false);

            OnClosed?.Invoke(reason);

        }

        #endregion


        public override String ToString()

            => $"{(IsInitiator ? "→" : "←")} {LocalDomain} / {RemoteDomain ?? "(unbekannt)"}" +
               (IsClosed ? " (beendet)" : IsOpen ? " (offen)" : " (im Aufbau)");

    }

}
