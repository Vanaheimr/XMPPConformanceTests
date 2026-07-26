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

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// XEP-0199: XMPP Ping - misst Round-Trip-Zeiten und hält die Verbindung offen.
/// </summary>
public sealed class PingManager
{
    private readonly Func<string, Task> _sendStanza;
    private readonly Dictionary<string, (TaskCompletionSource<TimeSpan?> Tcs, DateTime Sent)> _pending = new();
    private readonly object _lock = new();
    private int _counter;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    public event Action<string, TimeSpan>? OnPong;
    public event Action<string>? OnPingTimeout;

    /// <summary>
    /// Der Ping wurde mit einem Stanza-Fehler beantwortet. Das ist etwas
    /// anderes als ein Timeout: die Gegenstelle war erreichbar, hat aber
    /// abgelehnt - <c>service-unavailable</c> heisst schlicht, dass sie
    /// XEP-0199 nicht unterstützt.
    /// </summary>
    public event Action<string, StanzaError>? OnPingError;

    public PingManager(Func<string, Task> sendStanza)
    {
        _sendStanza = sendStanza;
    }

    /// <summary>
    /// Sendet einen Ping und misst die Antwortzeit
    /// </summary>
    public async Task<TimeSpan?> PingAsync(string? to = null, CancellationToken ct = default)
    {
        var id = $"ping-{Interlocked.Increment(ref _counter)}";
        // RunContinuationsAsynchronously: ohne das laufen die Fortsetzungen des
        // Aufrufers synchron in dem Thread, der die Antwort abliefert - also in
        // der Empfangsschleife. Beliebiger Anwendercode würde dort das Lesen
        // weiterer Stanzas aufhalten.
        var tcs = new TaskCompletionSource<TimeSpan?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = DateTime.UtcNow;

        lock (_lock)
        {
            _pending[id] = (tcs, sent);
        }

        var toAttr = to != null ? $" to='{XmlEscaping.Escape(to)}'" : "";
        await _sendStanza($"<iq type='get' id='{id}'{toAttr}><ping xmlns='urn:xmpp:ping'/></iq>");

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);

            return await tcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            lock (_lock) _pending.Remove(id);
            OnPingTimeout?.Invoke(to ?? "server");
            return null;
        }
    }

    /// <summary>
    /// Verarbeitet eine Ping-Antwort
    /// </summary>
    public bool ProcessPong(string id)
    {
        lock (_lock)
        {
            if (!_pending.TryGetValue(id, out var entry))
                return false;

            _pending.Remove(id);
            var rtt = DateTime.UtcNow - entry.Sent;
            entry.Tcs.TrySetResult(rtt);
            OnPong?.Invoke(id, rtt);
            return true;
        }
    }

    /// <summary>
    /// Verarbeitet einen Stanza-Fehler auf einen offenen Ping.
    ///
    /// Ohne diese Behandlung lief ein <c>iq type='error'</c> in ProcessPong und
    /// wurde als gültige Antwort gewertet - eine abgelehnte Anfrage sah damit
    /// aus wie eine gemessene Laufzeit.
    /// </summary>
    public bool ProcessError(string id, StanzaError error)
    {

        (TaskCompletionSource<TimeSpan?> Tcs, DateTime Sent) entry;

        lock (_lock)
        {
            if (!_pending.TryGetValue(id, out entry))
                return false;

            _pending.Remove(id);
        }

        entry.Tcs.TrySetResult(null);
        OnPingError?.Invoke(id, error);

        return true;

    }

    /// <summary>
    /// Beantwortet einen Ping.
    ///
    /// Ohne 'from' kam die Anfrage vom eigenen Server (RFC 6120,
    /// Abschnitt 8.1.1.1); die Antwort geht dann ohne 'to' implizit dorthin
    /// zurück.
    /// </summary>
    public Task RespondAsync(string id, string? from = null)
    {
        var toAttr = from != null ? $" to='{XmlEscaping.Escape(from)}'" : "";
        return _sendStanza($"<iq type='result' id='{XmlEscaping.Escape(id)}'{toAttr}/>");
    }

    /// <summary>
    /// Prüft ob ein IQ ein Ping ist
    /// </summary>
    public static bool IsPing(string xml) =>
        xml.Contains("urn:xmpp:ping") && xml.Contains("type='get'");
}
