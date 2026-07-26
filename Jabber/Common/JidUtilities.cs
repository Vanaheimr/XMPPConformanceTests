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
/// Hilfsfunktionen für JIDs (RFC 6122 / RFC 7622).
///
/// Ersetzt die zuvor in fünf Klassen duplizierten privaten GetBareJid-Helfer.
/// </summary>
/// <remarks>
/// ACHTUNG: <see cref="Bare"/> setzt den kompletten JID auf Kleinschreibung.
/// Nach RFC 7622 sind Localpart und Domainpart case-insensitive, der
/// Resourcepart aber NICHT. Für Bare-JIDs (ohne Resource) ist das Verhalten
/// korrekt; auf Full-JIDs angewandt wäre es falsch. Eine vollständige
/// Implementierung bräuchte PRECIS-Profile (RFC 8264/7622).
/// </remarks>
public static class JidUtilities
{
    /// <summary>
    /// Liefert den Bare-JID (user@domain) in Kleinschreibung.
    /// </summary>
    public static string Bare(string jid)
    {
        var slash = jid.IndexOf('/');
        return (slash > 0 ? jid[..slash] : jid).ToLowerInvariant();
    }
}
