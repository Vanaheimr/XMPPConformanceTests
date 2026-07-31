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
using System.Xml.Linq;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// XEP-0156: Findet den WebSocket-Endpunkt einer Domain über deren
/// <c>host-meta</c>.
/// </summary>
/// <remarks>
/// Zwei Sätze aus dem XEP bestimmen alles Weitere.
///
/// Der erste ist eine Rangfolge: „HTTPS queries for host-meta information MUST
/// be used only as a fallback after the methods specified in RFC 6120 have been
/// exhausted." Diese Klasse wird deshalb nur gefragt, wenn niemand einen
/// Endpunkt genannt hat.
///
/// Der zweite ist eine Sicherheitsregel: „host-meta files MUST be fetched only
/// over HTTPS, and MUST only use connection URLs starting with 'https://' or
/// 'wss://'." Beide Hälften gehören zusammen. Wer die Auskunft im Klartext
/// holt, lässt jeden Zwischenmann bestimmen, wohin sich der Client anmeldet;
/// wer einer sicher geholten Auskunft ein <c>ws://</c> abnimmt, schickt
/// Benutzer und Passwort anschliessend trotzdem offen durchs Netz.
///
/// Der DNS-Weg über <c>_xmppconnect</c>-TXT-Einträge fehlt nicht, er ist
/// abgeschafft: „A previous version of this XEP defined a DNS method to look up
/// this info using a TXT _xmppconnect record, this was insecure and has been
/// removed."
/// </remarks>
public sealed class AltConnectionsResolver
{

    #region Data

    /// <summary>Der Link-Typ des WebSocket-Endpunkts.</summary>
    public const string WebSocketRel = "urn:xmpp:alt-connections:websocket";

    private const string XrdNamespace = "http://docs.oasis-open.org/ns/xri/xrd-1.0";

    /// <summary>
    /// Ein gemeinsamer HttpClient - einer pro Anwendung, nicht einer pro
    /// Abfrage: Ein neuer je Aufruf verbraucht Sockets, die nach dem Schliessen
    /// noch minutenlang belegt bleiben.
    /// </summary>
    private static readonly HttpClient _httpClient = new();

    private readonly Func<string, CancellationToken, Task<string?>> _fetch;

    #endregion

    #region Constructor(s)

    /// <summary>
    /// Erzeugt einen Resolver.
    /// </summary>
    /// <param name="fetch">
    /// Holt den Inhalt einer Adresse, oder <c>null</c>, wenn es ihn nicht gibt.
    /// Ohne Angabe wird über HTTPS geladen; eingesetzt wird er von den Tests,
    /// die den Ablauf ohne Netz prüfen.
    /// </param>
    public AltConnectionsResolver(Func<string, CancellationToken, Task<string?>>? fetch = null)
    {
        _fetch = fetch ?? FetchOverHttpsAsync;
    }

    #endregion


    /// <summary>
    /// Fragt das <c>host-meta</c> der Domain ab und gibt den ersten
    /// WebSocket-Endpunkt zurück - oder <c>null</c>, wenn es keinen gibt.
    /// </summary>
    /// <remarks>
    /// Erst die JSON-Fassung, dann die XML-Fassung. Das XEP kennt beide
    /// gleichrangig; die Reihenfolge ist eine Wahl und keine Vorschrift.
    /// Gefragt wird die zweite nur, wenn die erste nichts hergibt - eine
    /// Domain, die beides ausliefert, kostet damit eine Abfrage statt zwei.
    /// </remarks>
    public async Task<string?> DiscoverWebSocketAsync(string             domain,
                                                      CancellationToken  ct   = default)
    {

        var jrd = await _fetch($"https://{domain}/.well-known/host-meta.json", ct);

        if (jrd is not null && WebSocketEndpointsFromJrd(jrd) is { Count: > 0 } ausJson)
            return ausJson[0];

        var xrd = await _fetch($"https://{domain}/.well-known/host-meta", ct);

        if (xrd is not null && WebSocketEndpointsFromXrd(xrd) is { Count: > 0 } ausXml)
            return ausXml[0];

        return null;

    }

    /// <summary>
    /// Die WebSocket-Endpunkte aus einem XRD-Dokument, in der vorgefundenen
    /// Reihenfolge.
    /// </summary>
    public static IReadOnlyList<string> WebSocketEndpointsFromXrd(string xrd)
    {

        try
        {

            var wurzel = XDocument.Parse(xrd).Root;

            if (wurzel is null)
                return [];

            return [.. wurzel.Elements(XName.Get("Link", XrdNamespace))
                             .Where  (link => link.Attribute("rel")?.Value == WebSocketRel)
                             .Select (link => link.Attribute("href")?.Value)
                             .Where  (IsSecureWebSocket)
                             .Select (href => href!)];

        }

        // Der Inhalt kommt von einem fremden Webserver und kann alles sein:
        // eine Fehlerseite, eine halbe Datei, HTML. Das ist kein Fehler dieses
        // Programms, sondern eine Domain ohne brauchbares host-meta - und die
        // richtige Antwort darauf ist "kein Endpunkt", nicht ein Abbruch des
        // Verbindungsaufbaus.
        catch (System.Xml.XmlException)
        {
            return [];
        }

    }

    /// <summary>
    /// Die WebSocket-Endpunkte aus einem JRD-Dokument, in der vorgefundenen
    /// Reihenfolge.
    /// </summary>
    public static IReadOnlyList<string> WebSocketEndpointsFromJrd(string jrd)
    {

        try
        {

            using var dokument = JsonDocument.Parse(jrd);

            if (!dokument.RootElement.TryGetProperty("links", out var links) ||
                 links.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var endpunkte = new List<string>();

            foreach (var link in links.EnumerateArray())
            {

                if (link.ValueKind != JsonValueKind.Object)
                    continue;

                if (link.TryGetProperty("rel",  out var rel)  && rel.ValueKind  == JsonValueKind.String &&
                    link.TryGetProperty("href", out var href) && href.ValueKind == JsonValueKind.String &&
                    rel.GetString() == WebSocketRel &&
                    IsSecureWebSocket(href.GetString()))
                {
                    endpunkte.Add(href.GetString()!);
                }

            }

            return endpunkte;

        }

        catch (JsonException)
        {
            return [];
        }

    }


    /// <summary>
    /// Zeigt diese Adresse auf einen TLS-geschützten WebSocket?
    /// </summary>
    /// <remarks>
    /// Das XEP lässt <c>https://</c> und <c>wss://</c> zu; <c>https://</c>
    /// gehört zu BOSH (XEP-0124), das dieser Client nicht spricht. Bliebe es
    /// hier stehen, käme es als WebSocket-Endpunkt zurück und der
    /// Verbindungsaufbau scheiterte an einer Adresse, die gar nicht dafür
    /// gedacht war.
    /// </remarks>
    private static bool IsSecureWebSocket(string? href)

        => href is not null &&
           href.StartsWith("wss://", StringComparison.OrdinalIgnoreCase);


    /// <summary>
    /// Lädt eine Adresse über HTTPS. Alles, was dabei schiefgeht, ist eine
    /// Domain ohne <c>host-meta</c> und kein Fehler.
    /// </summary>
    private static async Task<string?> FetchOverHttpsAsync(string             uri,
                                                           CancellationToken  ct)
    {

        // Die Sicherheitsregel des XEPs, hier und nicht erst beim Ergebnis:
        // Was über http:// käme, dürfte gar nicht erst gelesen werden.
        if (!uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {

            var antwort = await _httpClient.GetAsync(uri, ct);

            if (!antwort.IsSuccessStatusCode)
                return null;

            return await antwort.Content.ReadAsStringAsync(ct);

        }

        catch (HttpRequestException)
        {
            return null;
        }

        catch (TaskCanceledException)
        {
            return null;
        }

    }

}
