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

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// SCRAM-SHA-1 und SCRAM-SHA-256 Authentifizierung (RFC 5802)
///
/// Ablauf:
/// 1. Client → Server: client-first-message (username, client nonce)
/// 2. Server → Client: server-first-message (combined nonce, salt, iterations)
/// 3. Client → Server: client-final-message (channel binding, proof)
/// 4. Server → Client: server-final-message (server signature)
/// </summary>
public sealed class SCRAMAuthenticator
{
    private readonly string _username;
    private readonly string _password;
    private readonly SCRAMMechanism _mechanism;

    private string? _clientNonce;
    private string? _clientFirstMessageBare;
    private string? _serverFirstMessage;
    private byte[]? _saltedPassword;

    public SCRAMAuthenticator(string username, string password, SCRAMMechanism mechanism = SCRAMMechanism.ScramSha1)
    {
        _username = SaslPrep(username);
        _password = SaslPrep(password);
        _mechanism = mechanism;
    }

    /// <summary>
    /// Nur für Tests: erzwingt einen festen Client-Nonce, damit sich die
    /// Testvektoren aus RFC 5802 Abschnitt 5 und RFC 7677 Abschnitt 3
    /// nachrechnen lassen. Im Betrieb bleibt der Wert null und der Nonce
    /// kommt aus <see cref="RandomNumberGenerator"/>.
    /// </summary>
    internal string? FixedClientNonce { get; set; }

    public string MechanismName => _mechanism switch
    {
        SCRAMMechanism.ScramSha1 => "SCRAM-SHA-1",
        SCRAMMechanism.ScramSha256 => "SCRAM-SHA-256",
        _ => "SCRAM-SHA-1"
    };

    /// <summary>
    /// Schritt 1: Generiert die client-first-message
    /// </summary>
    public string CreateClientFirstMessage()
    {
        _clientNonce = FixedClientNonce ?? GenerateNonce();

        // n=username,r=nonce
        _clientFirstMessageBare = $"n={EscapeUsername(_username)},r={_clientNonce}";

        // GS2 Header: n,, (no channel binding, no authzid)
        // Vollständige Nachricht: n,,n=user,r=nonce
        var clientFirstMessage = $"n,,{_clientFirstMessageBare}";

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(clientFirstMessage));
    }

    /// <summary>
    /// Schritt 2: Verarbeitet server-first-message und generiert client-final-message
    /// </summary>
    public string ProcessServerFirstMessage(string serverFirstMessageBase64)
    {
        _serverFirstMessage = Encoding.UTF8.GetString(Convert.FromBase64String(serverFirstMessageBase64));

        // Parse: r=combinedNonce,s=salt,i=iterations
        var serverNonce = ExtractValue(_serverFirstMessage, "r");
        var saltBase64 = ExtractValue(_serverFirstMessage, "s");
        var iterationsStr = ExtractValue(_serverFirstMessage, "i");

        if (serverNonce == null || saltBase64 == null || iterationsStr == null)
            throw new AuthenticationException($"Ungültige server-first-message: {_serverFirstMessage}");

        // Verify nonce starts with client nonce
        if (!serverNonce.StartsWith(_clientNonce!))
            throw new AuthenticationException("Server nonce enthält nicht den Client nonce - möglicher MITM-Angriff!");

        var salt = Convert.FromBase64String(saltBase64);
        var iterations = int.Parse(iterationsStr);

        // Berechne SaltedPassword = Hi(password, salt, iterations)
        _saltedPassword = Hi(_password, salt, iterations);

        // ClientKey = HMAC(SaltedPassword, "Client Key")
        var clientKey = HmacCompute(_saltedPassword, "Client Key");

        // StoredKey = H(ClientKey)
        var storedKey = HashCompute(clientKey);

        // channel-binding-data = base64("n,,")
        var channelBinding = Convert.ToBase64String(Encoding.UTF8.GetBytes("n,,"));

        // client-final-message-without-proof = c=channelBinding,r=serverNonce
        var clientFinalWithoutProof = $"c={channelBinding},r={serverNonce}";

        // AuthMessage = client-first-message-bare + "," + server-first-message + "," + client-final-without-proof
        var authMessage = $"{_clientFirstMessageBare},{_serverFirstMessage},{clientFinalWithoutProof}";

        // ClientSignature = HMAC(StoredKey, AuthMessage)
        var clientSignature = HmacCompute(storedKey, authMessage);

        // ClientProof = ClientKey XOR ClientSignature
        var clientProof = XOR(clientKey, clientSignature);

        // client-final-message = client-final-without-proof + ",p=" + base64(ClientProof)
        var clientFinalMessage = $"{clientFinalWithoutProof},p={Convert.ToBase64String(clientProof)}";

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(clientFinalMessage));
    }

    /// <summary>
    /// Schritt 3: Verifiziert die server-final-message
    /// </summary>
    public bool VerifyServerFinalMessage(string serverFinalMessageBase64)
    {
        var serverFinalMessage = Encoding.UTF8.GetString(Convert.FromBase64String(serverFinalMessageBase64));

        // Check for error
        if (serverFinalMessage.StartsWith("e="))
        {
            var error = ExtractValue(serverFinalMessage, "e");
            throw new AuthenticationException($"SCRAM Fehler: {error}");
        }

        // Parse: v=serverSignature
        var serverSignatureBase64 = ExtractValue(serverFinalMessage, "v");
        if (serverSignatureBase64 == null)
            throw new AuthenticationException($"Ungültige server-final-message: {serverFinalMessage}");

        var receivedSignature = Convert.FromBase64String(serverSignatureBase64);

        // Berechne erwartete ServerSignature
        // ServerKey = HMAC(SaltedPassword, "Server Key")
        var serverKey = HmacCompute(_saltedPassword!, "Server Key");

        // Rekonstruiere AuthMessage
        var channelBinding = Convert.ToBase64String(Encoding.UTF8.GetBytes("n,,"));
        var serverNonce = ExtractValue(_serverFirstMessage!, "r");
        var clientFinalWithoutProof = $"c={channelBinding},r={serverNonce}";
        var authMessage = $"{_clientFirstMessageBare},{_serverFirstMessage},{clientFinalWithoutProof}";

        // ServerSignature = HMAC(ServerKey, AuthMessage)
        var expectedSignature = HmacCompute(serverKey, authMessage);

        // Constant-time comparison
        return CryptographicOperations.FixedTimeEquals(receivedSignature, expectedSignature);
    }

    // ===== Crypto Helpers =====

    private byte[] Hi(string password, byte[] salt, int iterations)
    {
        // PBKDF2 mit SHA-1 oder SHA-256
        var hashName = _mechanism == SCRAMMechanism.ScramSha256
            ? HashAlgorithmName.SHA256
            : HashAlgorithmName.SHA1;

        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            hashName,
            hashName == HashAlgorithmName.SHA256 ? 32 : 20
        );
    }

    private byte[] HmacCompute(byte[] key, string data)
    {
        return HmacCompute(key, Encoding.UTF8.GetBytes(data));
    }

    private byte[] HmacCompute(byte[] key, byte[] data)
    {
        using var hmac = _mechanism == SCRAMMechanism.ScramSha256
            ? (HMAC)new HMACSHA256(key)
            : new HMACSHA1(key);

        return hmac.ComputeHash(data);
    }

    private byte[] HashCompute(byte[] data)
    {
        return _mechanism == SCRAMMechanism.ScramSha256
            ? SHA256.HashData(data)
            : SHA1.HashData(data);
    }

    private static byte[] XOR(byte[] a, byte[] b)
    {
        var result = new byte[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            result[i] = (byte)(a[i] ^ b[i]);
        }
        return result;
    }

    private static string GenerateNonce()
    {
        var bytes = new byte[24];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string? ExtractValue(string message, string key)
    {
        // Verankert am Anfang oder hinter einem Komma: sonst trifft die Suche
        // nach "i=" auch ein 'i=' innerhalb der Nonce oder des Salts (RFC 5802
        // erlaubt dort jedes druckbare Zeichen ausser dem Komma).
        var match = Regex.Match(message, $@"(?:^|,){key}=([^,]+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string EscapeUsername(string username)
    {
        // RFC 5802: = → =3D, , → =2C
        return username
            .Replace("=", "=3D")
            .Replace(",", "=2C");
    }

    /// <summary>
    /// RFC 5802, Abschnitt 5.1: Benutzername und Passwort gehen durch
    /// SASLprep, bevor daraus ein Schlüssel wird.
    /// </summary>
    private static string SaslPrep(string input)
        => XMPP.SaslPrep.Prepare(input);
}
