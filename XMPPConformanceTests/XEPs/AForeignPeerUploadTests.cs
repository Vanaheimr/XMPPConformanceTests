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

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Ratatoskr;

#endregion

namespace org.GraphDefined.Vanaheimr.Ratatoskr.Tests
{

    /// <summary>
    /// XEP-0363 against an upload service nobody here wrote.
    /// </summary>
    /// <remarks>
    /// <b>The one extension in this suite whose interesting half is not XMPP.</b>
    /// The slot is asked for over the stream and the bytes go over HTTPS, and
    /// only both together are an upload - which means this is also the one
    /// extension that cannot be checked against a fixture at all. A test server
    /// that invents the far side can invent an IQ; it cannot invent an address
    /// that a second client then really fetches a file from.
    ///
    /// Two of the rounds go round the library on purpose and send the PUT by
    /// hand. They ask the question that decides whether an upload service is a
    /// service or a drop box for the whole internet: <b>is the slot the
    /// authorisation, or is the port?</b> Our own code cannot ask it, because it
    /// is written not to - it only ever sends to an address it was given.
    ///
    /// What is peculiar to a counterpart the derived classes lay down.
    /// </remarks>
    public abstract class AForeignPeerUploadTests : AForeignPeerTests
    {

        #region Helper functions

        /// <summary>
        /// Logs in and finds the upload service, or fails saying so.
        /// </summary>
        protected async Task<(XMPPClient Client, UploadService Service)> FindTheServiceAsync(String localPart = User)
        {

            var client   = await ConnectAsync(localPart);
            var service  = await client.DiscoverUploadServiceAsync();

            Assert.That(service, Is.Not.Null,
                        $"{PeerName} announces no upload service, so nothing that is not text can be sent at all.");

            return (client, service!);

        }

        /// <summary>
        /// The same URL with a different slot in it - an address that looks
        /// exactly right and was never issued.
        /// </summary>
        private static Uri WithAnotherSlot(Uri Url)
        {

            var builder = new UriBuilder(Url);
            var parts   = builder.Path.Split('/');

            // .../<slot>/<filename> is the shape both services use. The slot is
            // the one before the name; where there is no name, the last.
            if (parts.Length >= 3)
                parts[^2] = Guid.NewGuid().ToString("N");
            else
                parts[^1] = Guid.NewGuid().ToString("N");

            builder.Path = String.Join('/', parts);

            return builder.Uri;

        }

        private static Byte[] SomeBytes(Int32 howMany)
        {
            var bytes = new Byte[howMany];
            RandomNumberGenerator.Fill(bytes);
            return bytes;
        }

        #endregion


        #region 1. The service is where the specification says to look

        /// <summary>
        /// XEP-0363, section 4: finding the service at all.
        /// </summary>
        /// <remarks>
        /// <b>Not arrangeable from this side, and easy to get wrong in a way
        /// that never shows.</b> A client that guesses at <c>upload.</c> plus
        /// the domain works against both of these services and against nothing
        /// else; a client that only reads the host's own disco#info finds
        /// neither of them, because both put the service on a component.
        ///
        /// What is checked is the walk, not the name: the service has to turn up
        /// by asking the server what it carries and each of those what it does.
        /// </remarks>
        [Test]
        public async Task TheUploadServiceIsWhereTheSpecificationSaysToLook()
        {

            var (client, service) = await FindTheServiceAsync();

            Assert.Multiple(() =>
            {

                Assert.That(service.Address.ToString(), Is.Not.Empty);

                // And the announcement is really on it - the walk found it by
                // the feature, so asking again must give the same answer.
                Assert.That(client.Connection.Upload!.Searched, Is.True);

            });

            var info = await client.DiscoverInfoAsync(service.Address);

            Assert.That(info, Is.Not.Null,
                        "The service that was just found by its disco#info now answers none.");

            Assert.That(HttpFileUpload.Announces(info!), Is.True,
                        $"{PeerName} listed this service as an upload service and it does not say so itself.");

        }

        #endregion

        #region 2. The service says how large a file may be

        /// <summary>
        /// XEP-0363, section 4: the limit in the disco form (XEP-0128).
        /// </summary>
        /// <remarks>
        /// The number that decides whether a file can be offered at all, and the
        /// only one that can be known <em>before</em> reading it from disk.
        ///
        /// <b>A service is free to announce none</b>, and then the answer is
        /// null rather than infinity - which is what this checks: that a limit
        /// which is there is read, and that reading it produces a number and not
        /// a zero. Both set-ups announce one on purpose, small enough for round
        /// five to exceed it without moving ten megabytes through the loopback.
        /// </remarks>
        [Test]
        public async Task TheServiceSaysHowLargeAFileMayBe()
        {

            var (_, service) = await FindTheServiceAsync();

            Assert.That(service.MaxFileSize, Is.Not.Null,
                        $"{PeerName} announces no limit, so the set-up for this lane is not in place - " +
                        "see tools/*/setup.sh.");

            Assert.Multiple(() =>
            {

                Assert.That(service.MaxFileSize!.Value, Is.GreaterThan(0),
                            "A limit of zero would mean nothing can ever be sent.");

                Assert.That(service.IsTooLarge(service.MaxFileSize!.Value),     Is.False,
                            "The announced limit is described as too large for itself.");

                Assert.That(service.IsTooLarge(service.MaxFileSize!.Value + 1), Is.True);

            });

        }

        #endregion

        #region 3. A slot is two addresses, and the headers are not free

        /// <summary>
        /// XEP-0363, section 5: what comes back for a request.
        /// </summary>
        /// <remarks>
        /// Both URLs have to be there, and both have to be HTTPS - the address
        /// <em>is</em> the secret, so anything that carries it in the clear
        /// hands the file to whoever is listening.
        ///
        /// And the headers: the service names them, this client sends them, and
        /// that is one entity deciding what another puts into an HTTP request.
        /// Section 5 closes the list to three for exactly that reason. What is
        /// checked here is that nothing outside it survived the reading - the
        /// filter sits in <c>HttpFileUpload.ReadSlot</c>, and this is the only
        /// place where a real service's headers pass through it.
        /// </remarks>
        [Test]
        public async Task ASlotIsTwoAddressesAndTheHeadersAreNotFree()
        {

            var (client, service) = await FindTheServiceAsync();

            var outcome = await client.Connection.Upload!.RequestSlotAsync(
                              service.Address, "round3.txt", 11, "text/plain");

            Assert.That(outcome.Granted, Is.True,
                        $"{PeerName} gave no slot for eleven bytes: {outcome.Refusal}");

            var slot = outcome.Slot!;

            Assert.Multiple(() =>
            {

                Assert.That(slot.PutUrl.Scheme, Is.EqualTo("https"),
                            "The file would travel in the clear, and with it the address that is its only key.");

                Assert.That(slot.GetUrl.Scheme, Is.EqualTo("https"));

                Assert.That(slot.Headers.Select(h => h.Name),
                            Is.All.Matches<String>(HttpFileUpload.AllowedHeaders.Contains),
                            "A header outside the three of section 5 came through the reading.");

                Assert.That(slot.Headers.Select(h => h.Value),
                            Is.All.Matches<String>(v => !v.Contains('\r') && !v.Contains('\n')),
                            "A header value with a line break in it is a second header of somebody else's choosing.");

            });

        }

        #endregion

        #region 4. What goes up comes down

        /// <summary>
        /// The round the whole extension exists for.
        /// </summary>
        /// <remarks>
        /// <b>Both protocols, and nothing here can stand in for either.</b> The
        /// slot comes over XMPP, the bytes go over HTTPS to an address the
        /// service invented, and the file is then fetched back with no
        /// authentication at all - because the person a file is sent to is
        /// usually on another server and has no account here.
        ///
        /// Which is also why the bytes are random and compared exactly. A
        /// service that stored the right number of the wrong bytes, or that
        /// re-encoded them, would pass every check that only counted.
        /// </remarks>
        [Test]
        public async Task WhatGoesUpComesDown()
        {

            var (client, service) = await FindTheServiceAsync();

            var content = SomeBytes(4096);

            using var stream = new MemoryStream(content);

            var outcome = await client.Connection.Upload!.UploadAsync(
                              stream, content.Length, "round4.bin", "application/octet-stream",
                              service.Address);

            Assert.That(outcome.Uploaded, Is.True,
                        $"{PeerName} did not take the file: refusal {outcome.Refusal}, HTTP {outcome.HttpStatus}");

            var fetched = await client.DownloadFileAsync(outcome.Url!);

            Assert.That(fetched, Is.Not.Null,
                        "The address the service handed out gives nothing back, so nobody could ever be sent it.");

            Assert.That(fetched!, Is.EqualTo(content),
                        "What came back is not what went up.");

        }

        #endregion

        #region 5. A file over the limit is refused, and the refusal names the limit

        /// <summary>
        /// XEP-0363, section 5: <c>&lt;file-too-large/&gt;</c>.
        /// </summary>
        /// <remarks>
        /// <b>The refusal carries the answer.</b> A client that throws the error
        /// away and reports "upload failed" throws away the one number that
        /// would let the next attempt succeed - and a client that never read the
        /// disco form learns the limit here for the first time.
        ///
        /// Asked without a file: the request names a size, and a size is all the
        /// service needs to say no. That is the point of asking first.
        /// </remarks>
        [Test]
        public async Task AFileOverTheLimitIsRefusedAndTheLimitIsNamed()
        {

            var (client, service) = await FindTheServiceAsync();

            Assert.That(service.MaxFileSize, Is.Not.Null, "No announced limit to exceed.");

            var outcome = await client.Connection.Upload!.RequestSlotAsync(
                              service.Address,
                              "round5.bin",
                              service.MaxFileSize!.Value + 1_000_000,
                              "application/octet-stream");

            Assert.That(outcome.Granted, Is.False,
                        "The service handed out a slot for a file larger than the limit it announces, " +
                        "so the announcement means nothing.");

            Assert.Multiple(() =>
            {

                Assert.That(outcome.Refusal, Is.Not.Null,
                            "The service neither gave a slot nor said why.");

                Assert.That(outcome.MaxFileSize, Is.Not.Null,
                            "The refusal does not name the limit, so a client that never read the disco " +
                            "form has no way to find out what would fit.");

                Assert.That(outcome.TooLarge, Is.True);

            });

        }

        #endregion

        #region 6. A slot nobody issued is not a slot

        /// <summary>
        /// The question that decides what an upload service is.
        /// </summary>
        /// <remarks>
        /// <b>This is what "anonymous PUT" would mean, and why it must not be
        /// one.</b> The download has to be anonymous - there is nobody to ask.
        /// The upload must not be, or the service is a file drop for anybody who
        /// can reach the port, and its address will be found.
        ///
        /// Asked by hand, because the library cannot ask it: it only ever sends
        /// to an address it was handed. Two ways round, and a service has to
        /// refuse both:
        ///
        /// <list type="bullet">
        ///   <item>a plain PUT to an invented address, which is the naive
        ///         attempt;</item>
        ///   <item>the same address carrying the header from a slot that really
        ///         was issued - which tells apart a service that checks for a
        ///         token from one that checks the token belongs to <em>this</em>
        ///         slot.</item>
        /// </list>
        /// </remarks>
        [Test]
        public async Task ASlotNobodyIssuedIsNotASlot()
        {

            var (client, service) = await FindTheServiceAsync();

            var outcome = await client.Connection.Upload!.RequestSlotAsync(
                              service.Address, "round6.txt", 5, "text/plain");

            Assert.That(outcome.Granted, Is.True, $"No slot to work from: {outcome.Refusal}");

            var invented = WithAnotherSlot(outcome.Slot!.PutUrl);
            var http     = NewHttpClient();

            using (var bare = new HttpRequestMessage(HttpMethod.Put, invented) {
                                  Content = new ByteArrayContent(Encoding.UTF8.GetBytes("hello"))
                              })
            {

                using var answer = await http.SendAsync(bare);

                Assert.That((Int32) answer.StatusCode, Is.InRange(400, 499),
                            $"{PeerName} answered {(Int32) answer.StatusCode} to a PUT at an address it " +
                            "never handed out and with nothing to show for it. Anything but a refusal " +
                            "there is an open drop box, not an upload service.");

            }

            using (var borrowed = new HttpRequestMessage(HttpMethod.Put, invented) {
                                      Content = new ByteArrayContent(Encoding.UTF8.GetBytes("hello"))
                                  })
            {

                foreach (var header in outcome.Slot!.Headers)
                {
                    if (!borrowed.Headers.TryAddWithoutValidation(header.Name, header.Value))
                        borrowed.Content!.Headers.TryAddWithoutValidation(header.Name, header.Value);
                }

                using var answer = await http.SendAsync(borrowed);

                Assert.That((Int32) answer.StatusCode, Is.InRange(400, 499),
                            $"{PeerName} answered {(Int32) answer.StatusCode} to a slot's credentials used " +
                            "at a different slot's address. Accepting it would make one slot a key to all " +
                            "of them.");

            }

        }

        #endregion

        #region 7. A slot is for the size it was asked for

        /// <summary>
        /// More bytes than the slot was issued for.
        /// </summary>
        /// <remarks>
        /// <b>Without this the announced limit is decoration.</b> A service that
        /// hands out a slot for one byte and then takes a gigabyte has told the
        /// client a number that binds nobody. The size in the request is a
        /// promise, and a promise nobody checks is not one.
        ///
        /// Sent by hand again: the library sends exactly what it asked for, by
        /// construction - the Content-Length comes from the number in the
        /// request and not from the stream.
        /// </remarks>
        [Test]
        public async Task ASlotIsForTheSizeItWasAskedFor()
        {

            var (client, service) = await FindTheServiceAsync();

            var outcome = await client.Connection.Upload!.RequestSlotAsync(
                              service.Address, "round7.bin", 10, "application/octet-stream");

            Assert.That(outcome.Granted, Is.True, $"No slot to work from: {outcome.Refusal}");

            var tooMuch = SomeBytes(10_000);

            using var request = new HttpRequestMessage(HttpMethod.Put, outcome.Slot!.PutUrl) {
                                    Content = new ByteArrayContent(tooMuch)
                                };

            foreach (var header in outcome.Slot!.Headers)
            {
                if (!request.Headers.TryAddWithoutValidation(header.Name, header.Value))
                    request.Content!.Headers.TryAddWithoutValidation(header.Name, header.Value);
            }

            using var answer = await NewHttpClient().SendAsync(request);

            Assert.That((Int32) answer.StatusCode, Is.InRange(400, 499),
                        $"{PeerName} answered {(Int32) answer.StatusCode} to a thousand times the announced " +
                        "size in a slot issued for ten bytes. Taking it would make the limit it announces " +
                        "bind nobody."); 

        }

        #endregion

        #region 8. A file reaches somebody else as a file

        /// <summary>
        /// XEP-0363 with XEP-0066, all the way to a second person.
        /// </summary>
        /// <remarks>
        /// <b>The whole point, and the only round that involves two people.</b>
        /// The sender uploads and says where; the recipient has to recognise
        /// that the message is about a file rather than about its own text, and
        /// then fetch it - over HTTP, from a service they are not talking to,
        /// with nothing to identify themselves as.
        ///
        /// It is also where a quiet mistake would show. A client that reads the
        /// body and not the <c>&lt;x/&gt;</c> shows a person a URL; one that
        /// reads the <c>&lt;x/&gt;</c> without checking it against the body
        /// follows an address the person was never shown.
        /// </remarks>
        [Test]
        public async Task AFileReachesSomebodyElseAsAFile()
        {

            var (alice, _) = await FindTheServiceAsync();
            var bob        = await ConnectAsync(User2);

            var heard = new ConcurrentQueue<XMPPMessage>();
            bob.OnMessage += (t, s, m, ct) => { heard.Enqueue(m); return Task.CompletedTask; };

            var content = SomeBytes(2048);

            using var stream = new MemoryStream(content);

            var sent = await alice.SendFileAsync(bob.BareJid, stream, content.Length,
                                                 "round8.bin", "application/octet-stream");

            Assert.That(sent.Sent, Is.True,
                        $"Nothing was sent: {sent.Upload.Refusal}, HTTP {sent.Upload.HttpStatus}");

            await WaitFor(() => heard.Any(m => m.IsFile), "a message about a file reaching the other side");

            var message = heard.First(m => m.IsFile);

            Assert.Multiple(() =>
            {

                Assert.That(message.FileUrl, Is.EqualTo(sent.Url),
                            "The address that arrived is not the one that was sent.");

                Assert.That(message.Body, Is.EqualTo(sent.Url!.AbsoluteUri),
                            "The body says something other than the attachment does, so what is shown " +
                            "and what is opened are two different addresses.");

            });

            var fetched = await bob.DownloadFileAsync(message.FileUrl!);

            Assert.That(fetched, Is.Not.Null,
                        "The recipient cannot fetch the file, so the upload reached nobody.");

            Assert.That(fetched!, Is.EqualTo(content),
                        "What the recipient got is not what was sent.");

        }

        #endregion

        #region 9. A file that could not be sent is not announced

        /// <summary>
        /// The upload fails, and nothing is said.
        /// </summary>
        /// <remarks>
        /// <b>Written because a mutation survived.</b> The mutation took out the
        /// check that stops a message going out when the upload did not happen,
        /// and every round stayed green - because until this one every round
        /// sent a file that went up. Nothing was wrong with the code; the gap
        /// was that nothing had ever failed.
        ///
        /// What it protects against is worse than an error. A message naming an
        /// address the file never reached is indistinguishable from one that
        /// will be there in a moment: the person it was sent to keeps trying,
        /// and the person who sent it believes it arrived.
        ///
        /// The stream is empty on purpose. The refusal comes back from the slot
        /// request, before a single byte would have been read - which is the
        /// whole point of asking first.
        /// </remarks>
        [Test]
        public async Task AFileThatCouldNotBeUploadedIsNotAnnounced()
        {

            var (alice, service) = await FindTheServiceAsync();
            var bob              = await ConnectAsync(User2);

            var heard = new ConcurrentQueue<XMPPMessage>();
            bob.OnMessage += (t, s, m, ct) => { heard.Enqueue(m); return Task.CompletedTask; };

            Assert.That(service.MaxFileSize, Is.Not.Null, "No announced limit to exceed.");

            using var nothing = new MemoryStream();

            var sent = await alice.SendFileAsync(bob.BareJid,
                                                 nothing,
                                                 service.MaxFileSize!.Value + 1_000_000,
                                                 "round9.bin",
                                                 "application/octet-stream");

            Assert.Multiple(() =>
            {

                Assert.That(sent.Sent, Is.False,
                            "A file that was never uploaded was announced to somebody.");

                Assert.That(sent.Url, Is.Null);

                Assert.That(sent.Upload.MaxFileSize, Is.Not.Null,
                            "The caller is not told what would have fitted, so it cannot offer to try " +
                            "something smaller.");

            });

            // Nothing to wait for here - the point is that nothing comes. A
            // second is long enough: everything else on this connection crosses
            // the loopback in milliseconds, and round 8 shows a file message
            // doing exactly that.
            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.That(heard.Any(m => m.IsFile), Is.False,
                        "A message about a file arrived although no file was ever uploaded.");

        }

        #endregion

        #region 10. A file the service cannot read

        /// <summary>
        /// XEP-0454 over XEP-0363, against a service that really stores it.
        /// </summary>
        /// <remarks>
        /// <b>The one round in this repository where a file passes through a
        /// foreign server that cannot read it.</b> Everything else here the far
        /// side can see: a message, a room, an archive entry. The ciphertext it
        /// takes here it holds and hands back, and never learns what it was.
        ///
        /// The middle assertion is the whole point and would be easy to leave
        /// out: fetching the raw bytes with the <em>plain</em> address and
        /// showing they are not the plaintext. A round trip on its own passes
        /// just as well when nothing was encrypted at all - the bytes go up, the
        /// bytes come back, and only the step nobody checked was missing.
        ///
        /// And what the service is told is checked too. It gets a random name
        /// and <c>application/octet-stream</c>; a file called
        /// <c>passport.png</c> would say most of what the encryption was for.
        /// </remarks>
        [Test]
        public async Task AFileTheServiceCannotRead()
        {

            var (alice, service) = await FindTheServiceAsync();

            var secret = Encoding.UTF8.GetBytes("Nicht für den Server: äöüß 🎺");

            using var stream = new MemoryStream(secret);

            var encrypted = await alice.Connection.Upload!.UploadEncryptedAsync(
                                stream, "passport.png", service.Address);

            Assert.That(encrypted.Uploaded, Is.True,
                        $"{PeerName} did not take the ciphertext: {encrypted.Upload.Refusal}, " +
                        $"HTTP {encrypted.Upload.HttpStatus}");

            Assert.Multiple(() =>
            {

                Assert.That(AesGcmUrl.IsAesGcmUrl(encrypted.Url!), Is.True,
                            "What comes back has to be an aesgcm:// address, or nothing downstream " +
                            "knows there is a key on the end of it.");

                Assert.That(encrypted.Url!.AbsoluteUri,
                            Does.Not.Contain("passport"),
                            "The file name went to the service, and a name is often the whole secret.");

            });

            // What the service is actually holding, fetched without the key.
            var stored = await alice.DownloadFileAsync(AesGcmUrl.ToHttps(encrypted.Url!));

            Assert.That(stored, Is.Not.Null,
                        "The ciphertext cannot be fetched back at all.");

            Assert.Multiple(() =>
            {

                Assert.That(stored!, Is.Not.EqualTo(secret),
                            "The service is holding the plaintext, so nothing was encrypted.");

                Assert.That(stored!.Length, Is.EqualTo(secret.Length + 16),
                            "The stored file is not the ciphertext plus a 16 byte tag, which is the " +
                            "layout XEP-0454 prescribes.");

            });

            var read = await alice.DownloadEncryptedFileAsync(encrypted.Url!);

            Assert.That(read, Is.EqualTo(secret),
                        "What came back through the key is not what went in.");

        }

        #endregion

        #region 11. A slot is an address, and the address is the whole of the protection

        /// <summary>
        /// XEP-0363, security considerations: what holding the URL is worth.
        /// </summary>
        /// <remarks>
        /// <b>Both halves of this are the point of XEP-0454.</b> The lane has
        /// been putting files up and fetching them back since D119 and has never
        /// once asked who is allowed to - because the answer is nobody in
        /// particular, and that is the design:
        ///
        /// <blockquote>Anyone who knows the URL SHOULD be able to access
        /// it.</blockquote>
        ///
        /// So the file is guarded by the address and by nothing else, and the
        /// address travels in a message through servers this project does not
        /// own. That is the whole argument for encrypting before uploading
        /// (D69, D121), and it deserved one round that says it out loud instead
        /// of being folded into a remark.
        ///
        /// It is asked with a client that has no stream, no account and no
        /// standing of any kind - not the third account, which would be too
        /// much standing. The third account is on these servers; a passer-by
        /// with a URL is not.
        ///
        /// <b>The second half is pinned, not required.</b> XEP-0363 says nothing
        /// at all about using a slot twice. Both services refuse - ejabberd with
        /// 403, Prosody with 409 - and that is worth having written down,
        /// because the alternative is grim: a PUT URL and a GET URL that differ
        /// only in the verb mean anybody who was sent a file could quietly put
        /// something else in its place, under the address the recipient already
        /// trusts. A red here is a service that started allowing it.
        /// </remarks>
        [Test]
        public async Task ASlotIsAnAddressAndTheAddressIsTheWholeProtection()
        {

            var (client, service) = await FindTheServiceAsync();

            var content  = SomeBytes(2048);
            var outcome  = await client.Connection.Upload!.RequestSlotAsync(
                               service.Address, "round11.bin", content.Length, "application/octet-stream");

            Assert.That(outcome.Granted, Is.True, $"No slot to work from: {outcome.Refusal}");

            var slot = outcome.Slot!;
            var http = NewHttpClient();

            // By hand rather than through UploadAsync, because the same address
            // has to be used a second time afterwards.
            using (var put = new HttpRequestMessage(HttpMethod.Put, slot.PutUrl) {
                                 Content = new ByteArrayContent(content)
                             })
            {

                foreach (var header in slot.Headers)
                    if (!put.Headers.TryAddWithoutValidation(header.Name, header.Value))
                        put.Content!.Headers.TryAddWithoutValidation(header.Name, header.Value);

                using var answer = await http.SendAsync(put);

                Assert.That((Int32) answer.StatusCode, Is.InRange(200, 299),
                            $"{PeerName} would not take the file at the address it issued: " +
                            $"{(Int32) answer.StatusCode}");

            }

            // A passer-by: no stream, no account, nothing but the address.
            var passerBy = NewHttpClient();

            using (var get = new HttpRequestMessage(HttpMethod.Get, slot.GetUrl))
            {

                using var answer = await passerBy.SendAsync(get);

                Assert.That((Int32) answer.StatusCode, Is.InRange(200, 299),
                            $"{PeerName} answered {(Int32) answer.StatusCode} to somebody holding " +
                            "the address it handed out. A file nobody but the uploader can fetch " +
                            "is one that cannot be sent to anybody.");

                Assert.That(await answer.Content.ReadAsByteArrayAsync(), Is.EqualTo(content),
                            "What came back to the passer-by is not what went up.");

            }

            // And the same address a second time, with different bytes.
            var replacement = SomeBytes(2048);

            using (var again = new HttpRequestMessage(HttpMethod.Put, slot.PutUrl) {
                                   Content = new ByteArrayContent(replacement)
                               })
            {

                foreach (var header in slot.Headers)
                    if (!again.Headers.TryAddWithoutValidation(header.Name, header.Value))
                        again.Content!.Headers.TryAddWithoutValidation(header.Name, header.Value);

                using var answer = await http.SendAsync(again);

                Assert.That((Int32) answer.StatusCode, Is.InRange(400, 499),
                            $"{PeerName} answered {(Int32) answer.StatusCode} to a second PUT at a " +
                            "slot that had already been filled. XEP-0363 does not forbid it, so " +
                            "this is what both services did when the round was written and not a " +
                            "rule - but a service that starts allowing it lets anybody who was " +
                            "sent a file replace it under the address the recipient already trusts.");

            }

            // The bytes are still the first ones, which is the half a status
            // code does not prove: a refusal that had already written the file
            // would look exactly the same from here.
            using (var get = new HttpRequestMessage(HttpMethod.Get, slot.GetUrl))
            {

                using var answer = await passerBy.SendAsync(get);

                Assert.That(await answer.Content.ReadAsByteArrayAsync(), Is.EqualTo(content),
                            "The second PUT was refused and the file changed anyway, so the " +
                            "refusal is about the answer and not about the store.");

            }

        }

        #endregion

    }

}
