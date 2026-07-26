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

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// XEP-0184: Verfolgt gesendete Nachrichten bis zur Zustellbestätigung
/// und prüft eingehende Bestätigungen auf Spoofing.
/// </summary>
public sealed class ReceiptTracker
{
    private readonly Dictionary<string, PendingReceipt> _pending = new();
    private readonly object _lock = new();
    private readonly ILogger _logger;

    public event Action<string, string>? OnReceiptReceived; // messageId, from

    public ReceiptTracker(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Registriert eine gesendete Nachricht für Receipt-Tracking
    /// </summary>
    public void TrackMessage(string messageId, string to)
    {
        var bareTo = JidUtilities.Bare(to);
        lock (_lock)
        {
            _pending[messageId] = new PendingReceipt(messageId, bareTo, DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Verarbeitet eine eingehende Receipt mit Spoofing-Schutz
    /// </summary>
    public bool ProcessReceipt(string receiptId, string from)
    {
        var bareFrom = JidUtilities.Bare(from);

        lock (_lock)
        {
            if (!_pending.TryGetValue(receiptId, out var pending))
            {
                // Receipt für unbekannte Nachricht - ignorieren
                return false;
            }

            // SPOOFING-SCHUTZ: Receipt muss vom erwarteten Empfänger kommen
            if (!string.Equals(pending.ExpectedFrom, bareFrom, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Receipt-Spoofing erkannt! Erwartet: {Expected}, Erhalten: {Actual}",
                                   pending.ExpectedFrom, bareFrom);
                return false;
            }

            _pending.Remove(receiptId);
        }

        OnReceiptReceived?.Invoke(receiptId, from);
        return true;
    }

    /// <summary>
    /// Prüft auf Timeout (alte unbestätigte Nachrichten)
    /// </summary>
    public IEnumerable<string> GetTimedOutMessages(TimeSpan timeout)
    {
        var cutoff = DateTime.UtcNow - timeout;
        lock (_lock)
        {
            return _pending
                .Where(kvp => kvp.Value.SentAt < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();
        }
    }
}
