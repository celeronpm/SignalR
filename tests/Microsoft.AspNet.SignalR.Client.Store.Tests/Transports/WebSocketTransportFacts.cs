﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Microsoft.AspNet.SignalR.Client.Http;
using Microsoft.AspNet.SignalR.Client.Infrastructure;
using Microsoft.AspNet.SignalR.Client.Store.Tests;
using Microsoft.AspNet.SignalR.Client.Store.Tests.Fakes;
using System;
using System.Threading;
using Xunit;

namespace Microsoft.AspNet.SignalR.Client.Transports
{
    public class WebSocketTransportFacts
    {
        [Fact]
        public void CannotCreateWebSocketTransportWithNullHttpClient()
        {
            Assert.Equal(
                "httpClient",
                Assert.Throws<ArgumentNullException>(() => new WebSocketTransport(null)).ParamName);
        }

        [Fact]
        public void NameReturnsCorrectTransportName()
        {
            Assert.Equal("webSockets", new WebSocketTransport().Name);
        }

        [Fact]
        public void SupportsKeepAliveReturnsTrue()
        {
            Assert.True(new WebSocketTransport().SupportsKeepAlive);
        }

        [Fact]
        public async Task StartsValidatesInputParameters()
        {
            Assert.Equal("connection",
                (await Assert.ThrowsAsync<ArgumentNullException>(async () => await
                    new WebSocketTransport().Start(null, null, CancellationToken.None))).ParamName);
        }

        [Fact]
        public async Task StartCreatesAndOpensWebSocket()
        {
            var fakeWebSocketTransport = new FakeWebSocketTransport();

            fakeWebSocketTransport.Setup("OpenWebSocket", () =>
            {
                var tcs = new TaskCompletionSource<object>();
                tcs.TrySetResult(null);
                return tcs.Task;
            });

            var fakeConnection = new FakeConnection
            {
                TransportConnectTimeout = new TimeSpan(0, 0, 0, 0, 100),
                TotalTransportConnectTimeout = new TimeSpan(0, 0, 0, 0, 100),
                Url = "http://fake.url",
                Protocol = new Version(1, 42),
                ConnectionToken = "MyConnToken",
                MessageId = "MsgId"
            };

            // connect timeout unblocks this call hence the expected exception
            await Assert.ThrowsAsync<TimeoutException>(
                async () => await fakeWebSocketTransport.Start(fakeConnection, "test", CancellationToken.None));

            Assert.Equal(1, fakeConnection.GetInvocations("Trace").Count());
            Assert.Equal(1, fakeConnection.GetInvocations("PrepareRequest").Count());

            var openWebSocketInvocations = fakeWebSocketTransport.GetInvocations("OpenWebSocket").ToArray();
            Assert.Equal(1, openWebSocketInvocations.Length);
            Assert.StartsWith(
                "ws://fake.urlconnect/?clientProtocol=1.42&transport=webSockets&connectionData=test&connectionToken=MyConnToken&messageId=MsgId&noCache=",
                ((Uri)openWebSocketInvocations[0][1]).AbsoluteUri);
        }

        [Fact]
        public async Task InCaseOfExceptionStartInvokesOnFailureAndThrowsOriginalException()
        {
            var fakeConnection = new FakeConnection { TotalTransportConnectTimeout = new TimeSpan(1, 0, 0)};

            var initializationHandler = 
                new TransportInitializationHandler(new DefaultHttpClient(), fakeConnection, null,
                    "webSocks", CancellationToken.None, new TransportHelper());

            var onFailureInvoked = false;
            initializationHandler.OnFailure += () => onFailureInvoked = true;

            var fakeWebSocketTransport = new FakeWebSocketTransport();
            var expectedException = new Exception("OpenWebSocket failed.");
            fakeWebSocketTransport.Setup<Task>("OpenWebSocket", () =>
            {
                throw expectedException;
            });

            Assert.Same(expectedException,
                await Assert.ThrowsAsync<Exception>(
                    async () => await fakeWebSocketTransport.Start(fakeConnection, null, initializationHandler)));

            Assert.True(onFailureInvoked);
        }

        [Fact]
        public async Task StartInvokesOnFailureAndThrowsIfTaskCancelled()
        {
            var fakeConnection = new FakeConnection { TotalTransportConnectTimeout = new TimeSpan(1, 0, 0) };
            var cancellationTokenSource = new CancellationTokenSource();

            var initializationHandler =
                new TransportInitializationHandler(new DefaultHttpClient(), fakeConnection, null,
                    "webSocks", cancellationTokenSource.Token, new TransportHelper());

            var onFailureInvoked = false;
            initializationHandler.OnFailure += () => onFailureInvoked = true;

            var fakeWebSocketTransport = new FakeWebSocketTransport();
            fakeWebSocketTransport.Setup<Task>("OpenWebSocket", () =>
            {
                cancellationTokenSource.Cancel();

                var tcs = new TaskCompletionSource<object>();
                tcs.TrySetResult(null);
                return tcs.Task;
            });

            Assert.Equal(
                ResourceUtil.GetResource("Error_TransportFailedToConnect"),
                (await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await fakeWebSocketTransport.Start(fakeConnection, null, initializationHandler))).Message);

            Assert.True(onFailureInvoked);
        }

        [Fact]
        public void MessageReceivedReadsAndProcessesMessages()
        {
            var fakeDataReader = new FakeDataReader
            {
                UnicodeEncoding = (UnicodeEncoding)(-1),
                UnconsumedBufferLength = 42
            };
            fakeDataReader.Setup("ReadString", () => "MessageBody");

            var fakeWebSocketResponse = new FakeWebSocketResponse();
            fakeWebSocketResponse.Setup("GetDataReader", () => fakeDataReader);

            var fakeTransportHelper = new FakeTransportHelper();
            var transportInitialization = new TransportInitializationHandler(null, new FakeConnection(), 
                null, null, CancellationToken.None, fakeTransportHelper);

            new WebSocketTransport()
                .MessageReceived(fakeWebSocketResponse, fakeTransportHelper, transportInitialization);

            Assert.Equal(UnicodeEncoding.Utf8, fakeDataReader.UnicodeEncoding);
            fakeDataReader.Verify("ReadString", new List<object[]> {new object[] { (uint)42}});

            var processResponseInvocations = fakeTransportHelper.GetInvocations("ProcessResponse").ToArray();
            Assert.Equal(1, processResponseInvocations.Length);
            Assert.Equal("MessageBody", /* response */processResponseInvocations[0][1]);
        }
    }
}