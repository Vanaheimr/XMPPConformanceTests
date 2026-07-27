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
                          Func<String, String, Task<RemoteStanzaResult>>?  deliverStanza)
        {

            LocalDomain         = localDomain;
            RemoteDomain        = remoteDomain;
            IsInitiator         = isInitiator;

            this.sendFrame      = sendFrame;
            this.deliverStanza  = deliverStanza;

        }

        #endregion

        #region (static) Initiate(localDomain, remoteDomain, sendFrame)

        /// <summary>
        /// Der ausgehende Stream: er trägt Stanzas hinaus und nimmt keine
        /// entgegen.
        /// </summary>
        /// <param name="localDomain">Die eigene Domain.</param>
        /// <param name="remoteDomain">Die Domain, zu der aufgebaut wird.</param>
        /// <param name="sendFrame">Schickt einen Rahmen über den Transport.</param>
        public static S2SStream Initiate(String                                localDomain,
                                         String                                remoteDomain,
                                         Func<String, CancellationToken, Task> sendFrame)

            => new (localDomain,
                    remoteDomain,
                    isInitiator:    true,
                    sendFrame:      sendFrame,
                    deliverStanza:  null);

        #endregion

        #region (static) Accept(localDomain, sendFrame, deliverStanza)

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
        public static S2SStream Accept(String                                          localDomain,
                                       Func<String, CancellationToken, Task>           sendFrame,
                                       Func<String, String, Task<RemoteStanzaResult>>  deliverStanza)

            => new (localDomain,
                    remoteDomain:   null,
                    isInitiator:    false,
                    sendFrame:      sendFrame,
                    deliverStanza:  deliverStanza);

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

            // Die Features des Empfängers. Auszuhandeln gibt es hier noch
            // nichts - sobald Dialback dazukommt, steht es genau dort drin.
            if (frame.StartsWith("<stream:features", StringComparison.Ordinal) ||
                frame.StartsWith("<features",        StringComparison.Ordinal))
            {
                return true;
            }

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
            }

            await sendFrame(stanza, cancellationToken);

            return true;

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

            // Noch ist nichts auszuhandeln. Der leere Rahmen steht trotzdem
            // hier, weil RFC 6120, Abschnitt 4.3.2 ihn verlangt und weil
            // Dialback genau dort angekündigt werden wird.
            await sendFrame($"<stream:features xmlns:stream='{StreamNamespace}'/>",
                            cancellationToken);

            MarkOpen(streamId);

            return true;

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
            // wenn schon feststeht, dass er nicht mehr kommt.
            openHandshake.TrySetCanceled();

            OnClosed?.Invoke(reason);

        }

        #endregion


        public override String ToString()

            => $"{(IsInitiator ? "→" : "←")} {LocalDomain} / {RemoteDomain ?? "(unbekannt)"}" +
               (IsClosed ? " (beendet)" : IsOpen ? " (offen)" : " (im Aufbau)");

    }

}
