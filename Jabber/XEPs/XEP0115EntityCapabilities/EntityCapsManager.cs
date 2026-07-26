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

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// XEP-0115: Entity Capabilities - kürzt wiederholte disco#info-Abfragen
/// durch einen Hash der eigenen bzw. fremden Feature-Liste ab.
/// </summary>
public sealed class EntityCapsManager
{
    private readonly DiscoManager _disco;
    private readonly Dictionary<string, DiscoInfo> _cache = new();
    private readonly object _lock = new();

    public string Node { get; set; } = "https://github.com/xmpp-console";

    public event Action<string, DiscoInfo>? OnCapsDiscovered;

    public EntityCapsManager(DiscoManager disco)
    {
        _disco = disco;
    }

    /// <summary>
    /// Berechnet den Verification String (SHA-1 Hash der Features)
    /// </summary>
    public string CalculateVerificationString()
    {
        var sb = new StringBuilder();

        // Identitäten als category/type/xml:lang/name - jeder Schrägstrich steht
        // auch ohne Wert da (XEP-0115, Abschnitt 5.1). Sortiert wird über genau
        // die Zeichenkette, die auch ausgegeben wird, damit name mitsortiert;
        // der xml:lang-Platz bleibt leer, weil DiscoIdentity kein xml:lang trägt.
        foreach (var identity in _disco.LocalIdentities
                                      .Select(id => $"{id.Category}/{id.Type}//{id.Name ?? ""}")
                                      .Order(StringComparer.Ordinal))
        {
            sb.Append(identity).Append('<');
        }

        // Features sortiert - XEP-0115, Abschnitt 5.1 verlangt Oktett-Reihenfolge,
        // nicht den kulturabhängigen Standardvergleich ('B' 0x42 vor 'a' 0x61).
        foreach (var feature in _disco.LocalFeatures.Order(StringComparer.Ordinal))
        {
            sb.Append($"{feature}<");
        }

        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Erzeugt das <c>&lt;c/&gt;</c> Element für Presence
    /// </summary>
    public string GetCapsElement()
    {
        var ver = CalculateVerificationString();
        return $"<c xmlns='http://jabber.org/protocol/caps' hash='sha-1' node='{Node}' ver='{ver}'/>";
    }

    /// <summary>
    /// Verarbeitet ein caps-Element aus Presence
    /// </summary>
    public async Task ProcessCapsAsync(string from, string node, string ver, CancellationToken ct = default)
    {
        var cacheKey = $"{node}#{ver}";

        lock (_lock)
        {
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                OnCapsDiscovered?.Invoke(from, cached);
                return;
            }
        }

        // Noch nicht im Cache - disco#info abfragen
        var info = await _disco.QueryInfoAsync(from, cacheKey, ct: ct);

        if (info != null)
        {
            lock (_lock)
            {
                _cache[cacheKey] = info;
            }
            OnCapsDiscovered?.Invoke(from, info);
        }
    }

    /// <summary>
    /// Prüft ob ein JID ein Feature unterstützt (aus Cache)
    /// </summary>
    public DiscoInfo? GetCachedInfo(string verString)
    {
        lock (_lock)
        {
            return _cache.TryGetValue(verString, out var info) ? info : null;
        }
    }

    /// <summary>
    /// Extrahiert Caps aus einer Presence
    /// </summary>
    public static (string Node, string Ver, string? Hash)? ParseCaps(string presenceXml)
    {
        var match = Regex.Match(presenceXml,
            @"<c\s+[^>]*xmlns=['""]http://jabber\.org/protocol/caps['""][^>]*>",
            RegexOptions.Singleline);

        if (!match.Success) return null;

        var elem = match.Value;
        var nodeMatch = Regex.Match(elem, @"node=['""]([^'""]+)['""]");
        var verMatch = Regex.Match(elem, @"ver=['""]([^'""]+)['""]");
        var hashMatch = Regex.Match(elem, @"hash=['""]([^'""]+)['""]");

        if (nodeMatch.Success && verMatch.Success)
        {
            return (nodeMatch.Groups[1].Value, verMatch.Groups[1].Value,
                    hashMatch.Success ? hashMatch.Groups[1].Value : null);
        }

        return null;
    }
}
