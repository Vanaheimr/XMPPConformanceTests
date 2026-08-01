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

using System.Xml.Linq;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// RFC 6120: die Aushandlung vor der ersten Stanza - Stream-Features, SASL
/// und Resource Binding.
///
/// Bewusst reine Funktionen auf dem geparsten <see cref="XElement"/>: die
/// Aushandlung ist der Teil des Clients, der sich am schlechtesten
/// integrativ prüfen lässt, weil er nur beim Verbindungsaufbau läuft und
/// jeder Fehler dort die Verbindung gleich mitreisst. So sind die
/// Entscheidungen einzeln prüfbar.
/// </summary>
internal static class StreamNegotiation
{

    #region Namespaces

    /// <summary>Namespace der Stream-Ebene (RFC 6120, Abschnitt 4.8.2).</summary>
    public const string StreamNamespace   = "http://etherx.jabber.org/streams";

    /// <summary>Namespace des WebSocket-Framings (RFC 7395).</summary>
    public const string FramingNamespace  = "urn:ietf:params:xml:ns:xmpp-framing";

    /// <summary>Namespace von SASL (RFC 6120, Abschnitt 6).</summary>
    public const string SaslNamespace     = "urn:ietf:params:xml:ns:xmpp-sasl";

    /// <summary>Namespace des Resource Bindings (RFC 6120, Abschnitt 7).</summary>
    public const string BindNamespace     = "urn:ietf:params:xml:ns:xmpp-bind";

    /// <summary>Namespace der Legacy-Session (RFC 3921, in RFC 6121 entfallen).</summary>
    public const string SessionNamespace  = "urn:ietf:params:xml:ns:xmpp-session";

    #endregion

    #region Rahmen der Aushandlung

    /// <summary>
    /// Ist das der Stream-Kopf? Über WebSocket ist das <c>&lt;open/&gt;</c>
    /// nach RFC 7395, über TCP das <c>&lt;stream:stream&gt;</c>.
    /// </summary>
    public static bool IsStreamOpen(XElement element)
        => element.Name.LocalName is "open" or "stream";

    /// <summary>Sind das die Stream-Features?</summary>
    public static bool IsFeatures(XElement element)
        => element.Name.LocalName     == "features" &&
           element.Name.NamespaceName == StreamNamespace;

    #endregion

    #region SASL

    /// <summary>
    /// Ist das ein SASL-Element mit diesem Namen? Der Namespace gehört zur
    /// Prüfung: die frühere Suche nach der Zeichenfolge
    /// <c>&quot;&lt;success&quot;</c> im Rohtext traf auch ein
    /// <c>&lt;success/&gt;</c> einer beliebigen anderen Erweiterung.
    /// </summary>
    public static bool IsSasl(XElement element, string localName)
        => element.Name.LocalName     == localName &&
           element.Name.NamespaceName == SaslNamespace;

    /// <summary>
    /// Der Base64-Inhalt eines <c>&lt;challenge/&gt;</c> oder
    /// <c>&lt;success/&gt;</c>. Leer, wenn das Element keinen trägt - bei
    /// SCRAM ist das ein Fehler und keine Nebensächlichkeit, denn ohne die
    /// server-final-message lässt sich die Serversignatur nicht prüfen.
    /// </summary>
    public static string SaslPayload(XElement element)
        => element.Value.Trim();

    /// <summary>
    /// Die Bedingung eines <c>&lt;failure/&gt;</c>, also der lokale Name des
    /// ersten Kindelements - <c>not-authorized</c>,
    /// <c>invalid-mechanism</c> und so weiter (RFC 6120, Abschnitt 6.5).
    /// </summary>
    public static string? SaslFailureCondition(XElement failure)
        => failure.Elements()
                  .FirstOrDefault(child => child.Name.LocalName != "text")
                  ?.Name.LocalName;

    /// <summary>
    /// Die angebotenen SASL-Mechanismen.
    ///
    /// Das frühere Muster <c>&lt;mechanism&gt;([^&lt;]+)&lt;/mechanism&gt;</c>
    /// verlangte ein Element ganz ohne Attribute und gab den Inhalt
    /// unbeschnitten zurück. Ein Server, der seine Features einrückt oder den
    /// Namespace am Kindelement wiederholt - beides gültig -, sah für den
    /// Client aus wie einer ganz ohne SASL.
    /// </summary>
    public static List<string> SaslMechanisms(XElement features)
    {

        var mechanisms = new List<string>();
        var container  = features.Child(SaslNamespace, "mechanisms");

        if (container is null)
            return mechanisms;

        foreach (var mechanism in container.Elements().Where(e => e.Name.LocalName == "mechanism"))
        {

            var name = mechanism.Value.Trim();

            if (name.Length > 0)
                mechanisms.Add(name);

        }

        return mechanisms;

    }

    #endregion

    #region Angekündigte Features

    /// <summary>
    /// Die Namespaces der angekündigten Features.
    ///
    /// Gelesen werden die direkten Kinder von <c>&lt;features/&gt;</c>. Das
    /// frühere Muster suchte <c>xmlns</c> als erstes Attribut irgendwo im
    /// Text - ein <c>&lt;c hash='sha-1' ver='…' xmlns='…/caps'/&gt;</c> fiel
    /// damit heraus, und genau so serialisiert die BCL: der
    /// Namespace steht am Ende.
    /// </summary>
    public static List<string> FeatureNamespaces(XElement features)
        => features.Elements()
                   .Select(child => child.Name.NamespaceName)
                   .Where(ns => ns.Length > 0)
                   .Distinct()
                   .ToList();

    /// <summary>Bietet der Server Resource Binding an?</summary>
    public static bool OffersBind(XElement features)
        => features.Child(BindNamespace, "bind") is not null;

    /// <summary>Bietet der Server XEP-0198 Stream Management an?</summary>
    public static bool OffersStreamManagement(XElement features)
        => features.Child(StreamManagementManager.Namespace, "sm") is not null;

    /// <summary>
    /// Bietet der Server XEP-0352 Client State Indication an?
    /// </summary>
    /// <remarks>
    /// Ohne diese Ankündigung darf der Client kein <c>&lt;inactive/&gt;</c>
    /// schicken. Der Grund ist nicht Höflichkeit: Ein Server, der die
    /// Erweiterung nicht kennt, sieht ein unbekanntes Element auf Stream-Ebene
    /// - und RFC 6120, Abschnitt 4.9.3.24 lässt ihm dafür einen
    /// Stream-Fehler. Aus einer Sparmassnahme würde ein Verbindungsabbruch.
    /// </remarks>
    public static bool OffersClientStateIndication(XElement features)
        => features.Child(ClientStateIndication.Namespace, "csi") is not null;

    /// <summary>
    /// Bietet der Server Roster-Versionierung an (RFC 6121, Abschnitt 2.6.1)?
    /// </summary>
    /// <remarks>
    /// Ohne diese Ankündigung darf ein Client kein <c>ver</c> an seine
    /// Roster-Anfrage hängen. Der Grund ist nicht Höflichkeit: Ein Server ohne
    /// Versionierung übergeht das Attribut und antwortet mit dem vollen
    /// Roster - das ginge noch. Gefährlich wäre der umgekehrte Fall, dass ein
    /// leeres Ergebnis als „unverändert" gelesen wird, wo es „leerer Roster"
    /// heisst.
    /// </remarks>
    public static bool OffersRosterVersioning(XElement features)
        => features.Child("urn:xmpp:features:rosterver", "ver") is not null;

    /// <summary>
    /// Muss die Legacy-Session (RFC 3921) angefordert werden?
    ///
    /// Die frühere Prüfung war <c>Contains("&lt;session")</c> und
    /// <c>!Contains("optional")</c> auf dem ganzen Rahmen. Das
    /// <c>&lt;optional/&gt;</c> gehört aber jeweils zu genau einem Feature,
    /// und XEP-0198 setzt es in sein eigenes: bei einem Server, der
    /// <c>&lt;sm&gt;&lt;optional/&gt;&lt;/sm&gt;</c> ankündigt, unterblieb
    /// die zwingende Session.
    /// </summary>
    public static bool RequiresSession(XElement features)
    {

        var session = features.Child(SessionNamespace, "session");

        if (session is null)
            return false;

        return !session.Elements().Any(child => child.Name.LocalName == "optional");

    }

    #endregion

    #region Resource Binding

    /// <summary>
    /// Der zugeteilte Full-JID aus der Bind-Antwort, oder null, wenn die
    /// Antwort kein Ergebnis ist oder keinen JID trägt.
    ///
    /// Dass null hier wirklich "abgelehnt" heisst und nicht "such halt
    /// weiter", ist der Punkt: früher suchte ein
    /// <c>&lt;jid&gt;([^&lt;]+)&lt;/jid&gt;</c> im Rohtext, und blieb es
    /// erfolglos, nahm der Client den selbst gewünschten JID an. Ein
    /// abgelehntes Binding war von einem erfolgreichen nicht zu
    /// unterscheiden.
    /// </summary>
    public static string? ReadBoundJid(XElement iq)
    {

        if (iq.Name.LocalName != "iq" || iq.Attr("type") != "result")
            return null;

        var jid = iq.Child(BindNamespace, "bind")?.ChildValue("jid")?.Trim();

        return string.IsNullOrEmpty(jid) ? null : jid;

    }

    #endregion

}
