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

using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// XEP-0198: Stream Management - zählt Stanzas, holt Acks ein und
/// ermöglicht (prinzipiell) Stream Resumption.
/// </summary>
public sealed class StreamManagementManager
{
    private readonly Func<string, Task> _sendStanza;
    private readonly ILogger _logger;

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
    /// Trackt eine ausgehende Stanza
    /// </summary>
    public void TrackOutgoing(string stanza)
    {
        if (!_enabled) return;

        lock (_lock)
        {
            _outbound++;
            _unacked.Enqueue((_outbound, stanza, DateTime.UtcNow));
        }
    }

    /// <summary>
    /// Trackt eine eingehende Stanza
    /// </summary>
    public void TrackIncoming()
    {
        if (!_enabled) return;
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

    private void ProcessAck(uint h)
    {
        uint acked = 0;

        lock (_lock)
        {
            while (_unacked.Count > 0 && _unacked.Peek().Seq <= h)
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
