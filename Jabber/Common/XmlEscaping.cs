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
/// Escaping von XML-Sonderzeichen für Attributwerte und Textinhalte.
///
/// Ersetzt die zuvor in sechs Klassen duplizierten privaten XmlEscape-Helfer.
/// Die alten Kopien in PingManager, DiscoManager und ChatMarkers haben das
/// doppelte Anführungszeichen nicht escaped - das war für die intern erzeugten
/// Stanzas (alle Attribute mit single quotes) unkritisch, aber inkonsistent.
/// </summary>
public static class XmlEscaping
{
    public static string Escape(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("'", "&apos;")
            .Replace("\"", "&quot;");
}
