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

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// XEP-0198: Stream Management - zählt Stanzas, holt Acks ein und
/// ermöglicht (prinzipiell) Stream Resumption.
/// </summary>
public sealed class StreamManagementManager
{

    /// <summary>Der Namespace von XEP-0198.</summary>
    public const string Namespace = "urn:xmpp:sm:3";

    private readonly Func<string, Task> _sendStanza;
    private readonly ILogger _logger;

    /// <summary>
    /// Läuft gerade eine Aushandlung, wartet hier <see cref="NegotiateAsync"/>
    /// auf <c>&lt;enabled/&gt;</c> oder <c>&lt;failed/&gt;</c>.
    /// </summary>
    private TaskCompletionSource<bool>? _negotiation;

    private bool _enabled;
    private uint _inbound;       // Empfangene Stanzas
    private uint _outbound;      // Gesendete Stanzas
    private uint _lastAcked;     // Letzte bestätigte

    private readonly Queue<(uint Seq, string Stanza, DateTime Sent)> _unacked = new();
    private readonly object _lock = new();

    private string? _resumeId;
    private bool _resumable;

    public bool IsEnabled => _enabled;
    public bool CanResume => _resumable && _resumeId != null;
    public string? ResumeId => _resumeId;
    public uint InboundCount => _inbound;
    public uint OutboundCount => _outbound;
    public int UnackedCount { get { lock (_lock) return _unacked.Count; } }

    public event Action<uint>? OnAckReceived;
    public event Action<List<string>>? OnStanzasLost;
    public event Action? OnResumed;

    public StreamManagementManager(Func<string, Task> sendStanza, ILogger? logger = null)
    {
        _sendStanza = sendStanza;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Aktiviert Stream Management nach Bind
    /// </summary>
    public Task EnableAsync(bool requestResume = true)
    {
        var resume = requestResume ? " resume='true'" : "";
        return _sendStanza($"<enable xmlns='urn:xmpp:sm:3'{resume}/>");
    }

    /// <summary>
    /// Sendet <c>&lt;enable/&gt;</c> und wartet auf die Antwort des Servers.
    /// </summary>
    /// <remarks>
    /// Die Antwort kommt über die normale Empfangsschleife herein, nicht über
    /// einen eigenen Lesevorgang. Der Aufbau las früher selbst vom Socket und
    /// verwarf dabei bis zu zehn Rahmen, die nicht nach Stream Management
    /// aussahen - darunter Nachrichten und Presence.
    /// </remarks>
    /// <returns>true, wenn der Server mit <c>&lt;enabled/&gt;</c> geantwortet hat.</returns>
    public async Task<bool> NegotiateAsync(bool               requestResume  = false,
                                           TimeSpan?          timeout        = null,
                                           CancellationToken  ct             = default)
    {

        // RunContinuationsAsynchronously: die Antwort trifft im Thread der
        // Empfangsschleife ein; ohne das liefe alles Wartende dort weiter und
        // hielte das Lesen der nächsten Stanzas auf.
        var negotiation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        Interlocked.Exchange(ref _negotiation, negotiation);

        try
        {

            await EnableAsync(requestResume);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(10));

            return await negotiation.Task.WaitAsync(cts.Token);

        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Keine Antwort auf <enable/> - Stream Management bleibt aus");
            return false;
        }
        finally
        {
            Interlocked.CompareExchange(ref _negotiation, null, negotiation);
        }

    }

    /// <summary>
    /// Verarbeitet <c>&lt;enabled/&gt;</c> vom Server
    /// </summary>
    public void ProcessEnabled(string xml)
    {
        _enabled = true;
        _inbound = 0;
        _outbound = 0;
        _lastAcked = 0;

        lock (_lock) _unacked.Clear();

        var idMatch = Regex.Match(xml, @"id=['""]([^'""]+)['""]");
        _resumeId = idMatch.Success ? idMatch.Groups[1].Value : null;
        _resumable = xml.Contains("resume='true'") || xml.Contains("resume=\"true\"");

        if (_resumable)
            _logger.LogInformation("Stream Management aktiviert (Resume-ID: {ResumeIdPrefix}...)",
                                   _resumeId?[..Math.Min(8, _resumeId?.Length ?? 0)]);
        else
            _logger.LogInformation("Stream Management aktiviert (ohne Resume)");

        _negotiation?.TrySetResult(true);
    }

    /// <summary>
    /// Versucht Stream zu resumen
    /// </summary>
    public Task ResumeAsync()
    {
        if (!CanResume)
            throw new InvalidOperationException("Stream kann nicht resumed werden");

        return _sendStanza($"<resume xmlns='urn:xmpp:sm:3' h='{_inbound}' previd='{_resumeId}'/>");
    }

    /// <summary>
    /// Verarbeitet <c>&lt;resumed/&gt;</c>
    /// </summary>
    public void ProcessResumed(string xml)
    {
        var hMatch = Regex.Match(xml, @"h=['""](\d+)['""]");
        if (hMatch.Success)
        {
            ProcessAck(uint.Parse(hMatch.Groups[1].Value));
        }

        _enabled = true;
        _logger.LogInformation("Stream resumed");
        OnResumed?.Invoke();
    }

    /// <summary>
    /// Verarbeitet <c>&lt;failed/&gt;</c>
    /// </summary>
    public void ProcessFailed()
    {
        // Gilt für beides: eine abgelehnte Aushandlung und ein
        // fehlgeschlagenes Resume.
        _negotiation?.TrySetResult(false);

        List<string> lost;
        lock (_lock)
        {
            lost = _unacked.Select(x => x.Stanza).ToList();
            _unacked.Clear();
        }

        _resumable = false;
        _resumeId = null;

        if (lost.Count > 0)
        {
            _logger.LogWarning("Stream Resume fehlgeschlagen - {LostCount} Stanzas verloren", lost.Count);
            OnStanzasLost?.Invoke(lost);
        }
    }

    /// <summary>
    /// Prüft, ob eine Stanza mitgezählt werden muss.
    ///
    /// XEP-0198 Abschnitt 2 zählt ausschließlich die drei Stanza-Typen
    /// <c>message</c>, <c>presence</c> und <c>iq</c>. Nonzas - also
    /// <c>&lt;enable/&gt;</c>, <c>&lt;r/&gt;</c>, <c>&lt;a/&gt;</c>,
    /// <c>&lt;open/&gt;</c>, SASL-Elemente und so weiter - zählen nicht.
    /// Zählt eine Seite falsch, laufen die Zähler auseinander und der
    /// Gegenüber wertet das <c>h</c> als Protokollverletzung.
    /// </summary>
    public static bool IsCountableStanza(string xml)
    {
        if (string.IsNullOrEmpty(xml))
            return false;

        var i = 0;
        while (i < xml.Length && char.IsWhiteSpace(xml[i]))
            i++;

        if (i >= xml.Length || xml[i] != '<')
            return false;

        i++;
        var start = i;

        while (i < xml.Length &&
               (char.IsLetterOrDigit(xml[i]) || xml[i] == '-' || xml[i] == '_' || xml[i] == ':'))
            i++;

        var name = xml[start..i];

        // Ein Namespace-Präfix ändert nichts am Stanza-Typ: <client:message/> zählt.
        var colon = name.LastIndexOf(':');
        if (colon >= 0)
            name = name[(colon + 1)..];

        return name is "message" or "presence" or "iq";
    }

    /// <summary>
    /// Trackt eine ausgehende Stanza.
    ///
    /// Der Aufrufer muss dies erst nach dem erfolgreichen Senden tun und
    /// dabei die Sendereihenfolge einhalten - die Sequenznummern müssen der
    /// Reihenfolge auf der Leitung entsprechen, sonst bestätigt ein
    /// <c>&lt;a h='...'/&gt;</c> die falschen Stanzas.
    /// </summary>
    public void TrackOutgoing(string stanza)
    {
        if (!_enabled || !IsCountableStanza(stanza)) return;

        lock (_lock)
        {
            // 32-Bit-Zähler mit Überlauf auf 0 (XEP-0198, Abschnitt 4).
            _outbound = unchecked(_outbound + 1);
            _unacked.Enqueue((_outbound, stanza, DateTime.UtcNow));
        }
    }

    /// <summary>
    /// Trackt eine eingehende Stanza.
    ///
    /// Muss auf jedem Empfangspfad laufen - auch für Stanzas, die noch in der
    /// Aufbauphase direkt gelesen werden. Fehlen sie, meldet
    /// <c>&lt;a h='...'/&gt;</c> einen zu kleinen Wert.
    /// </summary>
    public void TrackIncoming(string stanza)
    {
        if (!_enabled || !IsCountableStanza(stanza)) return;

        // 32-Bit-Zähler mit Überlauf auf 0 (XEP-0198, Abschnitt 4).
        Interlocked.Increment(ref _inbound);
    }

    /// <summary>
    /// Fordert Ack vom Server an
    /// </summary>
    public Task RequestAckAsync()
    {
        if (!_enabled) return Task.CompletedTask;
        return _sendStanza("<r xmlns='urn:xmpp:sm:3'/>");
    }

    /// <summary>
    /// Sendet Ack an Server
    /// </summary>
    public Task SendAckAsync()
    {
        if (!_enabled) return Task.CompletedTask;
        return _sendStanza($"<a xmlns='urn:xmpp:sm:3' h='{_inbound}'/>");
    }

    /// <summary>
    /// Verarbeitet <c>&lt;a/&gt;</c> (Ack) vom Server
    /// </summary>
    public void ProcessAck(string xml)
    {
        var hMatch = Regex.Match(xml, @"h=['""](\d+)['""]");
        if (hMatch.Success)
        {
            ProcessAck(uint.Parse(hMatch.Groups[1].Value));
        }
    }

    /// <summary>
    /// Gilt die Stanza mit dieser Sequenznummer als bestätigt, wenn der
    /// Gegenüber <c>h</c> meldet?
    ///
    /// Ein schlichtes <c>Seq &lt;= h</c> wäre falsch: der Zähler ist nach
    /// XEP-0198 Abschnitt 4 ein 32-Bit-Wert, der nach 2^32-1 auf 0 überläuft.
    /// Direkt nach einem Überlauf ist h dann kleiner als die Sequenznummern
    /// der noch offenen Stanzas, und diese blieben für immer unbestätigt in
    /// der Queue liegen. Verglichen wird deshalb der Abstand in
    /// Modulo-Arithmetik.
    /// </summary>
    internal static bool IsAcknowledged(uint seq, uint h)
        => unchecked(h - seq) < 0x8000_0000u;

    private void ProcessAck(uint h)
    {
        uint acked = 0;

        lock (_lock)
        {
            while (_unacked.Count > 0 && IsAcknowledged(_unacked.Peek().Seq, h))
            {
                _unacked.Dequeue();
                acked++;
            }
            _lastAcked = h;
        }

        if (acked > 0)
        {
            OnAckReceived?.Invoke(acked);
        }
    }

    /// <summary>
    /// Verarbeitet <c>&lt;r/&gt;</c> (Ack-Request) vom Server
    /// </summary>
    public Task ProcessRequestAsync() => SendAckAsync();

    /// <summary>
    /// Gibt unbestätigte Stanzas zurück (für Resend nach Failed)
    /// </summary>
    public List<string> GetUnackedStanzas()
    {
        lock (_lock)
        {
            return _unacked.Select(x => x.Stanza).ToList();
        }
    }
}
