﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using RockLib.Messaging.Internal;

namespace RockLib.Messaging.NamedPipes
{
    /// <summary>
    /// An implementation of <see cref="ISender"/> that uses named pipes as
    /// its communication mechanism.
    /// </summary>
    public class NamedPipeSender : ISender
    {
        private readonly NamedPipeMessageSerializer _serializer = NamedPipeMessageSerializer.Instance;
        private static readonly Task _completedTask = Task.FromResult(0);
        private readonly BlockingCollection<string> _messages;
        private readonly Thread _runThread;

        /// <summary>
        /// Initializes a new instance of the <see cref="NamedPipeSender"/> class.
        /// </summary>
        /// <param name="name">The name of this instance of <see cref="NamedPipeSender"/>.</param>
        /// <param name="pipeName">Name of the named pipe.</param>
        /// <param name="compressed">Whether messages should be compressed.</param>
        public NamedPipeSender(string name, string pipeName = null, bool compressed = false)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            PipeName = pipeName ?? Name;
            Compressed = compressed;

            _messages = new BlockingCollection<string>();

            _runThread = new Thread(Run);
            _runThread.Start();
        }

        /// <summary>
        /// Gets the name of this instance of <see cref="ISender" />.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the name of the named pipe.
        /// </summary>
        public string PipeName { get; }

        /// <summary>
        /// Gets a value indicating whether message bodies send from this sender should be compressed.
        /// </summary>
        public bool Compressed { get; }

        /// <summary>
        /// Sends the specified message.
        /// </summary>
        /// <param name="message">The message to send.</param>
        public Task SendAsync(ISenderMessage message)
        {
            var shouldCompress = message.ShouldCompress(Compressed);

            var stringValue = shouldCompress
                ? MessageCompression.Compress(message.StringValue)
                : message.StringValue;

            var namedPipeMessage = new NamedPipeMessage
            {
                StringValue = stringValue,
                MessageFormat = message.MessageFormat,
                Priority = message.Priority,
                Headers = new Dictionary<string, string>()
            };

            var originatingSystemAlreadyExists = false;

            foreach (var header in message.Headers)
            {
                if (header.Key == HeaderName.OriginatingSystem)
                {
                    originatingSystemAlreadyExists = true;
                }

                namedPipeMessage.Headers.Add(header.Key, header.Value);
            }

            namedPipeMessage.Headers[HeaderName.MessageFormat] = message.MessageFormat.ToString();

            if (!originatingSystemAlreadyExists)
            {
                namedPipeMessage.Headers[HeaderName.OriginatingSystem] = "NamedPipe";
            }

            if (shouldCompress)
            {
                namedPipeMessage.Headers[HeaderName.CompressedPayload] = "true";
            }

            var messageString = _serializer.SerializeToString(namedPipeMessage);
            _messages.Add(messageString);

            return _completedTask;
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            _messages.CompleteAdding();
            _runThread.Join();
        }

        private void Run()
        {
            foreach (var message in _messages.GetConsumingEnumerable())
            {
                try
                {
                    var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);

                    try
                    {
                        pipe.Connect(0);
                    }
                    catch (TimeoutException)
                    {
                        continue;
                    }

                    using (var writer = new StreamWriter(pipe))
                    {
                        writer.WriteLine(message);
                    }
                }
                catch (Exception)
                {
                    // TODO: Something?
                    continue;
                }
            }
        }
    }
}