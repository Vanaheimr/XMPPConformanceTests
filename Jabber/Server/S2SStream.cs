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

        /// <summary>Wie dieser Stream eingepackt ist.</summary>
        private readonly IS2SFraming framing;

        /// <summary>
        /// Prüft, ob das im TLS-Handshake vorgelegte Zertifikat der
        /// Gegenstelle für die genannte Domain sprechen darf. Null, wenn
        /// SASL-EXTERNAL für diesen Stream nicht in Frage kommt - etwa weil
        /// es gar kein Zertifikat gibt.
        /// </summary>
        private readonly Func<String, Boolean>? externalIdentity;

        /// <summary>
        /// Darf dieser Stream sich per SASL-EXTERNAL ausweisen? Nur wenn ein
        /// eigenes Zertifikat vorgelegt wurde.
        /// </summary>
        private readonly Boolean canOfferExternal;

        /// <summary>
        /// Soll XEP-0288 versucht (Initiator) beziehungsweise angeboten
        /// (Empfänger) werden?
        /// </summary>
        private readonly Boolean bidi;

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
        /// Wird erfüllt, sobald der Stream offen <b>und</b> ausgewiesen ist.
        /// </summary>
        /// <remarks>
        /// Beides einzeln abzuwarten reicht nicht. Nach erfolgreichem SASL
        /// fängt der Stream von vorn an (RFC 6120, Abschnitt 6.4.6): einen
        /// Augenblick lang ist er ausgewiesen und trotzdem nicht offen. Wer
        /// dann sendet, verliert die Stanza - und zwar lautlos, weil der
        /// Stream weder geschlossen noch fehlerhaft ist.
        /// </remarks>
        private readonly TaskCompletionSource ready =
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

        /// <summary>Der Namensraum der WebSocket-Rahmung (RFC 7395, Abschnitt 3.1).</summary>
        public const String FramingNamespace = WebSocketFraming.Namespace;

        /// <summary>Der Namensraum der Stream-Ebene (RFC 6120, Abschnitt 4.8.2).</summary>
        public const String StreamNamespace = "http://etherx.jabber.org/streams";

        /// <summary>Der Namensraum der Stream-Fehlerbedingungen (RFC 6120, Abschnitt 4.9.2).</summary>
        public const String StreamErrorNamespace = "urn:ietf:params:xml:ns:xmpp-streams";

        /// <summary>XEP-0288: der Namensraum des <c>&lt;bidi/&gt;</c>-Elements.</summary>
        public const String BidiNamespace = "urn:xmpp:bidi";

        /// <summary>XEP-0288: der Namensraum der Ankündigung in den Features.</summary>
        public const String BidiFeatureNamespace = "urn:xmpp:features:bidi";

        /// <summary>
        /// XEP-0288: trägt dieser Stream beide Richtungen?
        /// </summary>
        /// <remarks>
        /// Ohne die Erweiterung ist eine S2S-Verbindung einseitig (RFC 6120,
        /// Abschnitt 4.1): wer eine Stanza bekommt, beantwortet sie über eine
        /// <b>eigene</b> Verbindung zur Absenderdomain. Das setzt voraus, dass
        /// er die Gegenstelle erreichen kann - hinter NAT, hinter einer
        /// Firewall oder ohne DNS-Eintrag kann er das nicht, und die Antwort
        /// geht verloren. Genau daran scheiterte der Rückweg im Lauf gegen
        /// Prosody.
        ///
        /// Ist Bidi ausgehandelt, trägt dieselbe Verbindung beide Richtungen.
        /// </remarks>
        public Boolean BidiEnabled { get; private set; }

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

        /// <summary>
        /// Womit die Domain der Gegenstelle belegt wurde, oder null solange
        /// sie es nicht ist.
        /// </summary>
        public String? AuthenticatedBy { get; private set; }

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

        /// <summary>
        /// Der Stream fängt von vorn an (RFC 6120, Abschnitt 6.4.6).
        /// </summary>
        /// <remarks>
        /// Der Transport muss darauf reagieren: was den Strom in Elemente
        /// zerlegt, hat den bisherigen Stream-Kopf gesehen und würde den
        /// neuen sonst für ein Kindelement halten.
        /// </remarks>
        public event Action? OnRestart;

        #endregion

        #region Constructor(s)

        private S2SStream(String                                           localDomain,
                          String?                                          remoteDomain,
                          Boolean                                          isInitiator,
                          Func<String, CancellationToken, Task>            sendFrame,
                          Func<String, String, Task<RemoteStanzaResult>>?  deliverStanza,
                          String?                                          secret,
                          Func<String, String, String, Task<Boolean>>?     verifyKey,
                          Boolean                                          requiresDialback,
                          IS2SFraming?                                     framing,
                          Func<String, Boolean>?                           externalIdentity,
                          Boolean                                          canOfferExternal,
                          Boolean                                          bidi)
        {

            LocalDomain         = localDomain;
            RemoteDomain        = remoteDomain;
            IsInitiator         = isInitiator;
            RequiresDialback    = requiresDialback;

            this.sendFrame      = sendFrame;
            this.deliverStanza  = deliverStanza;
            this.secret         = secret;
            this.verifyKey      = verifyKey;
            this.framing            = framing ?? WebSocketFraming.Instance;
            this.externalIdentity   = externalIdentity;
            this.canOfferExternal   = canOfferExternal;
            this.bidi               = bidi;

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
        /// <param name="deliverStanza">
        /// Nur für XEP-0288: wohin eingehende Stanzas gehen, sobald Bidi
        /// ausgehandelt ist. Ohne Bidi nimmt ein ausgehender Stream keine
        /// entgegen, und diese Funktion wird nie gerufen.
        /// </param>
        /// <param name="useBidi">
        /// XEP-0288 versuchen, wenn die Gegenstelle es ankündigt.
        /// </param>
        public static S2SStream Initiate(String                                           localDomain,
                                         String                                           remoteDomain,
                                         Func<String, CancellationToken, Task>            sendFrame,
                                         String?                                          secret            = null,
                                         IS2SFraming?                                     framing           = null,
                                         Boolean                                          canOfferExternal  = false,
                                         Func<String, String, Task<RemoteStanzaResult>>?  deliverStanza     = null,
                                         Boolean                                          useBidi           = false)

            => new (localDomain,
                    remoteDomain,
                    isInitiator:        true,
                    sendFrame:          sendFrame,
                    deliverStanza:      deliverStanza,
                    secret:             secret,
                    verifyKey:          null,
                    requiresDialback:   secret is not null,
                    framing:            framing,
                    externalIdentity:   null,
                    canOfferExternal:   canOfferExternal,
                    bidi:               useBidi);

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
        /// <param name="offerBidi">
        /// XEP-0288 in den Features ankündigen und ein
        /// <c>&lt;bidi/&gt;</c> der Gegenstelle annehmen.
        /// </param>
        public static S2SStream Accept(String                                          localDomain,
                                       Func<String, CancellationToken, Task>           sendFrame,
                                       Func<String, String, Task<RemoteStanzaResult>>  deliverStanza,
                                       String?                                         secret      = null,
                                       Func<String, String, String, Task<Boolean>>?    verifyKey          = null,
                                       IS2SFraming?                                    framing            = null,
                                       Func<String, Boolean>?                          externalIdentity   = null,
                                       Boolean                                         offerBidi          = false)

            => new (localDomain,
                    remoteDomain:       null,
                    isInitiator:        false,
                    sendFrame:          sendFrame,
                    deliverStanza:      deliverStanza,
                    secret:             secret,
                    verifyKey:          verifyKey,
                    requiresDialback:   verifyKey is not null,
                    framing:            framing,
                    externalIdentity:   externalIdentity,
                    canOfferExternal:   false,
                    bidi:               offerBidi);

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
                                                     Func<String, CancellationToken, Task> sendFrame,
                                                     IS2SFraming?                          framing = null)

            => new (localDomain,
                    remoteDomain,
                    isInitiator:       true,
                    sendFrame:         sendFrame,
                    deliverStanza:     null,
                    secret:            null,
                    verifyKey:         null,
                    requiresDialback:   false,
                    framing:            framing,
                    externalIdentity:   null,
                    canOfferExternal:   false,
                    bidi:               false);

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

            return sendFrame(framing.StreamOpen(LocalDomain, RemoteDomain, null),
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

        #region WaitUntilReadyAsync(Timeout, CancellationToken)

        /// <summary>
        /// Wartet, bis über den Stream tatsächlich gesendet werden darf -
        /// offen und, falls verlangt, ausgewiesen.
        /// </summary>
        public async Task<Boolean> WaitUntilReadyAsync(TimeSpan           Timeout,
                                                       CancellationToken  cancellationToken = default)
        {

            try
            {
                await ready.Task.WaitAsync(Timeout, cancellationToken);
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


            if (framing.IsStreamOpen(frame))
                return await ProcessOpenAsync(frame, cancellationToken);

            if (framing.IsStreamClose(frame))
            {
                MarkClosed(null);
                return true;
            }

            // RFC 6120, Abschnitt 4.9: nach einem Stream-Fehler ist der Stream
            // tot; eine Antwort darauf gibt es nicht.
            if (StanzaElement.Is(frame, "error") ||
                frame.Contains(StreamErrorNamespace, StringComparison.Ordinal))
            {
                MarkClosed($"Stream-Fehler der Gegenstelle: {frame}");
                return true;
            }

            // Die Features des Empfängers. Dass Dialback angeboten wird, steht
            // dort drin; verlangt wird es hier aber unabhängig davon, weil ein
            // Angreifer die Ankündigung schlicht weglassen könnte.
            // Der Elementname trägt hier auch das Präfix ab: Ein Server darf
            // seine Features als <stream:features/> oder als <features/>
            // schicken, je nachdem, woran er den Streams-Namensraum gebunden
            // hat (RFC 6120, Abschnitt 4.8.1). Beides ist dasselbe Element.
            if (StanzaElement.Is(frame, "features"))
                return await ProcessFeaturesAsync(frame, cancellationToken);

            if (StanzaElement.Is(frame, "bidi"))
                return ProcessBidi(frame);

            if (StanzaElement.Is(frame, "auth"))
                return await ProcessSaslAuthAsync(frame, cancellationToken);

            if (StanzaElement.Is(frame, "success"))
                return await ProcessSaslSuccessAsync(cancellationToken);

            if (StanzaElement.Is(frame, "failure") &&
                frame.Contains(SaslNamespace, StringComparison.Ordinal))
            {
                return await ProcessSaslFailureAsync(cancellationToken);
            }

            if (IsDialback(frame, "result"))
                return await ProcessDialbackResultAsync(frame, cancellationToken);

            if (IsDialback(frame, "verify"))
                return await ProcessDialbackVerifyAsync(frame, cancellationToken);

            if (StanzaElement.IsStanza(frame))
                return await ProcessStanzaAsync(frame, cancellationToken);

            // Ein Rahmen ohne Element ist kein unbekanntes Element, sondern gar
            // keines - Abschnitt 4.9.3.24 spricht von „a first-level child of
            // the stream that is not supported", und ein leerer Rahmen ist kein
            // Kind. Über TCP kommt so etwas nicht einmal an: SkipProlog im
            // Zerleger schluckt Leerraum, XML-Deklarationen und Kommentare, und
            // Leerraum als Keepalive ist ausdrücklich erlaubt (Abschnitt
            // 4.6.1). Über WebSocket wird jeder Frame durchgereicht.
            if (StanzaElement.NameOf(frame) is null)
                return false;

            // RFC 6120, Abschnitt 4.9.3.24, wie auf der Client-Verbindung seit
            // D26.
            //
            // Bis hierher blieb ein unbekanntes Element liegen, und das war
            // eine offen vermerkte Lücke und keine Nachlässigkeit: Auf dem
            // Client-Stream sprechen beide Seiten dasselbe, hier steht eine
            // fremde Implementierung gegenüber. Einen Stream abzubrechen, weil
            // man ein Element nicht kennt, wäre gegenüber Prosody oder ejabberd
            // eine Wette gewesen.
            //
            // Gemessen wurde deshalb zuerst: über den vollen Lauf gegen beide
            // Gegenstellen, ausgehend wie eingehend, fiel kein einziger Rahmen
            // bis hierher durch - und der Fühler dafür hat nachweislich
            // angeschlagen, sonst hiesse „nichts gemessen" nur „nicht
            // hingesehen".
            await SendStreamErrorAsync("unsupported-stanza-type",
                                       cancellationToken: cancellationToken);

            return true;

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
                await sendFrame(framing.StreamClose(), cancellationToken);
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
        /// Der Stream-Kopf der Gegenstelle (RFC 7395, Abschnitt 3.4 bzw.
        /// RFC 6120, Abschnitt 4.7).
        /// </summary>
        /// <remarks>
        /// Die Attribute werden gelesen, nicht geparst. Über TCP ist der
        /// Stream-Kopf ein <b>offenes</b> Tag und damit für sich genommen kein
        /// wohlgeformtes XML - <see cref="XElement.Parse(String)"/> stand hier
        /// zuerst und hätte jede TCP-Verbindung mit
        /// <c>&lt;bad-format/&gt;</c> abgewiesen.
        /// </remarks>
        private async Task<Boolean> ProcessOpenAsync(String             frame,
                                                     CancellationToken  cancellationToken)
        {

            var from  = Attr(frame, "from");
            var to    = Attr(frame, "to");
            var id    = Attr(frame, "id");

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

                // Nach einem SASL-Neustart ist der Stream schon ausgewiesen;
                // dann steht der zweite Stream-Kopf nur noch für den
                // Neuanfang und es ist nichts mehr auszuhandeln.
                if (IsAuthenticated)
                    return true;

                // Kommt SASL-EXTERNAL in Frage, wird das Angebot der
                // Gegenstelle abgewartet - es steht in den Features, die
                // gleich folgen.
                //
                // Für XEP-0288 gilt dasselbe, und zwar auch dann, wenn nur
                // Dialback in Frage kommt: ob Bidi angeboten wird, steht
                // ebenfalls erst in den Features, und das <bidi/> muss *vor*
                // dem <db:result/> hinausgehen (XEP-0288, Abschnitt 4). Der
                // unaufgeforderte Dialback aus XEP-0220 wandert deshalb nach
                // ProcessFeaturesAsync.
                if (canOfferExternal || bidi)
                    return true;

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

            await sendFrame(framing.StreamOpen(LocalDomain, from, streamId),
                            cancellationToken);

            // RFC 6120, Abschnitt 4.3.2 verlangt die Features. Verlangt wird
            // Dialback aber unabhängig davon, ob es hier angekündigt steht -
            // eine Ankündigung, auf die man sich verlässt, könnte ein
            // Angreifer einfach weglassen.
            // SASL-EXTERNAL nur anbieten, wenn ein Zertifikat der Gegenstelle
            // vorliegt, das überhaupt geprüft werden könnte - sonst wäre das
            // Angebot eine Einladung in eine Sackgasse.
            var bietetExternal = externalIdentity is not null && !IsAuthenticated;

            await sendFrame(
                      $"<stream:features xmlns:stream='{StreamNamespace}'>" +
                      (bietetExternal
                           ? $"<mechanisms xmlns='{SaslNamespace}'><mechanism>EXTERNAL</mechanism></mechanisms>"
                           : "") +
                      (RequiresDialback && !IsAuthenticated
                           ? "<dialback xmlns='urn:xmpp:features:dialback'><required/></dialback>"
                           : "") +
                      // XEP-0288, Abschnitt 3: angekündigt wird vor *und* nach
                      // TLS. Ist Bidi bereits ausgehandelt, entfällt die
                      // Ankündigung - ein zweites <bidi/> hätte nichts mehr zu
                      // sagen.
                      //
                      // Zwei Formen, und die zweite ist eine Zumutung mit
                      // Beleg: ejabberd 24.12 greift die Form der XEP nicht
                      // auf. Seine annehmende Seite kündigt selbst
                      // urn:xmpp:bidi an (siehe KuendigtBidiAn), und seine
                      // aufbauende Seite sucht offenbar dasselbe. Kündigen wir
                      // nur die XEP-Form an, nimmt es unsere Rückrichtung
                      // nicht - beobachtet, nicht vermutet: mit beiden Formen
                      // nimmt es sie.
                      //
                      // In P6 stand hier die Gegenthese, aus ejabberds
                      // *master* geschlossen, wo es behoben ist. Die
                      // ausgelieferte Fassung verhält sich anders, und darauf
                      // kommt es an.
                      //
                      // Auf dem Draht ist das eindeutig: das Freischalt-Element
                      // heisst in beiden Lesarten urn:xmpp:bidi, es gibt also
                      // nur eine Antwort. Wer nur die XEP-Form kennt,
                      // übergeht das zweite Element als unbekanntes Feature.
                      (bidi && !BidiEnabled
                           ? $"<bidi xmlns='{BidiFeatureNamespace}'/>" +
                             $"<bidi xmlns='{BidiNamespace}'/>"
                           : "") +
                      "</stream:features>",
                      cancellationToken);

            MarkOpen(streamId);

            return true;

        }

        #endregion

        #region SASL-EXTERNAL (RFC 6120, Abschnitt 6; XEP-0178)

        /// <summary>Der Namensraum der SASL-Aushandlung.</summary>
        public const String SaslNamespace = "urn:ietf:params:xml:ns:xmpp-sasl";

        /// <summary>
        /// Die Features der Gegenstelle - hier entscheidet der aufbauende
        /// Server, ob er SASL-EXTERNAL versucht oder auf Dialback zurückfällt.
        /// </summary>
        private async Task<Boolean> ProcessFeaturesAsync(String             frame,
                                                         CancellationToken  cancellationToken)
        {

            if (!IsInitiator || IsAuthenticated)
                return true;

            // XEP-0288, Abschnitt 4: das <bidi/> geht *vor* SASL oder
            // Dialback hinaus. Danach wäre es zu spät - die Gegenstelle hat
            // dann bereits entschieden, wie sie antwortet.
            //
            // Nach TLS ist hier ohnehin: diesen Stream gibt es erst, wenn der
            // Transport die Verschlüsselung hinter sich hat (XEP-0288
            // verlangt genau diese Reihenfolge).
            if (bidi && !BidiEnabled && KuendigtBidiAn(frame))
            {
                await sendFrame($"<bidi xmlns='{BidiNamespace}'/>", cancellationToken);
                BidiEnabled = true;
            }

            var bietetExternal = frame.Contains(SaslNamespace, StringComparison.Ordinal) &&
                                 frame.Contains("EXTERNAL",    StringComparison.Ordinal);

            if (canOfferExternal && bietetExternal)
            {

                // RFC 6120, Abschnitt 6.4.2: die authzid ist die Identität, für
                // die gesprochen werden soll - Base64, wie jede SASL-Nutzlast.
                var authzid = Convert.ToBase64String(
                                  System.Text.Encoding.UTF8.GetBytes(LocalDomain));

                await sendFrame(
                          $"<auth xmlns='{SaslNamespace}' mechanism='EXTERNAL'>{authzid}</auth>",
                          cancellationToken);

                return true;

            }

            // Kein EXTERNAL - dann der andere Weg, sofern vorgesehen.
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

        /// <summary>
        /// Steht in diesen Features ein Bidi-Angebot?
        /// </summary>
        /// <remarks>
        /// XEP-0288 vergibt zwei Namensräume und meint zwei verschiedene
        /// Dinge damit: <see cref="BidiFeatureNamespace"/> für die
        /// Ankündigung, <see cref="BidiNamespace"/> für das Element, mit dem
        /// der aufbauende Server sie annimmt. Angekündigt wird der erste -
        /// Prosody hält sich daran, und wir tun es auch.
        ///
        /// ejabberd 24.12 nicht: seine annehmende Seite legt das
        /// <i>Freischalt</i>-Element in die Features. Upstream ist das
        /// inzwischen behoben, in den ausgelieferten Fassungen steht es noch,
        /// und sie sind zahlreich.
        ///
        /// Deshalb hier beide Formen - aber nur beim Lesen. Was wir selbst
        /// ankündigen, bleibt die Form der XEP; ejabberds aufbauende Seite
        /// sucht genau die und versteht uns. Wer beim Lesen streng bliebe,
        /// bekäme keinen Fehler, sondern eine Verbindung, die stillschweigend
        /// einseitig ist - und deren Antworten dann an einer Firewall hängen
        /// bleiben, aus keinem im Protokoll sichtbaren Grund.
        /// </remarks>
        private static Boolean KuendigtBidiAn(String features)

            => features.Contains(BidiFeatureNamespace, StringComparison.Ordinal) ||
               features.Contains(BidiNamespace,        StringComparison.Ordinal);

        /// <summary>
        /// <c>&lt;auth mechanism='EXTERNAL'/&gt;</c> auf der annehmenden
        /// Seite: das Zertifikat muss die behauptete Domain decken.
        /// </summary>
        /// <remarks>
        /// Hier liegt der ganze Unterschied zu Dialback. Dort wird die Domain
        /// belegt, indem bei einer hinterlegten Adresse nachgefragt wird; hier,
        /// indem das im TLS-Handshake vorgelegte Zertifikat gelesen wird. Kein
        /// zweiter Verbindungsaufbau - dafür hängt alles an
        /// <see cref="CertificateIdentity"/>.
        ///
        /// Eine leere authzid (<c>=</c>) ist zulässig und heisst: nimm die
        /// Identität aus dem Zertifikat. Weil ein Zertifikat für mehrere
        /// Domains gelten kann, wird sie hier auf das <c>from</c> des
        /// Stream-Kopfs bezogen - eine andere Wahl gäbe es nicht, ohne zu
        /// raten.
        /// </remarks>
        private async Task<Boolean> ProcessSaslAuthAsync(String             frame,
                                                         CancellationToken  cancellationToken)
        {

            if (IsInitiator)
                return false;

            var mechanismus = Attr(frame, "mechanism");

            if (externalIdentity is null || mechanismus != "EXTERNAL")
            {

                await sendFrame(
                          $"<failure xmlns='{SaslNamespace}'><invalid-mechanism/></failure>",
                          cancellationToken);

                return true;

            }

            var behauptet = RemoteDomain;
            var nutzlast  = Body(frame);

            if (nutzlast is not null && nutzlast != "=")
            {

                try
                {
                    behauptet = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(nutzlast));
                }
                catch (FormatException)
                {

                    await sendFrame(
                              $"<failure xmlns='{SaslNamespace}'><incorrect-encoding/></failure>",
                              cancellationToken);

                    return true;

                }

            }

            // Wer sich für eine andere Domain ausweist, als der Stream-Kopf
            // nennt, bekommt nichts - sonst liesse sich der Stream nachträglich
            // auf eine zweite Identität umschreiben.
            if (behauptet is null ||
                !String.Equals(behauptet, RemoteDomain, StringComparison.OrdinalIgnoreCase) ||
                !externalIdentity(behauptet))
            {

                await sendFrame(
                          $"<failure xmlns='{SaslNamespace}'><not-authorized/></failure>",
                          cancellationToken);

                OnStanzaRefused?.Invoke($"SASL-EXTERNAL für '{behauptet ?? "(ohne)"}' abgelehnt");

                return true;

            }

            // Reihenfolge zählt: erst den Neustart vormerken, dann den
            // Ausweis. Andersherum meldete sich der Stream für einen
            // Augenblick als benutzbar, obwohl sein neuer Kopf noch aussteht.
            ReopenForRestart();
            MarkAuthenticated("SASL-EXTERNAL");

            await sendFrame($"<success xmlns='{SaslNamespace}'/>", cancellationToken);

            return true;

        }

        /// <summary>
        /// <c>&lt;success/&gt;</c> auf der aufbauenden Seite: Stream neu
        /// öffnen (RFC 6120, Abschnitt 6.4.6).
        /// </summary>
        private async Task<Boolean> ProcessSaslSuccessAsync(CancellationToken cancellationToken)
        {

            if (!IsInitiator)
                return false;

            ReopenForRestart();
            MarkAuthenticated("SASL-EXTERNAL");

            await sendFrame(framing.StreamOpen(LocalDomain, RemoteDomain, null), cancellationToken);

            return true;

        }

        /// <summary>
        /// <c>&lt;failure/&gt;</c>: SASL ist gescheitert. Ein Rückfall auf
        /// Dialback findet <b>nicht</b> statt.
        /// </summary>
        /// <remarks>
        /// Das ist eine Festlegung und keine Auslassung. Wer sich per
        /// Zertifikat ausweisen wollte und abgelehnt wurde, hat ein Problem,
        /// das ein zweiter Anlauf mit einem schwächeren Verfahren nicht löst -
        /// er verdeckt es nur. RFC 6120, Abschnitt 6.4.5 erlaubt zwar weitere
        /// Versuche; hier endet der Stream.
        /// </remarks>
        private async Task<Boolean> ProcessSaslFailureAsync(CancellationToken cancellationToken)
        {

            await SendStreamErrorAsync("not-authorized",
                                       "SASL-EXTERNAL wurde abgelehnt.",
                                       cancellationToken);

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
        internal static String? Attr(String xml, String name)
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

        #region (private) ProcessBidi(frame)

        /// <summary>
        /// XEP-0288, Abschnitt 4: die Gegenstelle schaltet die Rückrichtung
        /// frei.
        /// </summary>
        /// <remarks>
        /// Nur der Empfänger nimmt ein <c>&lt;bidi/&gt;</c> entgegen. Beim
        /// Initiator wäre es verkehrt herum - er hat es selbst geschickt, und
        /// eines zurück hiesse, die Gegenstelle wollte über <i>unseren</i>
        /// ausgehenden Stream ihrerseits etwas freischalten, was der Abschnitt
        /// nicht vorsieht.
        ///
        /// Angenommen wird es auch dann, wenn die Ankündigung gar nicht
        /// erbeten war (<c>bidi</c> aus): dann wird es <b>nicht</b>
        /// freigeschaltet. Ein Angreifer könnte sonst eine Rückrichtung
        /// erzwingen, die dieser Server nie angeboten hat.
        /// </remarks>
        private Boolean ProcessBidi(String frame)
        {

            if (IsInitiator || !frame.Contains(BidiNamespace, StringComparison.Ordinal))
                return false;

            if (!bidi)
            {
                OnStanzaRefused?.Invoke("<bidi/> ohne Ankündigung");
                return false;
            }

            BidiEnabled = true;

            return true;

        }

        #endregion

        #region SendStanzaOverBidiAsync(stanza, CancellationToken)

        /// <summary>
        /// Schickt eine Stanza über die Rückrichtung eines eingehenden Streams
        /// (XEP-0288).
        /// </summary>
        /// <returns>
        /// false, wenn dieser Stream die Rückrichtung nicht tragen darf - dann
        /// bleibt nur der gewöhnliche Weg über eine eigene Verbindung.
        /// </returns>
        /// <remarks>
        /// Zwei Bedingungen aus Abschnitt 4, und beide sind Sicherungen, keine
        /// Formalitäten:
        /// <list type="bullet">
        ///   <item>
        ///     <i>"The receiving server MUST NOT send stanzas to the peer
        ///     before it has authenticated via SASL, or the peer's identity has
        ///     been verified via Server Dialback."</i> Wer noch nicht belegt
        ///     hat, wer er ist, bekommt auch nichts - sonst liesse sich mit
        ///     einer blossen Behauptung fremde Post abholen.
        ///   </item>
        ///   <item>
        ///     <i>"The receiving server MUST only send stanzas for which it has
        ///     been authenticated - in the case of TLS/SASL based
        ///     authentication, this is the value of the stream's 'to'
        ///     attribute."</i> Das <c>to</c> des eingehenden Stream-Kopfs ist
        ///     unsere eigene Domain; für eine andere zu sprechen wäre hier
        ///     genauso falsch wie umgekehrt.
        ///   </item>
        /// </list>
        /// </remarks>
        public async Task<Boolean> SendStanzaOverBidiAsync(String             stanza,
                                                           CancellationToken  cancellationToken = default)
        {

            lock (dataLock)
            {

                if (IsInitiator || !BidiEnabled || !IsOpen || IsClosed || !IsAuthenticated)
                    return false;

            }

            var from = Attr(stanza, "from");

            if (from is not null && !GehoertZuLocalDomain(from))
            {
                OnStanzaRefused?.Invoke(
                    $"'{from}' gehört nicht zu '{LocalDomain}' - nicht über die Rückrichtung");
                return false;
            }

            await sendFrame(stanza, cancellationToken);

            return true;

        }

        /// <summary>
        /// Sucht unter eingehenden Streams einen, der die Rückrichtung zu
        /// dieser Domain trägt, und schickt die Stanza dort hinaus.
        /// </summary>
        /// <returns>true, wenn einer sie genommen hat.</returns>
        /// <remarks>
        /// Hier und nicht in den Transporten, obwohl beide dasselbe brauchen:
        /// der Abgleich der Domain ist die Stelle, an der eine Stanza an die
        /// falsche Gegenstelle geraten kann, und zwei Fassungen davon wären
        /// zwei Gelegenheiten dafür. Beim ersten Mutationslauf ist genau diese
        /// Regel durchgerutscht - sie hatte keinen Test, weil an jedem Aufbau
        /// nur eine Gegenstelle hing.
        /// </remarks>
        internal static async Task<Boolean> TryDeliverOverBidiAsync(IEnumerable<S2SStream>  inboundStreams,
                                                                    String                  remoteDomain,
                                                                    String                  stanza,
                                                                    CancellationToken       cancellationToken = default)
        {

            foreach (var stream in inboundStreams)
            {

                if (!String.Equals(stream.RemoteDomain, remoteDomain, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Ob der Stream die Rückrichtung überhaupt tragen darf,
                // entscheidet er selbst - dort stehen die Bedingungen aus
                // XEP-0288, Abschnitt 4.
                if (await stream.SendStanzaOverBidiAsync(stanza, cancellationToken))
                    return true;

            }

            return false;

        }

        private Boolean GehoertZuLocalDomain(String jid)
        {

            var at      = jid.IndexOf('@');
            var ohne    = at >= 0 ? jid[(at + 1)..] : jid;
            var schraeg = ohne.IndexOf('/');

            if (schraeg >= 0)
                ohne = ohne[..schraeg];

            return String.Equals(ohne, LocalDomain, StringComparison.OrdinalIgnoreCase);

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

            // Ein ausgehender Stream trägt nur in eine Richtung (RFC 6120,
            // Abschnitt 4.1) - es sei denn, XEP-0288 ist ausgehandelt. Dann
            // haben *wir* die Rückrichtung erbeten, und was darüber kommt,
            // gehört hierher.
            if (IsInitiator && !BidiEnabled)
            {
                OnStanzaRefused?.Invoke("Stanza auf einem ausgehenden Stream");
                return false;
            }

            if (deliverStanza is null)
            {
                OnStanzaRefused?.Invoke("Kein Empfänger für eingehende Stanzas");
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

            if (IsAuthenticated || !RequiresDialback)
                ready.TrySetResult();

        }

        private void MarkAuthenticated(String wodurch = "Dialback")
        {

            lock (dataLock)
            {

                if (IsAuthenticated || IsClosed)
                    return;

                IsAuthenticated  = true;
                AuthenticatedBy  = wodurch;

            }

            dialbackDone.TrySetResult();

            // Nur wenn der Stream nicht gerade neu anfängt - sonst meldet er
            // sich benutzbar, während sein Kopf noch aussteht.
            if (IsOpen)
                ready.TrySetResult();

        }

        /// <summary>
        /// Setzt den Stream auf "noch nicht geöffnet" zurück, ohne das
        /// Erreichte preiszugeben.
        /// </summary>
        /// <remarks>
        /// RFC 6120, Abschnitt 6.4.6: nach erfolgreichem SASL beginnt der
        /// Stream von vorn - neuer Stream-Kopf, neue Stream-ID. Was
        /// <b>nicht</b> zurückgesetzt wird, ist die Feststellung, wer die
        /// Gegenstelle ist: die stammt aus dem Zertifikat und nicht aus dem
        /// Stream, und sie noch einmal zu erfragen hiesse, sie noch einmal
        /// erraten zu lassen.
        /// </remarks>
        private void ReopenForRestart()
        {

            lock (dataLock)
            {

                if (IsClosed)
                    return;

                IsOpen    = false;
                StreamId  = null;

            }

            OnRestart?.Invoke();

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
            ready.TrySetCanceled();
            verificationAnswer?.TrySetResult(false);

            OnClosed?.Invoke(reason);

        }

        #endregion


        public override String ToString()

            => $"{(IsInitiator ? "→" : "←")} {LocalDomain} / {RemoteDomain ?? "(unbekannt)"}" +
               (IsClosed ? " (beendet)" : IsOpen ? " (offen)" : " (im Aufbau)");

    }

}
