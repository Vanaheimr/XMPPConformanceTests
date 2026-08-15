/*
 * Copyright (c) 2010-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of Ratatoskr <https://www.github.com/Vanaheimr/Ratatoskr>
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

using System.Text;
using System.Text.Json;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Ratatoskr;

// System.ComponentModel and System.Diagnostics stood here until D107, for the
// Win32Exception and the ProcessStartInfo of the oracle call - and with them an
// alias, because System.ComponentModel brings a CategoryAttribute of its own
// and NUnit's [Category] is ambiguous against it (CS0104). Starting the far
// side moved to ForeignSide, so both are gone and the alias with them: the
// three lines of workaround were the last thing the duplicated process handling
// was still costing this file.

#endregion

namespace org.GraphDefined.Vanaheimr.Ratatoskr.Tests
{

    /// <summary>
    /// OMEMO against the reference implementation - python-omemo (Syndace),
    /// the same version `urn:xmpp:omemo:2`.
    /// </summary>
    /// <remarks>
    /// <b>This collection cannot find one class of errors on principle.</b> If
    /// both sides are the same code, they agree even when both calculate
    /// wrongly in the same way. In D62 to D65 that was the finding five times -
    /// an info string, an order, an embedding. Every time two clients of this
    /// house would have understood each other perfectly and not a single
    /// foreign one.
    ///
    /// The only thing that helps against that is a far end nobody here wrote.
    ///
    /// <b>These tests skip themselves</b> when the oracle is not reachable -
    /// like the tests against Prosody and ejabberd. A run without python-omemo
    /// is not supposed to be red, only to say less. How many are skipped says
    /// afterwards what was measured.
    ///
    /// Where the oracle runs depends on where the tests run: on Windows in
    /// WSL, because python-omemo is not a Windows library, and on Linux -
    /// developer's WSL, container in CI - directly. Only the detour is
    /// platform-bound; the reference implementation is the same one either
    /// way.
    ///
    /// What is checked is everything from the payload downwards: bundle
    /// format, X3DH, the beginning of the ratchet, the wire format. The SCE
    /// envelope stays out of it - python-omemo leaves it to the application
    /// using it.
    /// </remarks>
    // The whole fixture needs the far side, so the category sits here. Since
    // d39656e a missing one skips instead of throwing out of [OneTimeSetUp],
    // which is what makes the category a selector rather than a shield: the
    // gate excludes these because they cannot measure anything without
    // python-omemo, not because they would go red.
    [TestFixture]
    [Category(TestCategories.Wsl)]
    [Category(TestCategories.Omemo)]
    public class OmemoOracleTests
    {

        #region Calling the oracle

        private const String LibPath     = "/tmp/omemo-oracle/lib";

        /// <summary>
        /// The oracle, relative to the test assembly.
        /// </summary>
        /// <remarks>
        /// The csproj copies `XEPs/Oracle/**` into the output directory, and
        /// that copy is what makes this path hold at all. Before it, the file
        /// was searched for by walking upwards from the output until precisely
        /// it lay there - and that walk finds nothing the moment the output
        /// lies outside the repository. Which is exactly what the run both
        /// setup scripts print does: `--artifacts-path /tmp/conformance-artifacts`
        /// keeps the Linux build out of the Windows tree, so the documented
        /// command turned three tests red with "the oracle is not to be found"
        /// while the checkout was perfectly fine.
        ///
        /// The walk stays behind the copy as a fallback, for an output
        /// directory built before it existed.
        ///
        /// Until D97 the walk went by `WORKPLAN.md` instead, and the path
        /// pointed at `Jabber.Tests/` (this suite under its name of back then):
        /// both belonged to the program, not to the library, and both were
        /// wrong here after the move. Ratatoskr has to be able to run its own
        /// tests even when nobody has checked this repository out next to it.
        /// </remarks>
        private static readonly String ScriptPath = Path.Combine("XEPs", "Oracle", "omemo_oracle.py");

        private static String? _reasonForSkipping;

        [OneTimeSetUp]
        public void CheckTheOracle()
        {

            var (code, _, errors) = Call("bundle", null, check: false);

            if (code != 0)
                _reasonForSkipping =
                    $"The oracle is not reachable (python-omemo under {LibPath}" +
                    (OperatingSystem.IsLinux() ? "" : ", in WSL") + "): " +
                    $"{errors.Split('\n').LastOrDefault(line => line.Trim().Length > 0)?.Trim()}";

        }

        [SetUp]
        public void SkipIfNeeded()
        {
            if (_reasonForSkipping is not null)
                Assert.Ignore(_reasonForSkipping);
        }

        /// <summary>
        /// Where the oracle lies: next to the test assembly, where the csproj
        /// copies it - and otherwise upwards from there.
        /// </summary>
        private static String? WhereTheOracleLies()
        {

            var beside = Path.Combine(AppContext.BaseDirectory, ScriptPath);

            if (File.Exists(beside))
                return beside;

            var root = new DirectoryInfo(AppContext.BaseDirectory);

            while (root is not null && !File.Exists(Path.Combine(root.FullName, ScriptPath)))
                root = root.Parent;

            return root is not null
                       ? Path.Combine(root.FullName, ScriptPath)
                       : null;

        }

        /// <summary>
        /// Starts the oracle and returns what it said.
        /// </summary>
        /// <remarks>
        /// The job goes over a file and not over the command line: a bundle
        /// with a hundred PreKeys blows every line length, and base64 in
        /// quotation marks across two operating system borders is a source of
        /// errors nobody needs.
        ///
        /// Where the far side lives, and how a path reaches it, is
        /// <see cref="ForeignSide"/>'s business. This fixture carried its own
        /// copy of that split until D107 - the third of them, which is what
        /// <see cref="TestEnvironment"/> had said would be one too many. What
        /// kept it here longest was not the split itself but
        /// <see cref="PathOver"/> below: the state file of
        /// <see cref="TheReferenceCanReadWhatWeWrote"/> travels *inside* the
        /// job, as a value in JSON, where an argument list cannot help it and
        /// only a translated path will do.
        ///
        /// A missing interpreter comes back as a non-zero code rather than as
        /// an exception, and that matters here beyond tidiness: what leaves
        /// <c>[OneTimeSetUp]</c> as an exception turns the whole fixture red,
        /// and on Linux - where `wsl` is no program at all - that used to mean
        /// three failures where three skips belonged. That guarantee now sits
        /// in <see cref="ForeignSide.Run"/>, where the other callers get it too.
        /// </remarks>
        private static (Int32 Code, String Output, String Errors) Call(String   mode,
                                                                       Object?  job,
                                                                       Boolean  check = true)
        {

            var script = WhereTheOracleLies();

            // No Assert.Ignore: the oracle lies in this project. If it is
            // missing, the checkout is broken - and a broken checkout is
            // supposed to be red and not skipped. What is skipped is only the
            // case where python-omemo is missing.
            Assert.That(script, Is.Not.Null,
                        $"The oracle is not to be found: '{ScriptPath}' lies neither next to " +
                        $"'{AppContext.BaseDirectory}' nor in any directory above it.");

            String? jobFile = null;

            try
            {

                if (job is not null)
                {
                    jobFile = Path.Combine(Path.GetTempPath(), $"orakel-{Guid.NewGuid():N}.json");
                    File.WriteAllText(jobFile, JsonSerializer.Serialize(job));
                }

                var command = $"PYTHONPATH={LibPath} python3 '{PathOver(script!)}'" +
                              $" {mode}" +
                              (jobFile is not null ? $" '{PathOver(jobFile)}'" : "");

                var (code, output, errors) = ForeignSide.Run(command);

                if (check && code != 0)
                    Assert.Fail($"The oracle failed in mode '{mode}':\n{errors}");

                return (code, output, errors);

            }
            finally
            {
                if (jobFile is not null)
                    try { File.Delete(jobFile); } catch { /* does not matter */ }
            }

        }

        /// <summary>
        /// A path as the oracle sees it.
        /// </summary>
        /// <remarks>
        /// <see cref="ForeignSide.PathOver"/> under a name that says what it is
        /// for here. It is kept as a name of its own because the jobs use it in
        /// a place a reader does not expect: not only for the arguments, but
        /// for the state file that travels as a value inside the JSON of
        /// <see cref="TheReferenceCanReadWhatWeWrote"/>, where no argument list
        /// can translate anything.
        /// </remarks>
        private static String PathOver(String path)
            => ForeignSide.PathOver(path);

        private static JsonElement Reply(String output)
            => JsonDocument.Parse(output.Trim()).RootElement;

        private static String B64(Byte[] data)
            => Convert.ToBase64String(data);

        #endregion

        #region Our bundle as a job

        /// <summary>
        /// Our bundle in the shape the oracle expects.
        /// </summary>
        private static Object AsJob(OmemoIdentity own, String jid, String? plaintext = null)
        {

            var bundle = own.Bundle();

            return new Dictionary<String, Object?> {
                ["jid"]                 = jid,
                ["device_id"]           = own.DeviceId,
                ["identity_key"]        = B64(bundle.IdentityKey),
                ["signed_pre_key_id"]   = bundle.SignedPreKeyId,
                ["signed_pre_key"]      = B64(bundle.SignedPreKey),
                ["signed_pre_key_sig"]  = B64(bundle.SignedPreKeySignature),
                ["pre_keys"]            = bundle.PreKeys
                                                .Take(10)
                                                .Select(p => new Dictionary<String, Object> {
                                                            ["id"]   = p.Id,
                                                            ["key"]  = B64(p.PublicKey)
                                                        })
                                                .ToList(),
                ["plaintext"]           = plaintext
            };

        }

        #endregion


        #region TheReferenceAcceptsOurBundle()

        /// <summary>
        /// The reference implementation takes our bundle - <b>and checks the
        /// signature over the signed PreKey itself while doing so</b>.
        /// </summary>
        /// <remarks>
        /// That was an unchecked assumption from D63, expressly noted as such:
        /// the signed PreKey is signed in Montgomery form. Section 5.3.2 says
        /// only "the signed PreKey signature" and leaves the encoding open.
        /// <b>Here it is decided whether the reading holds</b> - a foreign
        /// library checks the signature with its own idea of what it goes over.
        /// </remarks>
        [Test]
        public void TheReferenceAcceptsOurBundle()
        {

            var own = OmemoIdentity.Create();

            var (code, output, errors) = Call("encrypt",
                                              AsJob(own, "us@example.org", "Sample"),
                                               check: false);

            Assert.That(code, Is.EqualTo(0),
                        "The reference implementation refused our bundle. If there is talk here " +
                        "of an invalid signature, we sign the signed PreKey over something other " +
                        "than what it expects - the unchecked assumption from " +
                        $"D63:\n{errors}");

            Assert.That(Reply(output).GetProperty("key").GetString(), Is.Not.Empty);

        }

        #endregion

        #region WeCanReadWhatTheReferenceWrote()

        /// <summary>
        /// <b>The test this stage exists for:</b> the reference implementation
        /// encrypts, we decrypt.
        /// </summary>
        /// <remarks>
        /// What is checked here all at once, and that against foreign code: the
        /// encoding of the bundle, the order of the four Diffie-Hellmans, the
        /// info string of X3DH, the 0xFF prefix, the addition out of both
        /// identity keys, the beginning of the ratchet, the info strings of the
        /// root chain and of the message key, the constants 0x01/0x02, the
        /// protobuf field numbers, the embedding of the ciphertext into the
        /// message, the truncation of the HMAC and the derivation of the
        /// payload.
        ///
        /// <b>Every single one of these points was a surviving mutation or a
        /// find while reading in D62 to D65.</b> This one test would have found
        /// them all.
        /// </remarks>
        [Test]
        public void WeCanReadWhatTheReferenceWrote()
        {

            const String secret = "Written by the reference implementation";

            var own  = OmemoIdentity.Create();
            var jid  = "us@example.org";

            var (_, output, _) = Call("encrypt", AsJob(own, jid, secret));
            var reply         = Reply(output);

            // What is checked is on the layer the oracle covers: from the key
            // exchange to the payload. The SCE envelope stays out of it -
            // python-omemo leaves it to the application, and an envelope I
            // would build in the oracle myself would be no foreign check but
            // the same assumption twice.
            var exchange = OmemoKeyExchange.Decode(
                               Convert.FromBase64String(reply.GetProperty("key").GetString()!));

            var x3dh = X3DH.Accept(own,
                                   exchange.IdentityKey,
                                   exchange.EphemeralKey,
                                   exchange.SignedPreKeyId,
                                   exchange.PreKeyId == 0 ? null : exchange.PreKeyId);

            var ratchet = DoubleRatchet.InitiateAsReceiver(x3dh.SharedSecret, own.SignedPreKey);

            var keyAndHmac = ratchet.Decrypt(
                                 OmemoWireFormat.Decode(exchange.Message),
                                        x3dh.AssociatedData);

            var plaintext = OmemoPayloadCipher.Decrypt(
                                Convert.FromBase64String(reply.GetProperty("payload").GetString()!),
                               keyAndHmac);

            Assert.That(Encoding.UTF8.GetString(plaintext), Is.EqualTo(secret),
                        "What the reference implementation wrote, we could not read.");

        }

        #endregion

        #region TheReferenceCanReadWhatWeWrote()

        /// <summary>
        /// The reverse direction: <b>we</b> encrypt, the reference
        /// implementation reads.
        /// </summary>
        /// <remarks>
        /// <b>That is the direction deciding whether anybody can read us.</b>
        /// The forward direction checks whether we understand foreign messages;
        /// only this one checks whether ours are understood - and that is the
        /// question a client fails at without anybody noticing: whoever never
        /// gets an answer does not know whether nobody wanted to write or
        /// nobody could read.
        ///
        /// What is checked on top of that is our encoding of the key exchange:
        /// the library separates both parts out of our
        /// <c>&lt;key kex='true'/&gt;</c> - the exchange and the packed-in
        /// message. If that succeeds, our field numbers hold.
        /// </remarks>
        [Test]
        public void TheReferenceCanReadWhatWeWrote()
        {

            const String secret = "Written by us, read by the reference";

            var state = Path.Combine(Path.GetTempPath(), $"orakel-state-{Guid.NewGuid():N}.json");

            try
            {

                // 1. The oracle hands its bundle out - and remembers its keys
                //    in a file, otherwise the second call would be a different
                //    device.
                var (_, bundleOutput, _) = Call("bundle", new Dictionary<String, Object> {
                                                              ["state"] = PathOver(state)
                                                           });

                var b = Reply(bundleOutput);

                var bundle = new OmemoBundle(
                                 Convert.FromBase64String(b.GetProperty("identity_key").GetString()!),
                                 b.GetProperty("signed_pre_key_id").GetUInt32(),
                                 Convert.FromBase64String(b.GetProperty("signed_pre_key").GetString()!),
                                 Convert.FromBase64String(b.GetProperty("signed_pre_key_sig").GetString()!),
                                 [.. b.GetProperty("pre_keys").EnumerateArray()
                                      .Select(p => new OmemoPreKey(
                                                       p.GetProperty("id").GetUInt32(),
                                                       Convert.FromBase64String(p.GetProperty("key").GetString()!)))]);

                // And a check falls due here already: we recalculate the
                // signature of the reference.
                Assert.That(bundle.SignatureIsValid(), Is.True,
                            "We consider the signature of the reference implementation invalid - " +
                            "then we check over something other than what it signs.");

                // 2. We encrypt against it.
                var own      = OmemoIdentity.Create();
                var x3dh     = X3DH.Initiate(own, bundle);
                var ratchet  = DoubleRatchet.InitiateAsSender(x3dh.SharedSecret, bundle.SignedPreKey);
                var payload  = OmemoPayloadCipher.Encrypt(Encoding.UTF8.GetBytes(secret));
                var content  = ratchet.Encrypt(payload.KeyAndHmac, x3dh.AssociatedData);

                var exchange = new OmemoKeyExchange(x3dh.UsedPreKeyId ?? 0,
                                                    bundle.SignedPreKeyId,
                                                     own.PublicIdentityKey,
                                                     x3dh.EphemeralKey!,
                                                     OmemoWireFormat.Encode(content));

                // 3. The oracle reads.
                var (code, output, errors) = Call("decrypt",
                                                  new Dictionary<String, Object> {
                                                       ["state"]             = PathOver(state),
                                                       ["key"]               = B64(exchange.Encode()),
                                                       ["payload"]           = B64(payload.Ciphertext),
                                                       ["sender_jid"]        = "us@example.org",
                                                       ["sender_device_id"]  = (Int32) own.DeviceId
                                                   },
                                                   check: false);

                Assert.That(code, Is.EqualTo(0),
                            $"The reference implementation could not read our message:\n{errors}");

                Assert.That(Reply(output).GetProperty("plaintext").GetString(), Is.EqualTo(secret));

            }
            finally
            {
                try { File.Delete(state); } catch { /* does not matter */ }
            }

        }

        #endregion

    }

}
