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

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Ratatoskr;

#endregion

namespace org.GraphDefined.Vanaheimr.Ratatoskr.Tests
{

    /// <summary>
    /// OMEMO Media Sharing (XEP-0454) against an encryptor nobody here wrote.
    /// </summary>
    /// <remarks>
    /// <see cref="AesGcmUrl"/> has its own tests in Ratatoskr, and they check
    /// it against itself. That is enough for the arithmetic and not enough for
    /// the one thing at stake here: **the layout of the fragment is contested
    /// between implementations**, and a suite that encrypts and decrypts with
    /// the same code agrees with itself no matter which reading it picked. In
    /// D62 to D65 that exact shape - both sides computing the same wrong thing -
    /// was the finding five times over.
    ///
    /// <para>
    /// So the material comes from `cryptography` (pyca) through
    /// <c>XEPs/Oracle/aesgcm_oracle.py</c>, and it costs no new dependency: the
    /// OMEMO oracle already fetches that package because xeddsa needs it.
    /// </para>
    ///
    /// <para>
    /// What this does <b>not</b> check, and deliberately: the servers. An
    /// <c>aesgcm://</c> link is text in a body as far as Prosody and ejabberd
    /// are concerned - the ciphertext lies on an HTTP upload service (XEP-0363,
    /// which Ratatoskr does not implement) and the decryption never leaves the
    /// client. That a body arrives byte for byte is already shown by the
    /// federation lane; measuring it again under this name would be a green run
    /// that says something it did not test.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Wsl)]
    [Category(TestCategories.Omemo)]
    public class AesGcmUrlOracleTests
    {

        #region Calling the oracle

        private const  String  LibPath     = "/tmp/omemo-oracle/lib";
        private static readonly String ScriptPath = Path.Combine("XEPs", "Oracle", "aesgcm_oracle.py");

        private static String? _reasonForSkipping;

        [OneTimeSetUp]
        public void CheckTheOracle()
        {

            var (code, _, errors) = Call("probe", null, check: false);

            if (code != 0)
                _reasonForSkipping =
                    $"The XEP-0454 oracle is not reachable (cryptography under {LibPath}" +
                    (ForeignSide.IsHere ? "" : ", in WSL") + "): " +
                    $"{errors.Split('\n').LastOrDefault(line => line.Trim().Length > 0)?.Trim()}";

        }

        [SetUp]
        public void SkipIfNeeded()
        {
            if (_reasonForSkipping is not null)
                Assert.Ignore(_reasonForSkipping);
        }

        /// <summary>
        /// Runs the oracle and returns what it said.
        /// </summary>
        /// <remarks>
        /// The script is looked for next to the assembly, where the csproj
        /// copies <c>XEPs/Oracle/**</c>. A missing one is RED and not skipped:
        /// it lies in this project, so its absence is a broken checkout and not
        /// a property of the environment. Only a missing `cryptography` skips.
        /// That is D97's rule, and d39656e is what happens when a script is
        /// looked for by walking up from an output directory instead.
        /// </remarks>
        private static (Int32 Code, String Output, String Errors) Call(String   mode,
                                                                       Object?  job,
                                                                       Boolean  check = true)
        {

            var script = Path.Combine(AppContext.BaseDirectory, ScriptPath);

            Assert.That(File.Exists(script), Is.True,
                        $"The oracle is not to be found: '{script}' does not exist. " +
                        $"The csproj copies XEPs/Oracle/** next to the assembly; if it is " +
                        $"missing, the checkout is broken.");

            String? jobFile = null;

            if (job is not null)
            {
                jobFile = Path.Combine(Path.GetTempPath(), $"aesgcm-{Guid.NewGuid():N}.json");
                File.WriteAllText(jobFile, JsonSerializer.Serialize(job));
            }

            var command = $"PYTHONPATH={LibPath} python3 '{ForeignSide.PathOver(script)}' {mode}" +
                          (jobFile is not null ? $" '{ForeignSide.PathOver(jobFile)}'" : "");

            var (code, output, errors) = ForeignSide.Run(command);

            if (jobFile is not null)
                try { File.Delete(jobFile); } catch { /* does not matter */ }

            if (check && code != 0)
                Assert.Fail($"The oracle failed in mode '{mode}':\n{errors}");

            return (code, output, errors);

        }

        private static JsonElement Encrypt(String   plaintext,
                                           Int32    nonceLength = 12,
                                           Int32    keyLength   = 32)
        {

            var (_, output, _) = Call("encrypt", new Dictionary<String, Object> {
                                                     { "plaintext",    plaintext   },
                                                     { "nonce_length", nonceLength },
                                                     { "key_length",   keyLength   }
                                                 });

            return JsonDocument.Parse(output.Trim()).RootElement;

        }

        private static Byte[] Hex(JsonElement element, String name)
            => Convert.FromHexString(element.GetProperty(name).GetString()!);

        #endregion


        #region 1. The round trip

        /// <summary>
        /// What a foreign implementation encrypted, we read back byte for byte.
        /// </summary>
        /// <remarks>
        /// The whole chain in one: the fragment carries IV then key, the stored
        /// file is ciphertext followed by the 16 byte tag, and the plaintext
        /// comes out unchanged. Every one of those is a convention rather than
        /// a calculation, and a convention is exactly what two implementations
        /// can hold differently while both being internally consistent.
        /// </remarks>
        [Test]
        public void WeReadWhatTheReferenceWrote()
        {

            var written    = "Alles was zählt: äöüß, 🎺, and a NUL-free body.";
            var reply      = Encrypt(written);

            var url        = new Uri(reply.GetProperty("url").GetString()!);
            var payload    = Hex(reply, "payload");

            Assert.That(AesGcmUrl.IsAesGcmUrl(url), Is.True,
                        "The oracle's URL was not recognised as an aesgcm:// URL at all.");

            Assert.That(AesGcmUrl.TryParse(url, out var key, out var nonce, out var problem), Is.True,
                        $"The fragment a foreign implementation wrote could not be read: {problem}");

            Assert.Multiple(() => {

                Assert.That(key,   Is.EqualTo(Hex(reply, "key")),
                            "The key was not taken out of the fragment the way it was put in.");

                Assert.That(nonce, Is.EqualTo(Hex(reply, "nonce")),
                            "The nonce was not taken out of the fragment the way it was put in - " +
                            "which is what an IV/key order the other way round looks like.");

            });

            var read = AesGcmUrl.Decrypt(payload, key!, nonce!);

            Assert.That(Encoding.UTF8.GetString(read), Is.EqualTo(written),
                        "The plaintext did not survive the round trip.");

            // The URL that would actually be fetched carries no key any more.
            Assert.That(AesGcmUrl.ToHttps(url).Fragment, Is.Empty,
                        "The https:// URL still carried the fragment, and with it the key.");

        }

        #endregion

        #region 2. The older reading

        /// <summary>
        /// A 16 byte IV is refused, and says so.
        /// </summary>
        /// <remarks>
        /// This is the contested point, and the reason this fixture exists.
        /// XEP-0454 was read as a 16 byte IV for years - older Conversations
        /// sent it that way - and <see cref="AesGcmUrl"/> takes only 12,
        /// because .NET's <see cref="System.Security.Cryptography.AesGcm"/>
        /// accepts no other nonce length.
        ///
        /// <b>The point is not that we refuse it. The point is that we refuse it
        /// rather than misreading it.</b> A 16+32 fragment is 96 hex characters
        /// where 12+32 is 88; a reader that simply took the first 12 bytes would
        /// find a well-formed key and nonce, fail at the tag, and blame the
        /// file. So the test pins the boundary: `false` with a stated problem,
        /// never a silent misread, and never an exception.
        /// </remarks>
        [Test]
        public void TheOlderSixteenByteIvIsRefusedAndNotMisread()
        {

            var reply  = Encrypt("A file from a client that reads the XEP the old way.",
                                 nonceLength: 16);

            var url    = new Uri(reply.GetProperty("url").GetString()!);

            Assert.That(AesGcmUrl.IsAesGcmUrl(url), Is.True,
                        "It is still an aesgcm:// URL - only its fragment is the older shape.");

            var read = AesGcmUrl.TryParse(url, out var key, out var nonce, out var problem);

            Assert.Multiple(() => {

                Assert.That(read,    Is.False, "A 16 byte IV was accepted, which AesGcm cannot use.");
                Assert.That(key,     Is.Null,  "A key was handed out although the fragment was refused.");
                Assert.That(nonce,   Is.Null,  "A nonce was handed out although the fragment was refused.");
                Assert.That(problem, Is.Not.Null.And.Not.Empty,
                            "It was refused without saying why - and 'the file is broken' is what " +
                            "the user would otherwise conclude.");

            });

        }

        #endregion

        #region 3. The tag

        /// <summary>
        /// A file changed on the way fails, instead of decrypting to something.
        /// </summary>
        /// <remarks>
        /// The ciphertext lies on a storage host nobody in this conversation
        /// controls. Without the authentication tag that host could hand back
        /// anything and the recipient would take it for the file that was sent -
        /// which is the whole reason XEP-0454 prescribes AES-GCM and not a bare
        /// stream cipher.
        ///
        /// One flipped bit in the middle of the ciphertext, which is what a
        /// tampering host would do least conspicuously. AES-GCM has to notice.
        /// </remarks>
        [Test]
        public void ATamperedFileDoesNotDecrypt()
        {

            var reply    = Encrypt("The file as it was sent.");
            var url      = new Uri(reply.GetProperty("url").GetString()!);
            var payload  = Hex(reply, "payload");

            Assert.That(AesGcmUrl.TryParse(url, out var key, out var nonce, out _), Is.True);

            // Untouched it reads, so the tampering below is the only difference.
            Assert.That(() => AesGcmUrl.Decrypt(payload, key!, nonce!),
                        Throws.Nothing,
                        "The untouched payload already failed, so this test would prove nothing.");

            var tampered  = (Byte[]) payload.Clone();
            tampered[tampered.Length / 2] ^= 0x01;

            Assert.That(() => AesGcmUrl.Decrypt(tampered, key!, nonce!),
                        Throws.InstanceOf<CryptographicException>(),
                        "A single flipped bit was not noticed - the storage host could hand back " +
                        "whatever it liked.");

        }

        #endregion

        #region 4. The case of the hex

        /// <summary>
        /// Upper case hex in the fragment means the same as lower case.
        /// </summary>
        /// <remarks>
        /// XEP-0454 says hex and says nothing about the case, so both are sent.
        /// A reader that manages only one of them is wrong in a way nobody
        /// notices until it meets a client that chose the other - and then it
        /// looks like a broken file rather than like a parser.
        ///
        /// The two URLs come from one encryption, so the key and nonce behind
        /// them are the same bytes by construction: anything but equality here
        /// is the reader's doing.
        /// </remarks>
        [Test]
        public void TheFragmentIsReadInEitherCase()
        {

            var reply  = Encrypt("Case should not decide whether a file can be opened.");

            var lower  = new Uri(reply.GetProperty("url").      GetString()!);
            var upper  = new Uri(reply.GetProperty("url_upper").GetString()!);

            Assert.That(AesGcmUrl.TryParse(lower, out var keyLower, out var nonceLower, out var problemLower),
                        Is.True, $"The lower case fragment could not be read: {problemLower}");

            Assert.That(AesGcmUrl.TryParse(upper, out var keyUpper, out var nonceUpper, out var problemUpper),
                        Is.True, $"The upper case fragment could not be read: {problemUpper}");

            Assert.Multiple(() => {

                Assert.That(keyUpper,   Is.EqualTo(keyLower),
                            "The same key came out differently depending on the case of the hex.");

                Assert.That(nonceUpper, Is.EqualTo(nonceLower),
                            "The same nonce came out differently depending on the case of the hex.");

            });

        }

        #endregion

    }

}
